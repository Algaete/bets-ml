using System.Data;
using CornersPrediction.Application.Automation.BotH;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

/// <summary>
/// Query-only adapter for Bot H.  Settlement is calculated by the database from
/// immutable decision-time evidence; this repository deliberately exposes no write,
/// publish or settlement mutation operation.
/// </summary>
public sealed class SqlServerBotHShadowLabRepository : IBotHShadowLabReadRepository
{
    private readonly string _connectionString;

    public SqlServerBotHShadowLabRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<BotHShadowLabStatusDto> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var status = await connection.QuerySingleAsync<BotHShadowLabStatusDto>(
            new CommandDefinition(
                "dbo.sp_GetBotH2026ShadowStatus",
                commandType: CommandType.StoredProcedure,
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        if (!status.BotKey.Equals(BotHShadowLab.BotKey, StringComparison.Ordinal))
            throw new InvalidDataException("Bot H status returned an unexpected bot identity.");
        if (status.PublishEnabled || !status.ShadowOnly || status.UnsafePublicationRows != 0)
            return status.WithState("FAIL_CLOSED");
        return status;
    }

    public async Task<BotHShadowEvaluationPage> GetEvaluationsAsync(
        BotHShadowEvaluationFilter filter,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        BotHShadowLab.Validate(filter, utcNow);
        var asOfUtc = BotHShadowLab.NormalizeAsOfUtc(filter.AsOfUtc, utcNow);
        var page = filter.Page;
        var pageSize = filter.PageSize;

        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<BotHShadowEvaluationDto>(
            new CommandDefinition(
                "dbo.sp_GetBotH2026ShadowEvaluations",
                new
                {
                    PredictionFromUtc = AsUtc(filter.PredictionFromUtc),
                    PredictionToUtc = AsUtc(filter.PredictionToUtc),
                    AsOfUtc = asOfUtc,
                    Decision = Normalize(filter.Decision),
                    MarketType = Normalize(filter.MarketType),
                    Selection = Normalize(filter.Selection),
                    ConfigurationVersion = Normalize(filter.ConfigurationVersion),
                    SettlementState = Normalize(filter.SettlementState),
                    Page = page,
                    PageSize = pageSize
                },
                commandType: CommandType.StoredProcedure,
                commandTimeout: 120,
                cancellationToken: cancellationToken))).AsList();

        ValidateRows(rows, asOfUtc);
        return new BotHShadowEvaluationPage(
            rows,
            rows.Count == 0 ? 0 : rows[0].TotalRows,
            page,
            pageSize,
            asOfUtc);
    }

    public async Task<IReadOnlyList<BotHShadowScorecardDto>> GetScorecardsAsync(
        BotHShadowScorecardFilter filter,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        BotHShadowLab.Validate(filter, utcNow);
        var asOfUtc = BotHShadowLab.NormalizeAsOfUtc(filter.AsOfUtc, utcNow);

        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<BotHShadowScorecardDto>(
            new CommandDefinition(
                "dbo.sp_GetBotH2026ShadowScorecards",
                new
                {
                    AsOfUtc = asOfUtc,
                    ConfigurationVersion = Normalize(filter.ConfigurationVersion)
                },
                commandType: CommandType.StoredProcedure,
                commandTimeout: 180,
                cancellationToken: cancellationToken))).AsList();

        var windows = rows.Select(row => row.WindowDays).Distinct().Order().ToArray();
        if (rows.Any(row => row.Deployable
            || !row.PromotionState.Equals(BotHShadowLab.PromotionState, StringComparison.Ordinal)
            || !row.UnitOfAnalysis.Equals(BotHShadowLab.ScorecardUnitOfAnalysis, StringComparison.Ordinal)))
            throw new InvalidDataException("Bot H scorecard violated its shadow-only contract.");
        if (rows.Count > 0 && !windows.SequenceEqual(BotHShadowLab.ScorecardWindows))
            throw new InvalidDataException("Bot H scorecard did not return the fixed 7/30/90 windows.");
        return rows;
    }

    private static void ValidateRows(
        IReadOnlyList<BotHShadowEvaluationDto> rows,
        DateTime asOfUtc)
    {
        foreach (var row in rows)
        {
            if (!row.BotKey.Equals(BotHShadowLab.BotKey, StringComparison.Ordinal)
                || row.ShadowEvaluationId <= 0
                || row.SourceEvaluationId <= 0
                || row.OddsSnapshotId <= 0)
                throw new InvalidDataException("Bot H returned invalid audit lineage.");
            if (row.OddsCapturedAtUtc > row.PredictionTimestampUtc
                || row.PredictionTimestampUtc >= row.FixtureDateUtc
                || row.PredictionTimestampUtc > asOfUtc)
                throw new InvalidDataException("Bot H returned temporally invalid audit evidence.");
            if (row.SettlementState.Equals("Settled", StringComparison.Ordinal)
                && (row.MatchHistoryId is null
                    || row.OutcomeAvailableUtc is null
                    || row.OutcomeAvailableUtc <= row.PredictionTimestampUtc
                    || row.OutcomeAvailableUtc > asOfUtc
                    || row.SettlementFactor is null
                    || row.Result is null))
                throw new InvalidDataException("Bot H returned an unsafe dynamic settlement.");
            if (!row.SettlementState.Equals("Settled", StringComparison.Ordinal)
                && (row.SettlementFactor is not null || row.Result is not null || row.ProfitLoss is not null))
                throw new InvalidDataException("Bot H exposed settlement economics for an unsafe outcome.");
        }
    }

    private static DateTime? AsUtc(DateTime? value) => value.HasValue
        ? value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        }
        : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class BotHShadowLabStatusExtensions
{
    public static BotHShadowLabStatusDto WithState(
        this BotHShadowLabStatusDto value,
        string state) => new()
    {
        BotKey = value.BotKey,
        SchemaReady = value.SchemaReady,
        DefinitionExists = value.DefinitionExists,
        IsEnabled = value.IsEnabled,
        PublishEnabled = value.PublishEnabled,
        ShadowOnly = value.ShadowOnly,
        CaptureTriggerEnabled = value.CaptureTriggerEnabled,
        PublicationGuardsEnabled = value.PublicationGuardsEnabled,
        CapturedEvaluations = value.CapturedEvaluations,
        UnsafePublicationRows = value.UnsafePublicationRows,
        UncapturedEligibleEvaluations = value.UncapturedEligibleEvaluations,
        FirstPredictionTimestampUtc = value.FirstPredictionTimestampUtc,
        LastPredictionTimestampUtc = value.LastPredictionTimestampUtc,
        State = state
    };
}
