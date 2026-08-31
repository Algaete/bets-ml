using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornersPrediction.Application.Automation.BotG;
using CornersPrediction.Domain.Automation.BotG;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerBotGRepository : IBotGCandidateRepository, IBotGCandidateReadRepository
{
    private readonly string _connectionString;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SqlServerBotGRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<BotGCandidate> UpsertAsync(
        BotGCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var parameters = new DynamicParameters();
        parameters.Add("IdempotencyKey", BuildIdempotencyKey(candidate), DbType.AnsiStringFixedLength, size: 64);
        parameters.Add("RunId", candidate.RunId, DbType.Guid);
        parameters.Add("CandidateUuid", candidate.CandidateUuid, DbType.Guid);
        parameters.Add("AutomationVersion", candidate.AutomationVersion, DbType.String, size: 50);
        parameters.Add("PartidoProximoCuotaId", null, DbType.Int64);
        parameters.Add("OddsSnapshotId", candidate.SourceOddsId, DbType.Int64);
        parameters.Add("OddsTimestampUtc", AsUtc(candidate.OddsTimestampUtc), DbType.DateTime2);
        parameters.Add("FixtureId", candidate.FixtureId, DbType.Int64);
        parameters.Add("FixtureDateUtc", AsUtc(candidate.FixtureDateUtc), DbType.DateTime2);
        parameters.Add("PredictionTimestampUtc", AsUtc(candidate.PredictionTimestampUtc), DbType.DateTime2);
        parameters.Add("ApiFootballFixtureId", candidate.OfficialFixtureId, DbType.Int64);
        parameters.Add("League", candidate.League, DbType.String, size: 200);
        parameters.Add("Season", candidate.Season, DbType.String, size: 50);
        parameters.Add("HomeTeam", candidate.HomeTeam, DbType.String, size: 150);
        parameters.Add("AwayTeam", candidate.AwayTeam, DbType.String, size: 150);
        parameters.Add("Bookmaker", candidate.Bookmaker, DbType.String, size: 50);
        parameters.Add("SourceMarketType", ToSourceMarketType(candidate.MarketType), DbType.String, size: 50);
        parameters.Add("MarketType", candidate.MarketType.ToString(), DbType.String, size: 50);
        parameters.Add("Line", candidate.Line, DbType.Decimal, precision: 6, scale: 2);
        parameters.Add("Selection", candidate.Selection.ToString(), DbType.String, size: 10);
        parameters.Add("OverOdds", candidate.OverOdds, DbType.Decimal, precision: 10, scale: 4);
        parameters.Add("UnderOdds", candidate.UnderOdds, DbType.Decimal, precision: 10, scale: 4);
        parameters.Add("SelectedOdds", candidate.SelectedOdds, DbType.Decimal, precision: 10, scale: 4);
        parameters.Add("Decision", candidate.Decision.ToString(), DbType.String, size: 20);
        parameters.Add("DecisionReason", candidate.DecisionReason.ToString(), DbType.String, size: 1000);
        parameters.Add("DecisionReasonsJson", JsonSerializer.Serialize(candidate.DecisionReasons, JsonOptions), DbType.String);
        parameters.Add("RiskFlagsJson", "[]", DbType.String);
        parameters.Add("FeatureSnapshotJson", NormalizeJson(candidate.FeatureSnapshotJson), DbType.String);
        parameters.Add("ConfigurationVersion", candidate.ConfigurationVersion, DbType.String, size: 80);
        parameters.Add("FeatureSchemaVersion", candidate.FeatureSchemaVersion, DbType.String, size: 80);
        parameters.Add("BaseModelName", "GoalsEnsemble", DbType.String, size: 120);
        parameters.Add("BaseModelVersion", candidate.BaseModelVersion, DbType.String, size: 120);
        parameters.Add(
            "BaseModelTrainedThroughUtc",
            candidate.BaseModelTrainedThroughUtc.HasValue
                ? AsUtc(candidate.BaseModelTrainedThroughUtc.Value)
                : null,
            DbType.DateTime2);
        parameters.Add("MetaModelVersion", candidate.MetaModelVersion, DbType.String, size: 120);
        parameters.Add("CalibrationVersion", candidate.CalibrationVersion, DbType.String, size: 120);
        parameters.Add("UncertaintyVersion", candidate.UncertaintyVersion, DbType.String, size: 120);
        parameters.Add("OodVersion", candidate.OodVersion, DbType.String, size: 120);
        parameters.Add("RawImpliedProbability", Probability(candidate.RawImpliedProbability), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("MarketNoVigProbability", Probability(candidate.NoVigMarketProbability), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("LegacyPrediction", Metric(candidate.LegacyPrediction), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("Prediction2026", Metric(candidate.Prediction2026), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("ContextPrediction", Metric(candidate.ContextPrediction), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("HistoricalMean", Metric(candidate.HistoricalMean), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("HistoricalMedian", Metric(candidate.HistoricalMedian), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("HistoricalStd", Metric(candidate.HistoricalStandardDeviation), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("PredictionMinusLine", Metric(candidate.PredictionMinusLine), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("LegacyMinusMarketEquivalent", Metric(candidate.LegacyMinusMarketEquivalent), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("Model2026MinusMarketEquivalent", Metric(candidate.Model2026MinusMarketEquivalent), DbType.Decimal, precision: 12, scale: 6);
        parameters.Add("CandidateProbability", Probability(candidate.CandidateProbability), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("CalibratedProbability", Probability(candidate.CalibratedProbability), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("FinalProbability", Probability(candidate.FinalProbability), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("ProbabilityLowerBound", Probability(candidate.ProbabilityLowerBound), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("ProbabilityUpperBound", Probability(candidate.ProbabilityUpperBound), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("ConservativeProbability", Probability(candidate.ConservativeProbability), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("RawEdge", Metric(candidate.Edge), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("ConservativeEdge", Metric(candidate.ConservativeEdge), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("RawExpectedValue", Metric(candidate.ExpectedValue), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("ConservativeExpectedValue", Metric(candidate.ConservativeExpectedValue), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("DataQualityScore", Probability(candidate.DataQualityScore), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("ContextAgreementScore", Probability(candidate.ContextAgreementScore), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("UncertaintyScore", Probability(candidate.ProbabilityUncertainty), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("CalibrationReliability", Probability(candidate.CalibrationReliability), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("OutOfDistributionScore", Probability(candidate.OutOfDistributionScore), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("ModelDisagreement", Metric(candidate.ModelDisagreement), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("GSelectionScore", Metric(candidate.GSelectionScore), DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("Published", candidate.Published, DbType.Boolean);
        parameters.Add("PublicationStatus", PublicationStatus(candidate), DbType.String, size: 20);
        parameters.Add("PublishedSelectionId", candidate.PublishedSelectionId, DbType.Int64);
        parameters.Add("StakeUnits", candidate.StakeUnits, DbType.Decimal, precision: 9, scale: 4);

        await using var connection = new SqlConnection(_connectionString);
        var candidateId = await connection.QuerySingleAsync<long>(new CommandDefinition(
            "dbo.sp_UpsertBotG2026Candidate",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken));

        var persisted = await connection.QuerySingleAsync<CandidateRow>(new CommandDefinition(
            "SELECT * FROM dbo.vw_BotG2026Candidates WHERE CandidateId = @CandidateId;",
            new { CandidateId = candidateId },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return ToDomain(persisted);
    }

    public async Task<IReadOnlyList<BotGCandidate>> GetByFixtureAsync(
        long fixtureId,
        string configurationVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<CandidateRow>(new CommandDefinition(
            """
            SELECT *
            FROM dbo.vw_BotG2026Candidates
            WHERE FixtureId = @FixtureId
              AND ConfigurationVersion = @ConfigurationVersion
            ORDER BY GSelectionScore DESC, CandidateId;
            """,
            new { FixtureId = fixtureId, ConfigurationVersion = configurationVersion },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return rows.Select(ToDomain).ToArray();
    }

    public async Task<BotGCandidateAuditPage> GetCandidatesAsync(
        BotGCandidateAuditFilter filter,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 1000);
        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<BotGCandidateAuditDto>(new CommandDefinition(
            "dbo.sp_GetBotG2026Candidates",
            new
            {
                filter.DateFromUtc,
                filter.DateToUtc,
                filter.Decision,
                filter.PublicationStatus,
                filter.MarketType,
                filter.Selection,
                filter.Bookmaker,
                filter.ConfigurationVersion,
                filter.Result,
                Page = page,
                PageSize = pageSize
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken))).AsList();

        return new BotGCandidateAuditPage(rows, rows.FirstOrDefault()?.TotalRows ?? 0, page, pageSize);
    }

    public async Task<BotGCandidateAuditDto?> GetCandidateAsync(
        long candidateId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<BotGCandidateAuditDto>(new CommandDefinition(
            "dbo.sp_GetBotG2026CandidateDetail",
            new { CandidateId = candidateId },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BotGScorecardDto>> GetScorecardAsync(
        BotGScorecardFilter filter,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<BotGScorecardDto>(new CommandDefinition(
            "dbo.sp_GetBotG2026Scorecard",
            new { filter.DateFromUtc, filter.DateToUtc, filter.ConfigurationVersion },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SettleBotG2026CandidatesResult> SettlePendingAsync(
        SettleBotG2026CandidatesCommand command,
        CancellationToken cancellationToken)
    {
        if (command.MaximumCandidates is < 1 or > 50000)
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "MaximumCandidates must be between 1 and 50000.");

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleAsync<SettleBotG2026CandidatesResult>(new CommandDefinition(
            "dbo.sp_SettleBotG2026PendingCandidates",
            new
            {
                OutcomeAvailableThroughUtc = command.OutcomeAvailableThroughUtc.HasValue
                    ? (DateTime?)AsUtc(command.OutcomeAvailableThroughUtc.Value)
                    : null,
                command.MaximumCandidates,
                command.DryRun
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
    }

    private static BotGCandidate ToDomain(CandidateRow row) => new()
    {
        CandidateId = row.CandidateId,
        CandidateUuid = row.CandidateUuid,
        RunId = row.RunId,
        FixtureId = row.FixtureId,
        OfficialFixtureId = row.OfficialFixtureId,
        FixtureDateUtc = AsUtc(row.FixtureDateUtc),
        PredictionTimestampUtc = AsUtc(row.PredictionTimestampUtc),
        OddsTimestampUtc = AsUtc(row.OddsTimestampUtc),
        SourceOddsId = row.OddsSnapshotId,
        BotKey = row.BotKey,
        AutomationVersion = row.AutomationVersion,
        ConfigurationVersion = row.ConfigurationVersion,
        FeatureSchemaVersion = row.FeatureSchemaVersion,
        League = row.League,
        Season = row.Season ?? string.Empty,
        HomeTeam = row.HomeTeam,
        AwayTeam = row.AwayTeam,
        Bookmaker = row.Bookmaker,
        MarketFamily = row.MarketFamily,
        MarketType = ParseEnum<BotGMarketType>(row.MarketType),
        Selection = ParseEnum<BotGSelection>(row.Selection),
        Line = row.Line,
        OverOdds = row.OverOdds,
        UnderOdds = row.UnderOdds,
        SelectedOdds = row.SelectedOdds,
        RawImpliedProbability = row.RawImpliedProbability ?? 0d,
        NoVigMarketProbability = row.MarketNoVigProbability ?? 0d,
        LegacyPrediction = row.LegacyPrediction ?? 0d,
        Prediction2026 = row.Prediction2026 ?? 0d,
        ContextPrediction = row.ContextPrediction ?? 0d,
        HistoricalMean = row.HistoricalMean ?? 0d,
        HistoricalMedian = row.HistoricalMedian ?? 0d,
        HistoricalStandardDeviation = row.HistoricalStd ?? 0d,
        PredictionMinusLine = row.PredictionMinusLine ?? 0d,
        LegacyMinusMarketEquivalent = row.LegacyMinusMarketEquivalent ?? 0d,
        Model2026MinusMarketEquivalent = row.Model2026MinusMarketEquivalent ?? 0d,
        CandidateProbability = row.CandidateProbability ?? 0d,
        CalibratedProbability = row.CalibratedProbability ?? 0d,
        FinalProbability = row.FinalProbability ?? 0d,
        ProbabilityLowerBound = row.ProbabilityLowerBound ?? 0d,
        ProbabilityUpperBound = row.ProbabilityUpperBound ?? 0d,
        ProbabilityUncertainty = row.UncertaintyScore ?? 0d,
        ConservativeProbability = row.ConservativeProbability ?? 0d,
        Edge = row.RawEdge ?? 0d,
        ConservativeEdge = row.ConservativeEdge ?? 0d,
        ExpectedValue = row.RawExpectedValue ?? 0d,
        ConservativeExpectedValue = row.ConservativeExpectedValue ?? 0d,
        DataQualityScore = row.DataQualityScore ?? 0d,
        ContextAgreementScore = row.ContextAgreementScore ?? 0d,
        CalibrationReliability = row.CalibrationReliability ?? 0d,
        OutOfDistributionScore = row.OutOfDistributionScore ?? 0d,
        ModelDisagreement = row.ModelDisagreement ?? 0d,
        GSelectionScore = row.GSelectionScore ?? 0d,
        Decision = ParseEnum<BotGDecisionStatus>(row.Decision),
        DecisionReason = ParseEnum<BotGDecisionReason>(row.DecisionReason),
        DecisionReasons = DeserializeReasons(row.DecisionReasonsJson),
        Published = row.Published,
        Shadow = string.Equals(row.PublicationStatus, "Shadow", StringComparison.OrdinalIgnoreCase),
        PublishedSelectionId = row.PublishedSelectionId,
        BaseModelVersion = row.BaseModelVersion ?? string.Empty,
        BaseModelTrainedThroughUtc = row.BaseModelTrainedThroughUtc.HasValue
            ? AsUtc(row.BaseModelTrainedThroughUtc.Value)
            : null,
        MetaModelVersion = row.MetaModelVersion ?? string.Empty,
        CalibrationVersion = row.CalibrationVersion ?? string.Empty,
        UncertaintyVersion = row.UncertaintyVersion ?? string.Empty,
        OodVersion = row.OodVersion ?? string.Empty,
        StakeUnits = row.StakeUnits ?? 1m,
        SettlementState = ParseEnum(row.SettlementState, BotGSettlementState.Pending),
        Result = row.Result,
        ProfitLoss = row.ProfitLoss,
        OutcomeAvailableUtc = row.OutcomeAvailableUtc.HasValue ? AsUtc(row.OutcomeAvailableUtc.Value) : null,
        FeatureSnapshotJson = NormalizeJson(row.FeatureSnapshotJson)
    };

    private static string BuildIdempotencyKey(BotGCandidate candidate)
    {
        // The publication signature intentionally excludes quote timestamps (§94),
        // while candidate-audit idempotency must not recycle metrics from a newer
        // immutable quote into an older row. One snapshot/configuration is one audit.
        var signature = string.Join('|',
            candidate.BotKey.Trim().ToUpperInvariant(),
            candidate.FixtureId.ToString(CultureInfo.InvariantCulture),
            candidate.Bookmaker.Trim().ToUpperInvariant(),
            candidate.MarketType,
            candidate.Selection,
            candidate.Line.ToString("0.00", CultureInfo.InvariantCulture),
            candidate.ConfigurationVersion.Trim(),
            candidate.SourceOddsId?.ToString(CultureInfo.InvariantCulture) ?? "NO_IMMUTABLE_SNAPSHOT");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))).ToLowerInvariant();
    }

    private static string ToSourceMarketType(BotGMarketType marketType) => marketType switch
    {
        BotGMarketType.TotalGoals => "GoalsTotal",
        BotGMarketType.HomeTeamGoals => "GoalsHomeTeam",
        BotGMarketType.AwayTeamGoals => "GoalsAwayTeam",
        _ => throw new ArgumentOutOfRangeException(nameof(marketType), marketType, "Unsupported Bot G market.")
    };

    private static string PublicationStatus(BotGCandidate candidate) =>
        candidate.Published ? "Published" : candidate.Shadow ? "Shadow" : "NotSelected";

    private static decimal Probability(double value) =>
        Math.Round(Convert.ToDecimal(Math.Clamp(value, 0d, 1d)), 6, MidpointRounding.AwayFromZero);

    private static decimal Metric(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Bot G audit metrics must be finite.");
        return Math.Round(Convert.ToDecimal(value), 6, MidpointRounding.AwayFromZero);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string NormalizeJson(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "{}" : value;

    private static T ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : default;

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;

    private static IReadOnlyList<BotGDecisionReason> DeserializeReasons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<BotGDecisionReason>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class CandidateRow : BotGCandidateAuditDto
    {
    }
}
