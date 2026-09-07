using System.Data;
using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerAutomatedCornerSelectionsRepository : IAutomatedCornerSelectionsRepository,
    IAutomatedBotMonthlyHistoryRepository, IAutomatedBotPerformanceSelectionsRepository
{
    private readonly string _connectionString;
    private const string SelectSelectionSql = """
        SELECT
            s.AutomatedCornerBetSelectionId,
            s.RunId,
            s.BotKey,
            s.AutomationVersion,
            s.Source,
            s.SourceMatchId,
            s.ApiFootballFixtureId,
            s.MatchHistoryId,
            s.SourceUrl,
            s.MatchDate,
            MatchDay = CAST(s.MatchDate AS DATE),
            s.League,
            s.StandardizedLeague,
            s.HomeTeam,
            s.AwayTeam,
            s.StandardizedHomeTeam,
            s.StandardizedAwayTeam,
            s.SourceMarketType,
            s.MarketType,
            Recommendation = CONCAT(s.SelectedSide, ' ', CONVERT(VARCHAR(20), s.LineValue)),
            s.SelectedSide,
            s.LineValue,
            s.Odds,
            s.Stake,
            s.FlatStake,
            s.KellyFraction,
            s.ImpliedProbability,
            s.ModelProbability,
            s.ProbabilityEdge,
            s.ExpectedValue,
            s.SelectionScore,
            s.PredictedTotalCorners,
            s.PredTotalDirect,
            s.PredHomeCorners,
            s.PredAwayCorners,
            s.PredTotalCombined,
            s.DistanceToLine,
            s.ConfidenceLevel,
            s.OverUnderConfidenceLevel,
            s.ModelConsensus,
            s.ContextTotalCorners,
            s.ContextDifference,
            s.RecommendedSide,
            s.Status,
            s.ActualHomeCorners,
            s.ActualAwayCorners,
            s.ActualTotalCorners,
            s.SettlementActualValue,
            s.SettlementFactor,
            s.SettlementReason,
            s.SettlementSource,
            s.SettlementMatchStatus,
            s.LastSettlementCheckReason,
            s.LastSettlementCheckAtUtc,
            s.ProfitLoss,
            s.YieldPct,
            s.DecisionReason,
            s.CreatedAtUtc,
            s.UpdatedAtUtc,
            s.SettledAtUtc
        FROM dbo.AutomatedCornerBetSelections s
        """;
    private const string SelectSelectionByIdSql = SelectSelectionSql + " WHERE s.AutomatedCornerBetSelectionId = @Id;";

    public SqlServerAutomatedCornerSelectionsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetPerformanceSelectionsAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        // Older deployed procedures omit these identity columns. Introducing
        // them here would change bot cohorts and fixture deduplication while
        // ostensibly only optimizing the read. Preserve that existing contract.
        var columns = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT name
            FROM sys.dm_exec_describe_first_result_set_for_object(
                OBJECT_ID(N'dbo.sp_GetAutomatedCornerBetSelections'), 0)
            WHERE is_hidden = 0 AND name IN (N'BotKey', N'ApiFootballFixtureId', N'MatchHistoryId');
            """, commandTimeout: 30, cancellationToken: cancellationToken)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var botKey = columns.Contains("BotKey") ? "s.BotKey" : "N'' AS BotKey";
        var fixtureId = columns.Contains("ApiFootballFixtureId")
            ? "s.ApiFootballFixtureId" : "CAST(NULL AS BIGINT) AS ApiFootballFixtureId";
        var historyId = columns.Contains("MatchHistoryId")
            ? "s.MatchHistoryId" : "CAST(NULL AS BIGINT) AS MatchHistoryId";
        var sql = $$"""
            SELECT s.AutomatedCornerBetSelectionId, {{botKey}}, s.AutomationVersion,
                s.Source, {{fixtureId}}, {{historyId}}, s.MatchDate,
                s.League, s.StandardizedLeague, s.HomeTeam, s.AwayTeam,
                s.StandardizedHomeTeam, s.StandardizedAwayTeam,
                s.MarketType, s.SelectedSide, s.LineValue, s.Stake,
                s.ImpliedProbability, s.ModelProbability, s.ProbabilityEdge,
                s.Status, s.ProfitLoss, s.UpdatedAtUtc,
                DecisionReason = COALESCE(evidence.DecisionReason, N'{}')
            FROM dbo.AutomatedCornerBetSelections AS s
            OUTER APPLY
            (
                -- Keep raw numeric literals and JSON value types unchanged so
                -- the shared C# bot/probability fallback rules remain identical.
                SELECT DecisionReason = N'{' + STRING_AGG(CONVERT(NVARCHAR(MAX),
                    N'"' + j.[key] + N'":' + CASE j.[type]
                        WHEN 0 THEN N'null'
                        WHEN 1 THEN N'"' + STRING_ESCAPE(j.[value], 'json') + N'"'
                        ELSE j.[value] END) COLLATE DATABASE_DEFAULT, N',') + N'}'
                FROM OPENJSON(CASE WHEN ISJSON(s.DecisionReason) = 1
                    THEN s.DecisionReason ELSE N'{}' END) AS j
                WHERE j.[key] COLLATE Latin1_General_100_BIN2 IN
                    (N'botProfile', N'marketNoVigProbability', N'MarketNoVigProbability')
            ) AS evidence
            WHERE s.MatchDate >= @DateFrom AND s.MatchDate < @DateToExclusive
            ORDER BY s.MatchDate DESC, s.UpdatedAtUtc DESC, s.AutomatedCornerBetSelectionId DESC
            OPTION (RECOMPILE);
            """;
        return (await connection.QueryAsync<AutomatedCornerSelectionDto>(new CommandDefinition(
            sql,
            new { DateFrom = dateFrom.Date, DateToExclusive = dateTo.Date.AddDays(1) },
            commandTimeout: 30,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<AutomatedBotMonthlySummary>> GetMonthlyHistoryAsync(
        DateTime dateFrom, DateTime dateTo, string marketFamily, CancellationToken cancellationToken)
    {
        // Aggregate on the server: the dashboard needs counts and returns, not
        // a year of feature snapshots, explanations and individual selections.
        const string sql = """
            WITH Scoped AS
            (
                SELECT
                    Month = DATEFROMPARTS(YEAR(s.MatchDate), MONTH(s.MatchDate), 1),
                    s.Status, s.Stake, s.ProfitLoss,
                    StoredBotKey = UPPER(LTRIM(RTRIM(s.BotKey))),
                    Version = UPPER(LTRIM(RTRIM(s.AutomationVersion))),
                    DecisionProfile = UPPER(LTRIM(RTRIM(JSON_VALUE(
                        CASE WHEN ISJSON(s.DecisionReason) = 1 THEN s.DecisionReason ELSE N'{}' END,
                        '$.botProfile')))),
                    HasDecisionProfile = CASE WHEN EXISTS
                    (
                        SELECT 1 FROM OPENJSON(
                            CASE WHEN ISJSON(s.DecisionReason) = 1 THEN s.DecisionReason ELSE N'{}' END)
                        WHERE [key] COLLATE Latin1_General_100_BIN2 = N'botProfile'
                    ) THEN 1 ELSE 0 END
                FROM dbo.AutomatedCornerBetSelections AS s
                WHERE s.MatchDate >= @DateFrom AND s.MatchDate < @DateToExclusive
                  AND s.MarketType IN @MarketTypes
            ), Classified AS
            (
                SELECT *, BotKey = CASE
                    WHEN Version LIKE N'%-A' THEN N'A'
                    WHEN Version LIKE N'%-B' THEN N'B'
                    WHEN Version LIKE N'%-C2026' THEN N'C'
                    WHEN Version LIKE N'%-D2026' THEN N'D'
                    WHEN Version LIKE N'%-E2026' THEN N'E'
                    WHEN Version LIKE N'%-F2026' THEN N'F'
                    WHEN DecisionProfile IN (N'A', N'B') THEN DecisionProfile
                    WHEN DecisionProfile IN (N'C', N'C2026') THEN N'C'
                    WHEN DecisionProfile IN (N'D', N'D2026') THEN N'D'
                    WHEN DecisionProfile IN (N'E', N'E2026') THEN N'E'
                    WHEN DecisionProfile IN (N'F', N'F2026') THEN N'F'
                    WHEN HasDecisionProfile = 1 THEN N'Legacy'
                    ELSE N'A' END
                FROM Scoped
                WHERE Status = N'Pending'
                   OR (ISNULL(StoredBotKey, N'') <> N'B'
                       AND Version NOT LIKE N'%-B'
                       AND ISNULL(DecisionProfile, N'') <> N'B')
            )
            SELECT Month, BotKey, Total = COUNT(*),
                Pending = SUM(CASE WHEN Status = N'Pending' THEN 1 ELSE 0 END),
                Won = SUM(CASE WHEN Status = N'Won' THEN 1 ELSE 0 END),
                Lost = SUM(CASE WHEN Status = N'Lost' THEN 1 ELSE 0 END),
                Push = SUM(CASE WHEN Status = N'Push' THEN 1 ELSE 0 END),
                Void = SUM(CASE WHEN Status = N'Void' THEN 1 ELSE 0 END),
                ProfitLoss = SUM(COALESCE(ProfitLoss, 0)),
                SettledStake = SUM(CASE WHEN Status IN (N'Won', N'Lost', N'Push') THEN Stake ELSE 0 END)
            FROM Classified
            GROUP BY Month, BotKey
            ORDER BY Month DESC, CASE BotKey
                WHEN N'A' THEN 1 WHEN N'B' THEN 2 WHEN N'C' THEN 3
                WHEN N'D' THEN 4 WHEN N'E' THEN 5 WHEN N'F' THEN 6 ELSE 7 END
            OPTION (RECOMPILE);
            """;
        await using var connection = new SqlConnection(_connectionString);
        return (await connection.QueryAsync<AutomatedBotMonthlySummary>(new CommandDefinition(
            sql,
            new
            {
                DateFrom = dateFrom.Date,
                DateToExclusive = dateTo.Date.AddDays(1),
                MarketTypes = AutomatedBotMarketScope.MarketTypes(marketFamily)
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync(
        AutomatedCornerSelectionsFilterRequest filters,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(filters.MarketFamily))
        {
            // Push the family and the date window into SQL before reading the
            // large decision evidence used by the visible picks.
            const string sql = SelectSelectionSql + " " + """
                WHERE (@DateFrom IS NULL OR s.MatchDate >= @DateFrom)
                  AND (@DateToExclusive IS NULL OR s.MatchDate < @DateToExclusive)
                  AND (@Status IS NULL OR s.Status = @Status)
                  AND (@League IS NULL OR COALESCE(s.StandardizedLeague, s.League) = @League)
                  AND (@Source IS NULL OR s.Source = @Source)
                  AND (@MarketType IS NULL OR s.MarketType = @MarketType)
                  AND s.MarketType IN @MarketTypes
                  AND (@OnlyPending = 0 OR s.Status = N'Pending')
                ORDER BY s.MatchDate DESC, s.UpdatedAtUtc DESC, s.AutomatedCornerBetSelectionId DESC
                OPTION (RECOMPILE);
                """;
            await using var scopedConnection = new SqlConnection(_connectionString);
            return (await scopedConnection.QueryAsync<AutomatedCornerSelectionDto>(new CommandDefinition(
                sql,
                new
                {
                    DateFrom = filters.DateFrom?.Date,
                    DateToExclusive = filters.DateTo?.Date.AddDays(1),
                    filters.Status, filters.League, filters.Source, filters.MarketType, filters.OnlyPending,
                    MarketTypes = AutomatedBotMarketScope.MarketTypes(filters.MarketFamily)
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken))).AsList();
        }

        await using var connection = new SqlConnection(_connectionString);
        var supportedParameters = await GetStoredProcedureParametersAsync(
            connection,
            "dbo.sp_GetAutomatedCornerBetSelections",
            cancellationToken);
        var parameters = new DynamicParameters();
        AddParameter(parameters, supportedParameters, "DateFrom", filters.DateFrom, DbType.Date);
        AddParameter(parameters, supportedParameters, "DateTo", filters.DateTo, DbType.Date);
        AddParameter(parameters, supportedParameters, "Status", filters.Status, DbType.String, size: 20);
        AddParameter(parameters, supportedParameters, "League", filters.League, DbType.String, size: 200);
        AddParameter(parameters, supportedParameters, "Source", filters.Source, DbType.String, size: 50);
        AddParameter(parameters, supportedParameters, "MarketType", filters.MarketType, DbType.String, size: 50);
        AddParameter(parameters, supportedParameters, "OnlyPending", filters.OnlyPending, DbType.Boolean);

        var command = new CommandDefinition(
            "dbo.sp_GetAutomatedCornerBetSelections",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<AutomatedCornerSelectionDto>(command);
        var filteredRows = rows;

        if (!supportedParameters.Contains("League") && !string.IsNullOrWhiteSpace(filters.League))
        {
            filteredRows = filteredRows.Where(row =>
                (row.StandardizedLeague ?? row.League).Equals(filters.League.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!supportedParameters.Contains("OnlyPending") && filters.OnlyPending)
        {
            filteredRows = filteredRows.Where(row =>
                row.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        }

        if (!supportedParameters.Contains("Source") && !string.IsNullOrWhiteSpace(filters.Source))
        {
            filteredRows = filteredRows.Where(row =>
                row.Source.Equals(filters.Source.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!supportedParameters.Contains("MarketType") && !string.IsNullOrWhiteSpace(filters.MarketType))
        {
            filteredRows = filteredRows.Where(row =>
                row.MarketType.Equals(filters.MarketType.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return filteredRows.ToArray();
    }

    public async Task<AutomatedCornerSelectionDto> UpdateStatusAsync(
        long id,
        UpdateAutomatedCornerSelectionStatusRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("AutomatedCornerBetSelectionId", id, DbType.Int64);

        CommandDefinition updateCommand;
        if (request.Status.Equals("Void", StringComparison.OrdinalIgnoreCase))
        {
            parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            updateCommand = new CommandDefinition(
                "dbo.sp_VoidAutomatedCornerBetSelection",
                parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 300,
                cancellationToken: cancellationToken);
        }
        else
        {
            parameters.Add("Status", request.Status, DbType.String, size: 20);
            parameters.Add("ActualHomeCorners", request.ActualHomeCorners, DbType.Int32);
            parameters.Add("ActualAwayCorners", request.ActualAwayCorners, DbType.Int32);
            parameters.Add("ActualTotalCorners", request.ActualTotalCorners, DbType.Int32);
            updateCommand = new CommandDefinition(
                "dbo.sp_UpdateAutomatedCornerBetSelectionStatus",
                parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 300,
                cancellationToken: cancellationToken);
        }

        await connection.ExecuteAsync(updateCommand);

        if (request.Status.Equals("Void", StringComparison.OrdinalIgnoreCase) &&
            parameters.Get<int>("RowsAffected") == 0)
        {
            throw new KeyNotFoundException($"Automated corner selection {id} was not found.");
        }

        var selectCommand = new CommandDefinition(
            SelectSelectionByIdSql,
            new { Id = id },
            cancellationToken: cancellationToken);
        var updatedSelection = await connection.QuerySingleOrDefaultAsync<AutomatedCornerSelectionDto>(selectCommand);

        return updatedSelection ?? throw new KeyNotFoundException($"Automated corner selection {id} was not found.");
    }

    public async Task<AutomatedCornerSelectionDto> ResolveAsync(
        long id,
        int actualValue,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("AutomatedCornerBetSelectionId", id, DbType.Int64);
        parameters.Add("ActualValue", actualValue, DbType.Int32);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await connection.ExecuteAsync(new CommandDefinition(
            "dbo.sp_ResolveAutomatedCornerBetSelection",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken));

        if (parameters.Get<int>("RowsAffected") == 0)
        {
            throw new KeyNotFoundException($"Automated corner selection {id} was not found.");
        }

        var selectCommand = new CommandDefinition(
            SelectSelectionByIdSql,
            new { Id = id },
            cancellationToken: cancellationToken);
        var updatedSelection = await connection.QuerySingleOrDefaultAsync<AutomatedCornerSelectionDto>(selectCommand);

        return updatedSelection ?? throw new KeyNotFoundException($"Automated corner selection {id} was not found.");
    }

    public async Task<AutomatedCornerSelectionDto> LinkMatchAsync(
        long id,
        long matchHistoryId,
        long apiFootballFixtureId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE selection
            SET
                MatchHistoryId = history.Id,
                ApiFootballFixtureId = history.ApiFootballFixtureId,
                LastSettlementCheckReason = N'Pendiente de liquidar: partido enlazado mediante auditoría API-Football.',
                LastSettlementCheckAtUtc = SYSUTCDATETIME(),
                UpdatedAtUtc = SYSUTCDATETIME()
            FROM dbo.AutomatedCornerBetSelections selection
            INNER JOIN dbo.MatchHistory history
                ON history.Id = @MatchHistoryId
               AND history.ApiFootballFixtureId = @ApiFootballFixtureId
            WHERE selection.AutomatedCornerBetSelectionId = @Id
              AND selection.Status = N'Pending';
            """,
            new
            {
                Id = id,
                MatchHistoryId = matchHistoryId,
                ApiFootballFixtureId = apiFootballFixtureId
            },
            commandTimeout: 300,
            cancellationToken: cancellationToken));

        if (affected == 0)
        {
            throw new InvalidOperationException(
                $"Selection {id} is not pending, does not exist, or MatchHistory {matchHistoryId} does not belong to API-Football fixture {apiFootballFixtureId}.");
        }

        var updatedSelection = await connection.QuerySingleOrDefaultAsync<AutomatedCornerSelectionDto>(
            new CommandDefinition(
                SelectSelectionByIdSql,
                new { Id = id },
                cancellationToken: cancellationToken));

        return updatedSelection ?? throw new KeyNotFoundException($"Automated corner selection {id} was not found.");
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("AutomatedCornerBetSelectionId", id, DbType.Int64);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await connection.ExecuteAsync(new CommandDefinition(
            "dbo.sp_DeleteAutomatedCornerBetSelection",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken));

        return parameters.Get<int>("RowsAffected") > 0;
    }

    private static void AddParameter(
        DynamicParameters parameters,
        IReadOnlySet<string> supportedParameters,
        string name,
        object? value,
        DbType dbType,
        int? size = null)
    {
        if (supportedParameters.Count > 0 && !supportedParameters.Contains(name))
        {
            return;
        }

        parameters.Add(name, value, dbType, size: size);
    }

    private static async Task<IReadOnlySet<string>> GetStoredProcedureParametersAsync(
        SqlConnection connection,
        string procedureName,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            SELECT name
            FROM sys.parameters
            WHERE object_id = OBJECT_ID(@ProcedureName);
            """,
            new { ProcedureName = procedureName },
            cancellationToken: cancellationToken);

        var parameterNames = await connection.QueryAsync<string>(command);
        return parameterNames
            .Select(name => name.TrimStart('@'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
