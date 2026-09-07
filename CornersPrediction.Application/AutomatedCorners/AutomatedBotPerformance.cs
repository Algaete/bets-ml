using System.Text.Json;

namespace CornersPrediction.Application.AutomatedCorners;

public sealed record AutomatedBotPerformanceScorecard
{
    public int WindowDays { get; init; }
    public DateTime DateFromUtc { get; init; }
    public DateTime DateToUtc { get; init; }
    public string Dimension { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public string? BotKey { get; init; }
    public string? MarketFamily { get; init; }
    public string? MarketType { get; init; }
    public string? SelectedSide { get; init; }
    public string? Bookmaker { get; init; }
    public string? AutomationVersion { get; init; }
    public int Total { get; init; }
    public int Resolved { get; init; }
    public int PredictiveResolved { get; init; }
    public int PredictiveFixtures { get; init; }
    public int ScientificPredictiveResolved { get; init; }
    public int PublishedPredictiveResolved { get; init; }
    public string EvidenceBasis { get; init; } = "PublishedSelections";
    public decimal SettledStake { get; init; }
    public decimal ProfitLoss { get; init; }
    public decimal? Yield { get; init; }
    public double? ObservedWinRate { get; init; }
    public double? AverageModelProbability { get; init; }
    public double? AverageMarketProbability { get; init; }
    public double? CalibrationGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public double? AverageEdge { get; init; }
    public string TrafficLight { get; init; } = "Gray";
    public bool ProductionBlocked { get; init; }
    public string Recommendation { get; init; } = string.Empty;
}

public interface IAutomatedBotPerformanceService
{
    Task<IReadOnlyList<AutomatedBotPerformanceScorecard>> GetScorecardsAsync(
        CancellationToken cancellationToken);
}

public sealed record AutomatedBotProductionEligibility(
    bool CanPublish,
    string Reason,
    AutomatedBotPerformanceScorecard? MarketScorecard = null,
    AutomatedBotPerformanceScorecard? FamilyScorecard = null,
    string? Tier = null,
    decimal MaxStakeUnits = 0m);

/// <summary>
/// Fail-closed production policy. A candidate can keep being audited in shadow when
/// this policy rejects it; only a prospectively Green segment or a tightly capped,
/// healthy GOALS controlled trial may reach the published selections table.
/// </summary>
public static class AutomatedBotProductionEligibilityPolicy
{
    public const int RequiredWindowDays = 30;
    public const int MinimumPredictiveFixtures = 100;
    public const int MinimumControlledTrialFixtures = 30;
    public const decimal MinimumControlledTrialYield = 0.07m;
    public const decimal GreenMaxStakeUnits = 1m;
    public const decimal ControlledTrialMaxStakeUnits = 0.5m;

    public static AutomatedBotProductionEligibility Evaluate(
        IReadOnlyCollection<AutomatedBotPerformanceScorecard> scorecards,
        string botKey,
        string marketFamily,
        string marketType,
        string selectedSide,
        string bookmaker,
        string automationVersion,
        decimal line,
        DateTime oddsTimestampUtc,
        DateTime predictionTimestampUtc,
        int maximumOddsAgeMinutes = 120,
        bool immutableOddsSnapshotAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(scorecards);
        botKey = NormalizeBotKey(botKey);
        marketFamily = marketFamily.Trim().ToUpperInvariant();
        marketType = marketType.Trim();
        selectedSide = selectedSide.Trim();
        automationVersion = automationVersion.Trim();

        if (marketType.Equals("HomeTeamCorners", StringComparison.OrdinalIgnoreCase))
            return Block("HomeTeamCorners permanece pausado por rendimiento negativo validado.");
        if (marketType.Equals("TotalGoals", StringComparison.OrdinalIgnoreCase))
            return Block("TotalGoals permanece pausado por rendimiento negativo validado.");
        if (botKey.Equals("F2026", StringComparison.OrdinalIgnoreCase)
            && marketFamily.Equals("CORNERS", StringComparison.OrdinalIgnoreCase))
            return Block("F2026 · CORNERS permanece pausado por rendimiento negativo validado.");
        if (!IsHalfLine(line))
            return Block("Las líneas .0/.25/.75 siguen en shadow hasta usar EV asiático de cinco estados.");
        if (!immutableOddsSnapshotAvailable)
            return Block("No existe un snapshot inmutable y bilateral de las cuotas usadas por la decisión.");

        var oddsAge = predictionTimestampUtc - oddsTimestampUtc;
        if (oddsAge < TimeSpan.Zero || oddsAge > TimeSpan.FromMinutes(Math.Max(1, maximumOddsAgeMinutes)))
            return Block($"Cuota fuera de la ventana de frescura productiva ({Math.Max(1, maximumOddsAgeMinutes)} min).");

        var market = scorecards
            .Where(row => row.WindowDays == RequiredWindowDays
                && row.Dimension.Equals("BotMarketSideVersion", StringComparison.OrdinalIgnoreCase)
                && NormalizeBotKey(row.BotKey) == botKey
                && string.Equals(row.MarketType, marketType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.SelectedSide, selectedSide, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.AutomationVersion, automationVersion, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.PredictiveFixtures)
            .FirstOrDefault();
        if (market is null)
            return Block("No existe scorecard productivo de 30 días para esta versión del bot/mercado/lado.");

        var family = scorecards
            .Where(row => row.WindowDays == RequiredWindowDays
                && row.Dimension.Equals("BotFamily", StringComparison.OrdinalIgnoreCase)
                && NormalizeBotKey(row.BotKey) == botKey
                && string.Equals(row.MarketFamily, marketFamily, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.PredictiveFixtures)
            .FirstOrDefault();

        // Bookmaker is diagnostic only: the actual odds still pass the model's
        // price/EV checks, freshness and immutable-snapshot requirements.
        // GOALS is evaluated by the exact market/side/version segment.
        // A BotFamily aggregate can otherwise mix a damaged TotalGoals segment with
        // a healthy HomeTeamGoals/AwayTeamGoals segment and veto the latter.
        if (!marketFamily.Equals("GOALS", StringComparison.OrdinalIgnoreCase)
            && (family?.TrafficLight.Equals("Red", StringComparison.OrdinalIgnoreCase) == true
                || family?.ProductionBlocked == true))
            return Block("La familia completa del bot está en semáforo rojo.", market, family);

        var isControlledTrialSignal = IsControlledTrialGoalsSignal(
            botKey,
            marketFamily,
            marketType,
            selectedSide);
        if (isControlledTrialSignal)
        {
            if (market.PredictiveFixtures >= MinimumControlledTrialFixtures
                && market.Yield is >= MinimumControlledTrialYield
                && market.CalibrationGap is not null
                && Math.Abs(market.CalibrationGap.Value) <= 0.05d
                && market.DeltaBrier is <= 0d
                && !market.ProductionBlocked)
            {
                return Allow(
                    "ControlledTrial",
                    ControlledTrialMaxStakeUnits,
                    $"Elegible: prueba controlada GOALS 30d con {market.PredictiveFixtures} partidos independientes y yield de al menos {MinimumControlledTrialYield:P0}; máximo {ControlledTrialMaxStakeUnits:0.##}u.",
                    market,
                    family);
            }

            return Block(
                $"La cohorte controlada C/F no cumple muestra, yield mínimo de {MinimumControlledTrialYield:P0}, calibración o Brier; no puede saltar automáticamente a Green.",
                market,
                family);
        }

        if (market.PredictiveFixtures >= MinimumPredictiveFixtures
            && market.TrafficLight.Equals("Green", StringComparison.OrdinalIgnoreCase)
            && !market.ProductionBlocked)
        {
            return Allow(
                "Green",
                GreenMaxStakeUnits,
                $"Elegible: Green 30d con {market.PredictiveFixtures} partidos independientes; máximo {GreenMaxStakeUnits:0.##}u.",
                market,
                family);
        }

        // The only controlled-trial cohort returned above. Every other market,
        // including home goals, needs its own 100-fixture Green segment; suggesting
        // a 30-fixture trial here made the missing market coverage misleading.
        if (market.PredictiveFixtures < MinimumPredictiveFixtures)
            return Block($"Muestra insuficiente ({market.PredictiveFixtures}/{MinimumPredictiveFixtures} partidos independientes para Green). Este segmento no pertenece a la prueba controlada de goles visita.", market, family);

        return Block(
            $"Semáforo {market.TrafficLight}: el segmento exacto no cumple los criterios Green.",
            market,
            family);
    }

    private static AutomatedBotProductionEligibility Allow(
        string tier,
        decimal maxStakeUnits,
        string reason,
        AutomatedBotPerformanceScorecard market,
        AutomatedBotPerformanceScorecard? family) =>
        new(true, reason, market, family, tier, maxStakeUnits);

    private static AutomatedBotProductionEligibility Block(
        string reason,
        AutomatedBotPerformanceScorecard? market = null,
        AutomatedBotPerformanceScorecard? family = null) => new(false, reason, market, family);

    private static bool IsControlledTrialGoalsSignal(
        string botKey,
        string marketFamily,
        string marketType,
        string selectedSide) =>
        (botKey.Equals("C2026", StringComparison.OrdinalIgnoreCase)
            || botKey.Equals("F2026", StringComparison.OrdinalIgnoreCase))
        && marketFamily.Equals("GOALS", StringComparison.OrdinalIgnoreCase)
        && marketType.Equals("AwayTeamGoals", StringComparison.OrdinalIgnoreCase)
        && selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase);

    private static bool IsHalfLine(decimal line)
    {
        var fractionalHundredths = Math.Abs(decimal.ToInt32(decimal.Round(line * 100m, 0))) % 100;
        return fractionalHundredths == 50;
    }

    private static string NormalizeBotKey(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "C" => "C2026",
        "D" => "D2026",
        "E" => "E2026",
        "F" => "F2026",
        "G" => "G2026",
        "H" => "H2026",
        { Length: > 0 } normalized => normalized,
        _ => string.Empty
    };
}

public sealed class AutomatedBotPerformanceService : IAutomatedBotPerformanceService
{
    private static readonly int[] Windows = [7, 30, 90];
    private readonly IAutomatedCornerSelectionsRepository _repository;
    private readonly IAutomatedBotPerformanceEvidenceRepository? _evidenceRepository;

    public AutomatedBotPerformanceService(IAutomatedCornerSelectionsRepository repository) =>
        _repository = repository;

    public AutomatedBotPerformanceService(
        IAutomatedCornerSelectionsRepository repository,
        IAutomatedBotPerformanceEvidenceRepository evidenceRepository)
    {
        _repository = repository;
        _evidenceRepository = evidenceRepository;
    }

    public async Task<IReadOnlyList<AutomatedBotPerformanceScorecard>> GetScorecardsAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var selectionsTask = _repository is IAutomatedBotPerformanceSelectionsRepository performanceSelections
            ? performanceSelections.GetPerformanceSelectionsAsync(
                now.Date.AddDays(-Windows.Max()),
                now.Date.AddDays(1),
                cancellationToken)
            : _repository.GetSelectionsAsync(
            new AutomatedCornerSelectionsFilterRequest(
                now.Date.AddDays(-Windows.Max()),
                now.Date.AddDays(1),
                null,
                null,
                null,
                null,
                false),
            cancellationToken);
        var evidenceTask = _evidenceRepository?.GetSettledEvidenceAsync(
            now.Date.AddDays(-Windows.Max()),
            now,
            now,
            cancellationToken) ?? Task.FromResult<IReadOnlyList<AutomatedBotPerformanceEvidence>>([]);

        await Task.WhenAll(selectionsTask, evidenceTask);

        var selectionRows = selectionsTask.Result.Select(row => new PerformanceRow(
            row,
            ResolveBotKey(row),
            ResolveFamily(row.MarketType),
            ResolveMarketProbability(row),
            false)).ToArray();
        var scientificRows = evidenceTask.Result
            .Where(row => IsSafeScientificEvidence(row, now))
            .Select(ToPerformanceRow)
            .ToArray();
        var enriched = selectionRows.Concat(scientificRows).ToArray();
        var output = new List<AutomatedBotPerformanceScorecard>();

        foreach (var window in Windows)
        {
            var from = now.AddDays(-window);
            var rawScoped = enriched
                .Where(row => row.Selection.MatchDate >= from && row.Selection.MatchDate <= now)
                .ToArray();
            var scientificSegments = rawScoped
                .Where(row => row.IsScientificEvidence)
                .Select(ExactSegmentKey)
                .ToHashSet(StringComparer.Ordinal);
            var scoped = rawScoped
                .Where(row => row.IsScientificEvidence
                    || !scientificSegments.Contains(ExactSegmentKey(row)))
                .ToArray();
            Add(output, window, from, now, "Overall", "Todos los bots", null, null, null, scoped);

            foreach (var group in scoped.GroupBy(row => row.BotKey).OrderBy(group => group.Key))
                Add(output, window, from, now, "Bot", group.Key, group.Key, null, null, group);
            foreach (var group in scoped.GroupBy(row => row.Family).OrderBy(group => group.Key))
                Add(output, window, from, now, "Family", group.Key, null, group.Key, null, group);
            foreach (var group in scoped.GroupBy(row => (row.BotKey, row.Family)).OrderBy(group => group.Key.BotKey).ThenBy(group => group.Key.Family))
                Add(output, window, from, now, "BotFamily", $"{group.Key.BotKey} · {group.Key.Family}", group.Key.BotKey, group.Key.Family, null, group);
            foreach (var group in scoped.GroupBy(row => (row.BotKey, row.Selection.MarketType)).OrderBy(group => group.Key.BotKey).ThenBy(group => group.Key.MarketType))
                Add(output, window, from, now, "BotMarketType", $"{group.Key.BotKey} · {group.Key.MarketType}", group.Key.BotKey, ResolveFamily(group.Key.MarketType), group.Key.MarketType, group);
            foreach (var group in scoped
                         .Where(row => IsProductCompatibleHalfLine(row.Selection.LineValue))
                         .GroupBy(row => (row.BotKey, row.Selection.MarketType, row.Selection.SelectedSide))
                         .OrderBy(group => group.Key.BotKey)
                         .ThenBy(group => group.Key.MarketType)
                         .ThenBy(group => group.Key.SelectedSide))
            {
                Add(
                    output,
                    window,
                    from,
                    now,
                    "BotMarketSide",
                    $"{group.Key.BotKey} · {group.Key.MarketType} · {group.Key.SelectedSide}",
                    group.Key.BotKey,
                    ResolveFamily(group.Key.MarketType),
                    group.Key.MarketType,
                    group,
                    group.Key.SelectedSide);
            }
            foreach (var group in scoped
                         .Where(row => IsProductCompatibleHalfLine(row.Selection.LineValue))
                         .GroupBy(row => (
                             row.BotKey,
                             row.Selection.MarketType,
                             row.Selection.SelectedSide,
                             row.Selection.AutomationVersion))
                         .OrderBy(group => group.Key.BotKey)
                         .ThenBy(group => group.Key.MarketType)
                         .ThenBy(group => group.Key.SelectedSide)
                         .ThenBy(group => group.Key.AutomationVersion))
            {
                // Build from observations, never by adding bookmaker scorecards.
                // Scientific coverage is independent of publication at every house.
                var evidence = group.Any(row => row.IsScientificEvidence)
                    ? group.Where(row => row.IsScientificEvidence)
                    : group;
                Add(
                    output,
                    window,
                    from,
                    now,
                    "BotMarketSideVersion",
                    $"{group.Key.BotKey} · {group.Key.MarketType} · {group.Key.SelectedSide} · {group.Key.AutomationVersion}",
                    group.Key.BotKey,
                    ResolveFamily(group.Key.MarketType),
                    group.Key.MarketType,
                    evidence,
                    group.Key.SelectedSide,
                    automationVersion: group.Key.AutomationVersion);
            }
            foreach (var group in scoped
                         .Where(row => IsProductCompatibleHalfLine(row.Selection.LineValue))
                         .GroupBy(row => (
                             row.BotKey,
                             row.Selection.MarketType,
                             row.Selection.SelectedSide,
                             row.Selection.Source))
                         .OrderBy(group => group.Key.BotKey)
                         .ThenBy(group => group.Key.MarketType)
                         .ThenBy(group => group.Key.SelectedSide)
                         .ThenBy(group => group.Key.Source))
            {
                Add(
                    output,
                    window,
                    from,
                    now,
                    "BotMarketSideBookmaker",
                    $"{group.Key.BotKey} · {group.Key.MarketType} · {group.Key.SelectedSide} · {group.Key.Source}",
                    group.Key.BotKey,
                    ResolveFamily(group.Key.MarketType),
                    group.Key.MarketType,
                    group,
                    group.Key.SelectedSide,
                    group.Key.Source);
            }
            foreach (var group in scoped
                         .Where(row => IsProductCompatibleHalfLine(row.Selection.LineValue))
                         .GroupBy(row => (
                             row.BotKey,
                             row.Selection.MarketType,
                             row.Selection.SelectedSide,
                             row.Selection.Source,
                             row.Selection.AutomationVersion))
                         .OrderBy(group => group.Key.BotKey)
                         .ThenBy(group => group.Key.MarketType)
                         .ThenBy(group => group.Key.SelectedSide)
                         .ThenBy(group => group.Key.Source)
                         .ThenBy(group => group.Key.AutomationVersion))
            {
                Add(
                    output,
                    window,
                    from,
                    now,
                    "BotMarketSideBookmakerVersion",
                    $"{group.Key.BotKey} · {group.Key.MarketType} · {group.Key.SelectedSide} · {group.Key.Source} · {group.Key.AutomationVersion}",
                    group.Key.BotKey,
                    ResolveFamily(group.Key.MarketType),
                    group.Key.MarketType,
                    group,
                    group.Key.SelectedSide,
                    group.Key.Source,
                    group.Key.AutomationVersion);
            }
        }

        return output;
    }

    private static void Add(
        ICollection<AutomatedBotPerformanceScorecard> output,
        int window,
        DateTime from,
        DateTime to,
        string dimension,
        string segment,
        string? botKey,
        string? family,
        string? marketType,
        IEnumerable<PerformanceRow> source,
        string? selectedSide = null,
        string? bookmaker = null,
        string? automationVersion = null)
    {
        var rawRows = source.ToArray();
        var isProductionSegment = dimension.Equals("BotMarketSideVersion", StringComparison.Ordinal);
        var rows = dimension.StartsWith("Bot", StringComparison.OrdinalIgnoreCase)
            ? rawRows
                .GroupBy(PerformanceFixtureKey, StringComparer.Ordinal)
                .Select(group => isProductionSegment
                    // Freeze the first decision, with a stable identity tie-break.
                    // Later retries, prices and settlement order cannot choose a
                    // more favorable observation for the production scorecard.
                    ? group.OrderBy(row => row.Selection.CreatedAtUtc)
                        .ThenBy(row => row.Selection.AutomatedCornerBetSelectionId)
                        .First()
                    : group.OrderByDescending(row => row.Selection.UpdatedAtUtc)
                        .ThenByDescending(row => row.Selection.AutomatedCornerBetSelectionId)
                        .First())
                .ToArray()
            : rawRows;
        // The production segment reports independent observations throughout.
        // Existing diagnostic dimensions retain their raw observation counters.
        // Selecting a whole row also keeps its odds, line, outcome and P/L together.
        var countRows = isProductionSegment ? rows : rawRows;
        var rawPredictiveResolved = countRows.Count(row => IsBinaryResolved(row.Selection.Status)
            && row.Selection.ModelProbability is > 0m and < 1m);
        var scientificPredictiveResolved = countRows.Count(row => row.IsScientificEvidence
            && IsBinaryResolved(row.Selection.Status)
            && row.Selection.ModelProbability is > 0m and < 1m);
        var publishedPredictiveResolved = rawPredictiveResolved - scientificPredictiveResolved;
        var resolved = rows.Where(row => IsResolved(row.Selection.Status)).ToArray();
        var predictive = rows.Where(row => IsBinaryResolved(row.Selection.Status)
            && row.Selection.ModelProbability is > 0m and < 1m).ToArray();
        var predictiveFixtures = predictive
            .Select(PerformanceFixtureKey)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var stake = resolved.Where(row => !row.Selection.Status.Equals("Void", StringComparison.OrdinalIgnoreCase))
            .Sum(row => row.Selection.Stake);
        var profitLoss = resolved.Sum(row => row.Selection.ProfitLoss ?? 0m);
        decimal? yield = stake > 0m ? profitLoss / stake : null;
        double? observed = predictive.Length == 0
            ? null
            : predictive.Average(row => row.Selection.Status.Equals("Won", StringComparison.OrdinalIgnoreCase) ? 1d : 0d);
        double? model = predictive.Length == 0 ? null : predictive.Average(row => (double)row.Selection.ModelProbability!.Value);
        var marketRows = predictive.Where(row => row.MarketProbability is > 0d and < 1d).ToArray();
        double? market = marketRows.Length == 0 ? null : marketRows.Average(row => row.MarketProbability!.Value);
        double? brier = predictive.Length == 0 ? null : predictive.Average(row =>
        {
            var outcome = row.Selection.Status.Equals("Won", StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
            var probability = (double)row.Selection.ModelProbability!.Value;
            return Math.Pow(probability - outcome, 2d);
        });
        double? marketBrier = marketRows.Length == 0 ? null : marketRows.Average(row =>
        {
            var outcome = row.Selection.Status.Equals("Won", StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
            return Math.Pow(row.MarketProbability!.Value - outcome, 2d);
        });
        var calibrationGap = model.HasValue && observed.HasValue ? model - observed : null;
        var deltaBrier = brier.HasValue && marketBrier.HasValue ? brier - marketBrier : null;
        var traffic = Classify(predictiveFixtures, yield, calibrationGap, deltaBrier);

        output.Add(new AutomatedBotPerformanceScorecard
        {
            WindowDays = window,
            DateFromUtc = from,
            DateToUtc = to,
            Dimension = dimension,
            Segment = segment,
            BotKey = botKey,
            MarketFamily = family,
            MarketType = marketType,
            SelectedSide = selectedSide,
            Bookmaker = bookmaker,
            AutomationVersion = automationVersion,
            Total = countRows.Length,
            Resolved = countRows.Count(row => IsResolved(row.Selection.Status)),
            PredictiveResolved = rawPredictiveResolved,
            PredictiveFixtures = predictiveFixtures,
            ScientificPredictiveResolved = scientificPredictiveResolved,
            PublishedPredictiveResolved = publishedPredictiveResolved,
            EvidenceBasis = ResolveEvidenceBasis(scientificPredictiveResolved, publishedPredictiveResolved),
            SettledStake = stake,
            ProfitLoss = profitLoss,
            Yield = yield,
            ObservedWinRate = observed,
            AverageModelProbability = model,
            AverageMarketProbability = market,
            CalibrationGap = calibrationGap,
            Brier = brier,
            MarketBrier = marketBrier,
            DeltaBrier = deltaBrier,
            AverageEdge = rows.Where(row => row.Selection.ProbabilityEdge.HasValue)
                .Select(row => (double?)row.Selection.ProbabilityEdge!.Value).Average(),
            TrafficLight = traffic,
            ProductionBlocked = traffic == "Red",
            Recommendation = Recommendation(traffic)
        });
    }

    private static string Classify(int sample, decimal? yield, double? calibrationGap, double? deltaBrier)
    {
        if (sample < 30) return "Gray";
        var severeEconomic = yield <= -0.05m;
        var severeCalibration = yield < 0m && calibrationGap >= 0.10d && deltaBrier >= 0.015d;
        if (severeEconomic || severeCalibration) return "Red";
        if (sample >= 100 && yield > 0m && Math.Abs(calibrationGap ?? 1d) <= 0.05d && deltaBrier <= 0d)
            return "Green";
        return "Amber";
    }

    private static string Recommendation(string traffic) => traffic switch
    {
        "Red" => "Pausar en el plan productivo; seguir recolectando y recalibrar.",
        "Amber" => "Mantener en monitoreo o usar como máximo 0.5u; falta estabilidad/calibración.",
        "Green" => "Segmento elegible con vigilancia continua y stake limitado.",
        _ => "Muestra insuficiente; sólo shadow/monitoreo."
    };

    private static bool IsResolved(string status) =>
        status.Equals("Won", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Lost", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Push", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Void", StringComparison.OrdinalIgnoreCase);

    private static bool IsBinaryResolved(string status) =>
        status.Equals("Won", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Lost", StringComparison.OrdinalIgnoreCase);

    private static bool IsProductCompatibleHalfLine(decimal line)
    {
        var fractionalHundredths = Math.Abs(decimal.ToInt32(decimal.Round(line * 100m, 0))) % 100;
        return fractionalHundredths == 50;
    }

    private static string PerformanceFixtureKey(PerformanceRow row)
    {
        if (row.Selection.ApiFootballFixtureId is > 0)
            return $"api:{row.Selection.ApiFootballFixtureId.Value}";
        if (row.Selection.MatchHistoryId is > 0)
            return $"history:{row.Selection.MatchHistoryId.Value}";

        return string.Join('|',
            row.Selection.MatchDate.ToString("yyyy-MM-ddTHH:mm"),
            NormalizeIdentity(row.Selection.StandardizedLeague ?? row.Selection.League),
            NormalizeIdentity(row.Selection.StandardizedHomeTeam ?? row.Selection.HomeTeam),
            NormalizeIdentity(row.Selection.StandardizedAwayTeam ?? row.Selection.AwayTeam));
    }

    private static string NormalizeIdentity(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string ExactSegmentKey(PerformanceRow row) => string.Join('|',
        NormalizeBotKey(row.BotKey),
        NormalizeIdentity(row.Selection.MarketType),
        NormalizeIdentity(row.Selection.SelectedSide),
        NormalizeIdentity(row.Selection.Source),
        NormalizeIdentity(row.Selection.AutomationVersion));

    private static string ResolveEvidenceBasis(int scientific, int published) =>
        (scientific > 0, published > 0) switch
        {
            (true, false) => "ScientificEvaluations",
            (false, true) => "PublishedSelections",
            (true, true) => "MixedSegments",
            _ => "NoPredictiveEvidence"
        };

    private static bool IsSafeScientificEvidence(
        AutomatedBotPerformanceEvidence evidence,
        DateTime asOfUtc) =>
        evidence.ModelDecision.Equals("Approved", StringComparison.OrdinalIgnoreCase)
        && evidence.ApiFootballFixtureId is > 0
        && evidence.DecisionAtUtc < evidence.FixtureDateUtc
        && evidence.OutcomeAvailableAtUtc > evidence.DecisionAtUtc
        && evidence.OutcomeAvailableAtUtc <= asOfUtc
        && IsResolved(evidence.Result)
        && (evidence.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            || evidence.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
        && evidence.Odds > 1m
        && evidence.StakeUnits > 0m;

    private static PerformanceRow ToPerformanceRow(AutomatedBotPerformanceEvidence evidence)
    {
        var selection = new AutomatedCornerSelectionDto
        {
            AutomatedCornerBetSelectionId = evidence.EvidenceId,
            BotKey = NormalizeBotKey(evidence.BotKey),
            AutomationVersion = evidence.AutomationVersion,
            Source = evidence.Bookmaker,
            ApiFootballFixtureId = evidence.ApiFootballFixtureId,
            MatchDate = evidence.FixtureDateUtc,
            League = evidence.League,
            HomeTeam = evidence.HomeTeam,
            AwayTeam = evidence.AwayTeam,
            MarketType = evidence.MarketType,
            SelectedSide = evidence.SelectedSide,
            LineValue = evidence.LineValue,
            Odds = evidence.Odds,
            Stake = evidence.StakeUnits,
            Status = evidence.Result,
            SettlementFactor = evidence.SettlementFactor,
            ProfitLoss = evidence.ProfitLoss,
            ModelProbability = evidence.ModelProbability,
            ImpliedProbability = evidence.MarketProbability,
            ProbabilityEdge = evidence.ProbabilityEdge,
            CreatedAtUtc = evidence.DecisionAtUtc,
            UpdatedAtUtc = evidence.OutcomeAvailableAtUtc,
            SettledAtUtc = evidence.OutcomeAvailableAtUtc
        };

        return new PerformanceRow(
            selection,
            NormalizeBotKey(evidence.BotKey),
            ResolveFamily(evidence.MarketType),
            evidence.MarketProbability is > 0m and < 1m
                ? (double)evidence.MarketProbability.Value
                : null,
            true);
    }

    private static string ResolveFamily(string marketType) => marketType switch
    {
        "TotalCorners" or "HomeTeamCorners" or "AwayTeamCorners" => "CORNERS",
        "TotalGoals" or "HomeTeamGoals" or "AwayTeamGoals" => "GOALS",
        "TotalShots" or "HomeTeamShots" or "AwayTeamShots" => "SHOTS",
        "TotalShotsOnGoal" or "HomeTeamShotsOnGoal" or "AwayTeamShotsOnGoal" => "SOG",
        _ => "OTHER"
    };

    private static string ResolveBotKey(AutomatedCornerSelectionDto selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.BotKey))
            return NormalizeBotKey(selection.BotKey);

        var version = selection.AutomationVersion.Trim();
        foreach (var pair in new[]
        {
            ("-C2026", "C2026"), ("-D2026", "D2026"), ("-E2026", "E2026"),
            ("-F2026", "F2026"), ("-G2026", "G2026"), ("-H2026", "H2026"),
            ("-A", "A"), ("-B", "B")
        })
        {
            if (version.EndsWith(pair.Item1, StringComparison.OrdinalIgnoreCase)) return pair.Item2;
        }

        if (!string.IsNullOrWhiteSpace(selection.DecisionReason))
        {
            try
            {
                using var document = JsonDocument.Parse(selection.DecisionReason);
                if (document.RootElement.TryGetProperty("botProfile", out var profile)
                    && !string.IsNullOrWhiteSpace(profile.GetString()))
                    return NormalizeBotKey(profile.GetString()!);
            }
            catch (JsonException)
            {
            }
        }
        return "UNKNOWN";
    }

    private static string NormalizeBotKey(string value) => value.Trim().ToUpperInvariant() switch
    {
        "C" => "C2026", "D" => "D2026", "E" => "E2026", "F" => "F2026",
        "G" => "G2026", "H" => "H2026", var normalized => normalized
    };

    private static double? ResolveMarketProbability(AutomatedCornerSelectionDto selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.DecisionReason))
        {
            try
            {
                using var document = JsonDocument.Parse(selection.DecisionReason);
                if (TryReadNumber(document.RootElement, "marketNoVigProbability", out var value)
                    || TryReadNumber(document.RootElement, "MarketNoVigProbability", out value))
                    return value;
            }
            catch (JsonException)
            {
            }
        }
        return selection.ImpliedProbability is > 0m and < 1m
            ? (double)selection.ImpliedProbability.Value
            : null;
    }

    private static bool TryReadNumber(JsonElement element, string name, out double value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private sealed record PerformanceRow(
        AutomatedCornerSelectionDto Selection,
        string BotKey,
        string Family,
        double? MarketProbability,
        bool IsScientificEvidence);
}
