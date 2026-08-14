using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Application.Teams;

namespace CornersPredictionApi.ApiFootball;

public sealed class ApiFootballBotPickReconciliationService
{
    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "AET", "PEN"
    };

    private static readonly TimeZoneInfo SantiagoTimeZone = ResolveSantiagoTimeZone();

    private readonly ApiFootballClient _client;
    private readonly ApiFootballSyncService _syncService;
    private readonly IGetAutomatedCornerSelectionsUseCase _getSelectionsUseCase;
    private readonly IAutomatedCornerSelectionsRepository _selectionsRepository;
    private readonly IAutomatedBotPickSettlementUseCase _settlementUseCase;
    private readonly ILogger<ApiFootballBotPickReconciliationService> _logger;

    public ApiFootballBotPickReconciliationService(
        ApiFootballClient client,
        ApiFootballSyncService syncService,
        IGetAutomatedCornerSelectionsUseCase getSelectionsUseCase,
        IAutomatedCornerSelectionsRepository selectionsRepository,
        IAutomatedBotPickSettlementUseCase settlementUseCase,
        ILogger<ApiFootballBotPickReconciliationService> logger)
    {
        _client = client;
        _syncService = syncService;
        _getSelectionsUseCase = getSelectionsUseCase;
        _selectionsRepository = selectionsRepository;
        _settlementUseCase = settlementUseCase;
        _logger = logger;
    }

    public async Task<ApiFootballBotPickReconciliationResult> ReconcileAsync(
        ApiFootballBotPickReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SantiagoTimeZone));
        var dateTo = !request.DateTo.HasValue || request.DateTo > today
            ? today
            : request.DateTo.Value;
        if (request.DateFrom.HasValue && request.DateFrom > dateTo)
        {
            throw new ArgumentException("DateFrom cannot be greater than DateTo.");
        }

        var initialSettlement = await _settlementUseCase.SettleAsync(
            new AutomatedBotPickSettlementRequest(
                dateTo,
                request.DryRun,
                request.MaxSelections,
                BotKey: null,
                MarketFamily: null),
            cancellationToken);

        var pending = (await _getSelectionsUseCase.GetAsync(
                new AutomatedCornerSelectionsFilterRequest(
                    request.DateFrom?.ToDateTime(TimeOnly.MinValue),
                    dateTo.ToDateTime(TimeOnly.MinValue),
                    "Pending",
                    League: null,
                    Source: null,
                    MarketType: null,
                    OnlyPending: true),
                cancellationToken))
            .OrderBy(selection => selection.MatchDate)
            .ThenBy(selection => selection.AutomatedCornerBetSelectionId)
            .Take(request.MaxSelections)
            .ToArray();

        var states = pending.ToDictionary(
            selection => selection.AutomatedCornerBetSelectionId,
            selection => new ReconciliationRowState(selection));
        var fixturesById = new Dictionary<long, ApiFootballFixture>();
        var dateErrors = new Dictionary<DateOnly, string>();
        var fixtureDates = pending
            .Select(selection => DateOnly.FromDateTime(ToUtc(selection.MatchDate)))
            .Distinct()
            .Order()
            .ToArray();

        foreach (var date in fixtureDates)
        {
            try
            {
                var fixtures = ApiFootballSyncService.ParseFixtures(
                    await _client.GetFixturesForDateAsync(date, cancellationToken));
                foreach (var fixture in fixtures.Where(fixture => FinishedStatuses.Contains(fixture.Status)))
                {
                    fixturesById[fixture.FixtureId] = fixture;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                dateErrors[date] = exception.Message;
                _logger.LogWarning(exception, "Could not audit API-Football fixtures for {FixtureDate}", date);
            }
        }

        var storedFixtureIds = pending
            .Where(selection => selection.ApiFootballFixtureId.HasValue)
            .Select(selection => selection.ApiFootballFixtureId!.Value)
            .Distinct()
            .Where(fixtureId => !fixturesById.ContainsKey(fixtureId))
            .ToArray();
        foreach (var fixtureId in storedFixtureIds)
        {
            try
            {
                var fixture = ApiFootballSyncService.ParseFixtures(
                        await _client.GetFixtureAsync(fixtureId, cancellationToken))
                    .FirstOrDefault(row => row.FixtureId == fixtureId && FinishedStatuses.Contains(row.Status));
                if (fixture is not null)
                {
                    fixturesById[fixture.FixtureId] = fixture;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Could not audit stored API-Football fixture {FixtureId}", fixtureId);
            }
        }

        var matches = new List<MatchedSelection>();
        foreach (var selection in pending)
        {
            var state = states[selection.AutomatedCornerBetSelectionId];
            var expectedUtc = ToUtc(selection.MatchDate);
            var expectedDate = DateOnly.FromDateTime(expectedUtc);
            var resolution = ResolveFixture(selection, expectedUtc, fixturesById.Values);
            state.MatchStatus = resolution.Status;
            state.Confidence = resolution.Confidence;

            if (resolution.Fixture is null)
            {
                state.Result = resolution.Status;
                state.Message = dateErrors.TryGetValue(expectedDate, out var dateError)
                    ? $"No se pudo consultar API-Football para {expectedDate:yyyy-MM-dd}: {dateError}"
                    : resolution.Message;
                continue;
            }

            state.FixtureId = resolution.Fixture.FixtureId;
            state.Result = "Matched";
            state.Message = resolution.Message;
            matches.Add(new MatchedSelection(selection, resolution.Fixture));
        }

        var syncedFixtures = 0;
        var linkedSelections = 0;
        var missingMarketStatistics = 0;
        foreach (var fixtureGroup in matches.GroupBy(match => match.Fixture.FixtureId))
        {
            var fixture = fixtureGroup.First().Fixture;
            ApiFootballSettlementFixtureSyncResult sync;
            try
            {
                sync = await _syncService.SyncFixtureForSettlementAsync(
                    fixture,
                    fixtureGroup.Select(match => match.Selection.MarketType).Distinct().ToArray(),
                    request.DryRun,
                    cancellationToken);
                syncedFixtures++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Could not synchronize matched fixture {FixtureId}", fixture.FixtureId);
                foreach (var match in fixtureGroup)
                {
                    var failedState = states[match.Selection.AutomatedCornerBetSelectionId];
                    failedState.Result = "SyncError";
                    failedState.Message = exception.Message;
                }
                continue;
            }

            foreach (var match in fixtureGroup)
            {
                var state = states[match.Selection.AutomatedCornerBetSelectionId];
                state.MatchHistoryId = sync.MatchHistoryId;
                var hasMarketData = HasMarketData(match.Selection.MarketType, sync);
                if (!hasMarketData)
                {
                    missingMarketStatistics++;
                }

                if (request.DryRun)
                {
                    state.Result = hasMarketData ? "Ready" : "MissingStatistic";
                    state.Message = hasMarketData
                        ? "Coincidencia segura y dato requerido disponible; DryRun=true."
                        : "Partido final encontrado, pero API-Football no entrega la estadística requerida.";
                    continue;
                }

                if (!sync.MatchHistoryId.HasValue)
                {
                    state.Result = "SyncError";
                    state.Message = "La sincronización no devolvió MatchHistoryId.";
                    continue;
                }

                try
                {
                    var linked = await _selectionsRepository.LinkMatchAsync(
                        match.Selection.AutomatedCornerBetSelectionId,
                        sync.MatchHistoryId.Value,
                        fixture.FixtureId,
                        cancellationToken);
                    linkedSelections++;
                    state.MatchHistoryId = linked.MatchHistoryId;
                    state.Result = hasMarketData ? "Linked" : "LinkedMissingStatistic";
                    state.Message = hasMarketData
                        ? "MatchHistory enlazado; listo para liquidar."
                        : "MatchHistory enlazado, pero falta la estadística requerida.";
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    state.Result = "LinkError";
                    state.Message = exception.Message;
                }
            }
        }

        var finalSettlement = await _settlementUseCase.SettleAsync(
            new AutomatedBotPickSettlementRequest(
                dateTo,
                request.DryRun,
                request.MaxSelections,
                BotKey: null,
                MarketFamily: null),
            cancellationToken);
        var finalItems = finalSettlement.Items.ToDictionary(item => item.SelectionId);
        foreach (var state in states.Values)
        {
            if (!finalItems.TryGetValue(state.Selection.AutomatedCornerBetSelectionId, out var item))
            {
                continue;
            }

            state.MatchHistoryId = item.MatchHistoryId ?? state.MatchHistoryId;
            state.FixtureId = item.ApiFootballFixtureId ?? state.FixtureId;
            if (!item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                state.Result = item.Status;
                state.Message = item.Reason;
            }
            else if (state.Result is "Linked" or "LinkedMissingStatistic")
            {
                state.Result = "Pending";
                state.Message = item.Reason;
            }
        }

        return new ApiFootballBotPickReconciliationResult(
            request.DateFrom,
            dateTo,
            request.DryRun,
            initialSettlement.ReviewedRows,
            initialSettlement.SettledRows,
            pending.Length,
            fixtureDates.Length,
            fixturesById.Count,
            matches.Count,
            matches.Select(match => match.Fixture.FixtureId).Distinct().Count(),
            syncedFixtures,
            linkedSelections,
            states.Values.Count(state => state.Result == "NotFound"),
            states.Values.Count(state => state.Result == "Ambiguous"),
            missingMarketStatistics,
            finalSettlement.ReviewedRows,
            finalSettlement.SettledRows,
            finalSettlement.WonRows,
            finalSettlement.LostRows,
            finalSettlement.PushRows,
            finalSettlement.StillPendingRows,
            _client.DailyRemaining,
            _client.MinuteRemaining,
            states.Values.Select(state => state.ToResult()).ToArray());
    }

    private static FixtureResolution ResolveFixture(
        AutomatedCornerSelectionDto selection,
        DateTime expectedUtc,
        IEnumerable<ApiFootballFixture> fixtures)
    {
        if (selection.ApiFootballFixtureId.HasValue)
        {
            var stored = fixtures.FirstOrDefault(fixture =>
                fixture.FixtureId == selection.ApiFootballFixtureId.Value);
            if (stored is not null)
            {
                return new FixtureResolution(
                    stored,
                    "StoredFixtureId",
                    1,
                    "Coincidencia por ApiFootballFixtureId almacenado.");
            }
        }

        var candidates = fixtures
            .Select(fixture => BuildCandidate(selection, expectedUtc, fixture))
            .Where(candidate => candidate is not null)
            .Cast<FixtureCandidate>()
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.TimeDistanceHours)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new FixtureResolution(
                null,
                "NotFound",
                null,
                "No existe un fixture finalizado con equipos y hora suficientemente equivalentes.");
        }

        if (candidates.Length > 1)
        {
            var first = candidates[0];
            var second = candidates[1];
            if (first.Confidence - second.Confidence < 0.02 &&
                Math.Abs(first.TimeDistanceHours - second.TimeDistanceHours) < 1)
            {
                return new FixtureResolution(
                    null,
                    "Ambiguous",
                    first.Confidence,
                    $"Hay más de un fixture candidato equivalente ({first.Fixture.FixtureId}, {second.Fixture.FixtureId}).");
            }
        }

        var best = candidates[0];
        return new FixtureResolution(
            best.Fixture,
            "TeamsAndKickoff",
            best.Confidence,
            $"Coincidencia única por equipos y hora; diferencia {best.TimeDistanceHours:0.##} h.");
    }

    private static FixtureCandidate? BuildCandidate(
        AutomatedCornerSelectionDto selection,
        DateTime expectedUtc,
        ApiFootballFixture fixture)
    {
        var timeDistanceHours = Math.Abs((fixture.Date.UtcDateTime - expectedUtc).TotalHours);
        if (timeDistanceHours > 12)
        {
            return null;
        }

        var home = TeamNameMatcher.FindBestMatch(
            selection.StandardizedHomeTeam ?? selection.HomeTeam,
            [fixture.HomeTeam]);
        var away = TeamNameMatcher.FindBestMatch(
            selection.StandardizedAwayTeam ?? selection.AwayTeam,
            [fixture.AwayTeam]);
        if (home is null || away is null)
        {
            return null;
        }

        var confidence = Math.Min(home.Confidence, away.Confidence);
        if (confidence < 0.93)
        {
            return null;
        }

        return new FixtureCandidate(fixture, confidence, timeDistanceHours);
    }

    private static bool HasMarketData(
        string marketType,
        ApiFootballSettlementFixtureSyncResult sync) => marketType switch
    {
        "TotalGoals" or "HomeTeamGoals" or "AwayTeamGoals" => sync.HasGoals,
        "TotalCorners" or "HomeTeamCorners" or "AwayTeamCorners" => sync.HasCorners,
        "TotalShots" or "HomeTeamShots" or "AwayTeamShots" => sync.HasShots,
        "TotalShotsOnGoal" or "HomeTeamShotsOnGoal" or "AwayTeamShotsOnGoal" => sync.HasShotsOnGoal,
        _ => false
    };

    private static DateTime ToUtc(DateTime localMatchDate)
    {
        var unspecified = DateTime.SpecifyKind(localMatchDate, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, SantiagoTimeZone);
    }

    private static string ResolveBotKey(AutomatedCornerSelectionDto selection)
    {
        var version = selection.AutomationVersion.Trim().ToUpperInvariant();
        if (version.EndsWith("-C2026", StringComparison.Ordinal)) return "C2026";
        if (version.EndsWith("-D2026", StringComparison.Ordinal)) return "D2026";
        if (version.EndsWith("-E2026", StringComparison.Ordinal)) return "E2026";
        if (version.EndsWith("-F2026", StringComparison.Ordinal)) return "F2026";
        if (version.EndsWith("-A", StringComparison.Ordinal)) return "A";
        if (version.EndsWith("-B", StringComparison.Ordinal)) return "B";
        return "Legacy";
    }

    private static TimeZoneInfo ResolveSantiagoTimeZone()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static void Validate(ApiFootballBotPickReconciliationRequest request)
    {
        if (request.MaxSelections is < 1 or > 20000)
        {
            throw new ArgumentException("MaxSelections must be between 1 and 20000.");
        }
        if (request.DateFrom.HasValue && request.DateTo.HasValue && request.DateFrom > request.DateTo)
        {
            throw new ArgumentException("DateFrom cannot be greater than DateTo.");
        }
    }

    private sealed record FixtureCandidate(
        ApiFootballFixture Fixture,
        double Confidence,
        double TimeDistanceHours);

    private sealed record FixtureResolution(
        ApiFootballFixture? Fixture,
        string Status,
        double? Confidence,
        string Message);

    private sealed record MatchedSelection(
        AutomatedCornerSelectionDto Selection,
        ApiFootballFixture Fixture);

    private sealed class ReconciliationRowState
    {
        public ReconciliationRowState(AutomatedCornerSelectionDto selection)
        {
            Selection = selection;
        }

        public AutomatedCornerSelectionDto Selection { get; }
        public long? FixtureId { get; set; }
        public long? MatchHistoryId { get; set; }
        public string MatchStatus { get; set; } = "NotAudited";
        public double? Confidence { get; set; }
        public string Result { get; set; } = "Pending";
        public string Message { get; set; } = string.Empty;

        public ApiFootballBotPickReconciliationRow ToResult() => new(
            Selection.AutomatedCornerBetSelectionId,
            ResolveBotKey(Selection),
            Selection.MarketType,
            Selection.MatchDate,
            Selection.HomeTeam,
            Selection.AwayTeam,
            FixtureId,
            MatchHistoryId,
            MatchStatus,
            Confidence,
            Result,
            Message);
    }
}
