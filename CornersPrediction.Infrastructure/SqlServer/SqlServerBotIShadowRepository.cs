using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornersPrediction.Application.Automation.BotI;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

/// <summary>
/// SQL adapter for the isolated I2026 market-movement collector. It only reads
/// immutable odds snapshots and appends shadow audits. There is deliberately no
/// method capable of writing AutomatedCornerBetSelections.
/// </summary>
public sealed class SqlServerBotIShadowRepository : IBotIShadowRepository
{
    private readonly string _connectionString;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SqlServerBotIShadowRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<BotIMarketTimelineCandidate>> GetTimelinesAsync(
        BotICollectCommand command,
        CancellationToken cancellationToken)
    {
        var asOfUtc = command.AsOfUtc.HasValue
            ? BotIShadowLab.EnsureUtc(command.AsOfUtc.Value)
            : DateTime.UtcNow;
        var parameters = new
        {
            DateFrom = command.DateFrom.ToDateTime(TimeOnly.MinValue),
            DateTo = command.DateTo.ToDateTime(TimeOnly.MinValue),
            AsOfUtc = asOfUtc,
            command.MaximumFixtures
        };

        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<BotIMarketTimelineCandidate>(new CommandDefinition(
            TimelineSql,
            parameters,
            commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();

        foreach (var row in rows)
        {
            if (row.OpeningSnapshotId <= 0 || row.CurrentSnapshotId <= 0
                || row.OpeningCapturedAtUtc > row.CurrentCapturedAtUtc
                || row.CurrentCapturedAtUtc > asOfUtc)
                throw new InvalidDataException("Bot I timeline query returned invalid point-in-time lineage.");
        }
        return rows;
    }

    public async Task<IReadOnlySet<long>> GetCapturedCurrentSnapshotIdsAsync(
        IReadOnlyCollection<long> currentSnapshotIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentSnapshotIds);
        var distinctIds = currentSnapshotIds.Where(id => id > 0).Distinct().ToArray();
        var captured = new HashSet<long>();
        if (distinctIds.Length == 0)
            return captured;

        await using var connection = new SqlConnection(_connectionString);
        foreach (var chunk in distinctIds.Chunk(1000))
        {
            var rows = await connection.QueryAsync<long>(new CommandDefinition(
                """
                SELECT CurrentSnapshotId
                FROM dbo.BotI2026ShadowEvaluations
                WHERE ConfigurationVersion = @ConfigurationVersion
                  AND CurrentSnapshotId IN @CurrentSnapshotIds;
                """,
                new
                {
                    ConfigurationVersion = BotIShadowLab.ConfigurationVersion,
                    CurrentSnapshotIds = chunk
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            captured.UnionWith(rows);
        }
        return captured;
    }

    public async Task<bool> AppendAsync(
        BotIShadowEvaluationDraft evaluation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (!evaluation.BotKey.Equals(BotIShadowLab.BotKey, StringComparison.Ordinal)
            || evaluation.OpeningSnapshotId <= 0
            || evaluation.CurrentSnapshotId <= 0
            || evaluation.OpeningCapturedAtUtc > evaluation.CurrentCapturedAtUtc
            || evaluation.CurrentCapturedAtUtc > evaluation.PredictionTimestampUtc
            || evaluation.PredictionTimestampUtc >= evaluation.FixtureDateUtc)
            throw new InvalidDataException("Bot I refused an unsafe shadow evaluation.");

        var featureJson = NormalizeJson(evaluation.FeatureSnapshotJson);
        var parameters = new DynamicParameters();
        parameters.Add("IdempotencyKey", BotIShadowLab.BuildIdempotencyKey(evaluation), DbType.AnsiStringFixedLength, size: 64);
        parameters.Add("ConfigurationVersion", evaluation.ConfigurationVersion, DbType.String, size: 80);
        parameters.Add("FeatureSchemaVersion", evaluation.FeatureSchemaVersion, DbType.String, size: 80);
        parameters.Add("FixtureIdentity", evaluation.FixtureIdentity, DbType.Int64);
        parameters.Add("ApiFootballFixtureId", evaluation.ApiFootballFixtureId, DbType.Int64);
        parameters.Add("FixtureDateUtc", AsUtc(evaluation.FixtureDateUtc), DbType.DateTime2);
        parameters.Add("PredictionTimestampUtc", AsUtc(evaluation.PredictionTimestampUtc), DbType.DateTime2);
        parameters.Add("League", evaluation.League, DbType.String, size: 300);
        parameters.Add("HomeTeam", evaluation.HomeTeam, DbType.String, size: 300);
        parameters.Add("AwayTeam", evaluation.AwayTeam, DbType.String, size: 300);
        parameters.Add("Source", evaluation.Source, DbType.String, size: 50);
        parameters.Add("SourceMatchId", evaluation.SourceMatchId, DbType.String, size: 100);
        parameters.Add("MarketType", evaluation.MarketType, DbType.String, size: 50);
        parameters.Add("Selection", evaluation.Selection, DbType.String, size: 10);
        parameters.Add("Decision", evaluation.Decision.ToString(), DbType.String, size: 20);
        parameters.Add("SignalScore", evaluation.SignalScore, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("SelectedOdds", evaluation.SelectedOdds, DbType.Decimal, precision: 18, scale: 6);
        parameters.Add("OpeningSnapshotId", evaluation.OpeningSnapshotId, DbType.Int64);
        parameters.Add("CurrentSnapshotId", evaluation.CurrentSnapshotId, DbType.Int64);
        parameters.Add("PeerSnapshotId", evaluation.PeerSnapshotId, DbType.Int64);
        parameters.Add("OpeningCapturedAtUtc", AsUtc(evaluation.OpeningCapturedAtUtc), DbType.DateTime2);
        parameters.Add("CurrentCapturedAtUtc", AsUtc(evaluation.CurrentCapturedAtUtc), DbType.DateTime2);
        parameters.Add("PeerCapturedAtUtc", evaluation.PeerCapturedAtUtc.HasValue ? AsUtc(evaluation.PeerCapturedAtUtc.Value) : null, DbType.DateTime2);
        parameters.Add("OpeningLine", evaluation.OpeningLine, DbType.Decimal, precision: 10, scale: 2);
        parameters.Add("CurrentLine", evaluation.CurrentLine, DbType.Decimal, precision: 10, scale: 2);
        parameters.Add("PeerLine", evaluation.PeerLine, DbType.Decimal, precision: 10, scale: 2);
        parameters.Add("OpeningOverNoVigProbability", evaluation.OpeningOverNoVigProbability, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("CurrentOverNoVigProbability", evaluation.CurrentOverNoVigProbability, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("PeerOverNoVigProbability", evaluation.PeerOverNoVigProbability, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("SelectedProbabilityMovement", evaluation.SelectedProbabilityMovement, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("SelectedLineMovement", evaluation.SelectedLineMovement, DbType.Decimal, precision: 10, scale: 2);
        parameters.Add("MovementVelocityPerHour", evaluation.MovementVelocityPerHour, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("ObservationHours", evaluation.ObservationHours, DbType.Decimal, precision: 12, scale: 4);
        parameters.Add("OddsAgeMinutes", evaluation.OddsAgeMinutes, DbType.Decimal, precision: 12, scale: 4);
        parameters.Add("SnapshotCount", evaluation.SnapshotCount, DbType.Int32);
        parameters.Add("PeerSource", evaluation.PeerSource, DbType.String, size: 50);
        parameters.Add("PinnacleOverNoVigProbability", evaluation.PinnacleOverNoVigProbability, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("BetanoOverNoVigProbability", evaluation.BetanoOverNoVigProbability, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("CrossBookProbabilityDispersion", evaluation.CrossBookProbabilityDispersion, DbType.Decimal, precision: 12, scale: 8);
        parameters.Add("CrossBookLineDispersion", evaluation.CrossBookLineDispersion, DbType.Decimal, precision: 10, scale: 2);
        parameters.Add("ReasonCodesJson", JsonSerializer.Serialize(evaluation.ReasonCodes, JsonOptions), DbType.String);
        parameters.Add("RiskFlagsJson", JsonSerializer.Serialize(evaluation.RiskFlags, JsonOptions), DbType.String);
        parameters.Add("Explanation", evaluation.Explanation, DbType.String, size: 1000);
        parameters.Add("FeatureSnapshotJson", featureJson, DbType.String);
        parameters.Add("FeatureSnapshotHash", SHA256.HashData(Encoding.Unicode.GetBytes(featureJson)), DbType.Binary, size: 32);

        await using var connection = new SqlConnection(_connectionString);
        var result = await connection.QuerySingleAsync<AppendResult>(new CommandDefinition(
            "dbo.sp_AppendBotI2026ShadowEvaluation",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return result.WasInserted;
    }

    public async Task<BotIShadowStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var status = await connection.QuerySingleAsync<BotIShadowStatusDto>(new CommandDefinition(
            "dbo.sp_GetBotI2026ShadowStatus",
            commandType: CommandType.StoredProcedure,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        if (!status.BotKey.Equals(BotIShadowLab.BotKey, StringComparison.Ordinal)
            || !status.ShadowOnly
            || !status.PublicationBlocked
            || status.UnsafeRows != 0)
            throw new InvalidDataException("Bot I shadow publication guard is not healthy.");
        return status;
    }

    public async Task<BotIEvaluationPage> GetEvaluationsAsync(
        BotIEvaluationFilter filter,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        BotIShadowLab.Validate(filter, now);
        var asOfUtc = BotIShadowLab.ValidateAsOf(filter.AsOfUtc, now);
        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<BotIShadowEvaluationDto>(new CommandDefinition(
            "dbo.sp_GetBotI2026ShadowEvaluations",
            new
            {
                PredictionFromUtc = ToUtc(filter.PredictionFromUtc),
                PredictionToUtc = ToUtc(filter.PredictionToUtc),
                AsOfUtc = asOfUtc,
                Decision = Normalize(filter.Decision),
                MarketType = Normalize(filter.MarketType),
                Selection = Normalize(filter.Selection),
                Source = Normalize(filter.Source),
                ConfigurationVersion = Normalize(filter.ConfigurationVersion),
                filter.Page,
                filter.PageSize
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken))).AsList();

        foreach (var row in rows)
        {
            if (!row.BotKey.Equals(BotIShadowLab.BotKey, StringComparison.Ordinal)
                || !row.ShadowOnly
                || !row.PublicationBlocked
                || row.OpeningCapturedAtUtc > row.CurrentCapturedAtUtc
                || row.CurrentCapturedAtUtc > row.PredictionTimestampUtc
                || row.PredictionTimestampUtc >= row.FixtureDateUtc
                || row.PredictionTimestampUtc > asOfUtc)
                throw new InvalidDataException("Bot I returned unsafe audit lineage.");
            if (row.SettlementState.Equals("Settled", StringComparison.Ordinal)
                && (row.Decision != nameof(BotIShadowDecision.Approved)
                    || row.MatchHistoryId is null
                    || row.OutcomeAvailableUtc is null
                    || row.OutcomeAvailableUtc <= row.PredictionTimestampUtc
                    || row.OutcomeAvailableUtc > asOfUtc
                    || row.ActualValue is null
                    || row.SettlementFactor is null
                    || row.Result is null
                    || row.ProfitLoss is null))
                throw new InvalidDataException("Bot I returned temporally unsafe outcome evidence.");
            if (!row.SettlementState.Equals("Settled", StringComparison.Ordinal)
                && (row.SettlementFactor is not null || row.Result is not null || row.ProfitLoss is not null))
                throw new InvalidDataException("Bot I exposed economics without safe settlement evidence.");
        }
        return new BotIEvaluationPage(
            rows,
            rows.Count == 0 ? 0 : rows[0].TotalRows,
            filter.Page,
            filter.PageSize,
            asOfUtc);
    }

    public async Task<IReadOnlyList<BotIShadowScorecardDto>> GetScorecardsAsync(
        DateTime? asOfUtc,
        string? configurationVersion,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var cutoff = BotIShadowLab.ValidateAsOf(asOfUtc, now);
        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<BotIShadowScorecardDto>(new CommandDefinition(
            "dbo.sp_GetBotI2026ShadowScorecards",
            new
            {
                AsOfUtc = cutoff,
                ConfigurationVersion = Normalize(configurationVersion)
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 90,
            cancellationToken: cancellationToken))).AsList();
        if (rows.Any(row => row.Deployable
                || !row.PromotionState.Equals(BotIShadowLab.PromotionState, StringComparison.Ordinal)
                || !row.ScorecardType.Equals("OUTCOME_AWARE_SHADOW_OFFICIAL_FIXTURE_ONLY", StringComparison.Ordinal)))
            throw new InvalidDataException("Bot I scorecard violated its shadow-only contract.");
        var windows = rows.Select(row => row.WindowDays).Distinct().Order().ToArray();
        if (rows.Count > 0 && !windows.SequenceEqual(BotIShadowLab.ScorecardWindows))
            throw new InvalidDataException("Bot I scorecard did not return its fixed windows.");
        return rows;
    }

    private static DateTime AsUtc(DateTime value) => BotIShadowLab.EnsureUtc(value);
    private static DateTime? ToUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("FeatureSnapshotJson must be an object.", nameof(value));
        return document.RootElement.GetRawText();
    }

    private sealed class AppendResult
    {
        public bool WasInserted { get; init; }
    }

    private const string TimelineSql = """
        SELECT TOP (@MaximumFixtures)
            snapshot.MatchDate AS SourceMatchDate,
            League = COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedLeague)), N''), LTRIM(RTRIM(snapshot.League))),
            HomeTeam = COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedHomeTeam)), N''), LTRIM(RTRIM(snapshot.HomeTeam))),
            AwayTeam = COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedAwayTeam)), N''), LTRIM(RTRIM(snapshot.AwayTeam)))
        INTO #BotISelectedFixtures
        FROM dbo.CornerOddsSnapshots AS snapshot
        WHERE snapshot.MatchDate >= @DateFrom
          AND snapshot.MatchDate < @DateTo
          AND CONVERT(
                DATETIME2(3),
                SWITCHOFFSET(snapshot.MatchDate AT TIME ZONE 'Pacific SA Standard Time', '+00:00')) > @AsOfUtc
          AND snapshot.CapturedAtUtc <= @AsOfUtc
          AND snapshot.MarketType IN (N'GoalsTotal', N'CornersTotal')
          AND snapshot.OverOdds > 1
          AND snapshot.UnderOdds > 1
          AND snapshot.LineValue >= 0
          AND snapshot.LineValue * 2 = FLOOR(snapshot.LineValue * 2)
          AND snapshot.LineValue <> FLOOR(snapshot.LineValue)
          AND ISNULL(snapshot.HomeTeamGender, N'M') <> N'F'
          AND ISNULL(snapshot.AwayTeamGender, N'M') <> N'F'
        GROUP BY
            snapshot.MatchDate,
            COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedLeague)), N''), LTRIM(RTRIM(snapshot.League))),
            COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedHomeTeam)), N''), LTRIM(RTRIM(snapshot.HomeTeam))),
            COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedAwayTeam)), N''), LTRIM(RTRIM(snapshot.AwayTeam)))
        ORDER BY SourceMatchDate, League, HomeTeam, AwayTeam
        OPTION (RECOMPILE);

        WITH Eligible AS
        (
            SELECT
                snapshot.CornerOddsSnapshotId,
                snapshot.CapturedAtUtc,
                snapshot.Source,
                snapshot.SourceMatchId,
                snapshot.MatchDate AS SourceMatchDate,
                League = COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedLeague)), N''), LTRIM(RTRIM(snapshot.League))),
                HomeTeam = COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedHomeTeam)), N''), LTRIM(RTRIM(snapshot.HomeTeam))),
                AwayTeam = COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedAwayTeam)), N''), LTRIM(RTRIM(snapshot.AwayTeam))),
                snapshot.MarketType AS SourceMarketType,
                snapshot.LineValue,
                OverOdds = CONVERT(DECIMAL(18,6), snapshot.OverOdds),
                UnderOdds = CONVERT(DECIMAL(18,6), snapshot.UnderOdds),
                BatchRank = ROW_NUMBER() OVER
                (
                    PARTITION BY
                        snapshot.MatchDate,
                        COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedLeague)), N''), LTRIM(RTRIM(snapshot.League))),
                        COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedHomeTeam)), N''), LTRIM(RTRIM(snapshot.HomeTeam))),
                        COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedAwayTeam)), N''), LTRIM(RTRIM(snapshot.AwayTeam))),
                        snapshot.Source,
                        snapshot.MarketType,
                        snapshot.CapturedAtUtc
                    ORDER BY
                        ABS(
                            ((1.0 / snapshot.OverOdds) /
                                ((1.0 / snapshot.OverOdds) + (1.0 / snapshot.UnderOdds))) - 0.5),
                        snapshot.CornerOddsSnapshotId
                )
            FROM dbo.CornerOddsSnapshots AS snapshot
            INNER JOIN #BotISelectedFixtures AS fixture
              ON fixture.SourceMatchDate = snapshot.MatchDate
             AND fixture.League COLLATE Latin1_General_100_CI_AI =
                 COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedLeague)), N''), LTRIM(RTRIM(snapshot.League)))
                    COLLATE Latin1_General_100_CI_AI
             AND fixture.HomeTeam COLLATE Latin1_General_100_CI_AI =
                 COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedHomeTeam)), N''), LTRIM(RTRIM(snapshot.HomeTeam)))
                    COLLATE Latin1_General_100_CI_AI
             AND fixture.AwayTeam COLLATE Latin1_General_100_CI_AI =
                 COALESCE(NULLIF(LTRIM(RTRIM(snapshot.StandardizedAwayTeam)), N''), LTRIM(RTRIM(snapshot.AwayTeam)))
                    COLLATE Latin1_General_100_CI_AI
            WHERE snapshot.MatchDate >= @DateFrom
              AND snapshot.MatchDate < @DateTo
              AND CONVERT(
                    DATETIME2(3),
                    SWITCHOFFSET(snapshot.MatchDate AT TIME ZONE 'Pacific SA Standard Time', '+00:00')) > @AsOfUtc
              AND snapshot.CapturedAtUtc <= @AsOfUtc
              AND snapshot.CapturedAtUtc < CONVERT(
                    DATETIME2(3),
                    SWITCHOFFSET(snapshot.MatchDate AT TIME ZONE 'Pacific SA Standard Time', '+00:00'))
              AND snapshot.MarketType IN (N'GoalsTotal', N'CornersTotal')
              AND snapshot.OverOdds > 1
              AND snapshot.UnderOdds > 1
              AND snapshot.LineValue >= 0
              AND snapshot.LineValue * 2 = FLOOR(snapshot.LineValue * 2)
              AND snapshot.LineValue <> FLOOR(snapshot.LineValue)
              AND ISNULL(snapshot.HomeTeamGender, N'M') <> N'F'
              AND ISNULL(snapshot.AwayTeamGender, N'M') <> N'F'
        )
        SELECT *
        INTO #BotIMainLine
        FROM Eligible
        WHERE BatchRank = 1
        OPTION (RECOMPILE);

        CREATE INDEX IX_BotIMainLine_Capture
            ON #BotIMainLine(SourceMatchDate, Source, SourceMarketType, CapturedAtUtc, CornerOddsSnapshotId);

        ;WITH Ranked AS
        (
            SELECT
                line.*,
                OpeningRank = ROW_NUMBER() OVER
                (
                    PARTITION BY SourceMatchDate, League, HomeTeam, AwayTeam, Source, SourceMarketType
                    ORDER BY CapturedAtUtc, CornerOddsSnapshotId
                ),
                CurrentRank = ROW_NUMBER() OVER
                (
                    PARTITION BY SourceMatchDate, League, HomeTeam, AwayTeam, Source, SourceMarketType
                    ORDER BY CapturedAtUtc DESC, CornerOddsSnapshotId DESC
                ),
                SnapshotCount = COUNT_BIG(*) OVER
                (
                    PARTITION BY SourceMatchDate, League, HomeTeam, AwayTeam, Source, SourceMarketType
                )
            FROM #BotIMainLine AS line
        )
        SELECT
            latest.SourceMatchDate,
            latest.League,
            latest.HomeTeam,
            latest.AwayTeam,
            latest.Source,
            latest.SourceMatchId,
            latest.SourceMarketType,
            fixture.ApiFootballFixtureId,
            OpeningSnapshotId = opening.CornerOddsSnapshotId,
            OpeningCapturedAtUtc = opening.CapturedAtUtc,
            OpeningLine = opening.LineValue,
            OpeningOverOdds = opening.OverOdds,
            OpeningUnderOdds = opening.UnderOdds,
            CurrentSnapshotId = latest.CornerOddsSnapshotId,
            CurrentCapturedAtUtc = latest.CapturedAtUtc,
            CurrentLine = latest.LineValue,
            CurrentOverOdds = latest.OverOdds,
            CurrentUnderOdds = latest.UnderOdds,
            SnapshotCount = CONVERT(INT, latest.SnapshotCount),
            PeerSnapshotId = peer.CornerOddsSnapshotId,
            PeerSource = peer.Source,
            PeerCapturedAtUtc = peer.CapturedAtUtc,
            PeerLine = peer.LineValue,
            PeerOverOdds = peer.OverOdds,
            PeerUnderOdds = peer.UnderOdds
        FROM Ranked AS latest
        INNER JOIN Ranked AS opening
          ON opening.SourceMatchDate = latest.SourceMatchDate
         AND opening.League COLLATE Latin1_General_100_CI_AI = latest.League COLLATE Latin1_General_100_CI_AI
         AND opening.HomeTeam COLLATE Latin1_General_100_CI_AI = latest.HomeTeam COLLATE Latin1_General_100_CI_AI
         AND opening.AwayTeam COLLATE Latin1_General_100_CI_AI = latest.AwayTeam COLLATE Latin1_General_100_CI_AI
         AND opening.Source = latest.Source
         AND opening.SourceMarketType = latest.SourceMarketType
         AND opening.OpeningRank = 1
        OUTER APPLY
        (
            SELECT ApiFootballFixtureId = CASE WHEN COUNT_BIG(*) = 1 THEN MAX(upcoming.ExternalFixtureId) END
            FROM dbo.PartidosProximos AS upcoming
            WHERE upcoming.ExternalFixtureId IS NOT NULL
              AND upcoming.FechaPartido >= CAST(latest.SourceMatchDate AS DATE)
              AND upcoming.FechaPartido < DATEADD(DAY, 1, CAST(latest.SourceMatchDate AS DATE))
              AND upcoming.EquipoLocal COLLATE Latin1_General_100_CI_AI = latest.HomeTeam COLLATE Latin1_General_100_CI_AI
              AND upcoming.EquipoVisita COLLATE Latin1_General_100_CI_AI = latest.AwayTeam COLLATE Latin1_General_100_CI_AI
        ) AS fixture
        OUTER APPLY
        (
            SELECT TOP (1)
                candidate.CornerOddsSnapshotId,
                candidate.Source,
                candidate.CapturedAtUtc,
                candidate.LineValue,
                candidate.OverOdds,
                candidate.UnderOdds
            FROM #BotIMainLine AS candidate
            WHERE candidate.SourceMatchDate = latest.SourceMatchDate
              AND candidate.League COLLATE Latin1_General_100_CI_AI = latest.League COLLATE Latin1_General_100_CI_AI
              AND candidate.HomeTeam COLLATE Latin1_General_100_CI_AI = latest.HomeTeam COLLATE Latin1_General_100_CI_AI
              AND candidate.AwayTeam COLLATE Latin1_General_100_CI_AI = latest.AwayTeam COLLATE Latin1_General_100_CI_AI
              AND candidate.SourceMarketType = latest.SourceMarketType
              AND candidate.Source <> latest.Source
              AND candidate.CapturedAtUtc <= latest.CapturedAtUtc
            ORDER BY candidate.CapturedAtUtc DESC, candidate.CornerOddsSnapshotId DESC
        ) AS peer
        WHERE latest.CurrentRank = 1
        ORDER BY latest.SourceMatchDate, latest.HomeTeam, latest.AwayTeam,
                 latest.SourceMarketType, latest.Source;
        """;
}
