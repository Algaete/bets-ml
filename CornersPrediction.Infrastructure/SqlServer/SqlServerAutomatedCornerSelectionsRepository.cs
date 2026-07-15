using System.Data;
using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerAutomatedCornerSelectionsRepository : IAutomatedCornerSelectionsRepository
{
    private readonly string _connectionString;
    private const string SelectSelectionByIdSql = """
        SELECT
            s.AutomatedCornerBetSelectionId,
            s.RunId,
            s.AutomationVersion,
            s.Source,
            s.SourceMatchId,
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
            s.ProfitLoss,
            s.YieldPct,
            s.DecisionReason,
            s.CreatedAtUtc,
            s.UpdatedAtUtc,
            s.SettledAtUtc
        FROM dbo.AutomatedCornerBetSelections s
        WHERE s.AutomatedCornerBetSelectionId = @Id;
        """;

    public SqlServerAutomatedCornerSelectionsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync(
        AutomatedCornerSelectionsFilterRequest filters,
        CancellationToken cancellationToken)
    {
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
        parameters.Add("Status", request.Status, DbType.String, size: 20);
        parameters.Add("ActualHomeCorners", request.ActualHomeCorners, DbType.Int32);
        parameters.Add("ActualAwayCorners", request.ActualAwayCorners, DbType.Int32);
        parameters.Add("ActualTotalCorners", request.ActualTotalCorners, DbType.Int32);

        var updateCommand = new CommandDefinition(
            "dbo.sp_UpdateAutomatedCornerBetSelectionStatus",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(updateCommand);

        var selectCommand = new CommandDefinition(
            SelectSelectionByIdSql,
            new { Id = id },
            cancellationToken: cancellationToken);
        var updatedSelection = await connection.QuerySingleOrDefaultAsync<AutomatedCornerSelectionDto>(selectCommand);

        return updatedSelection ?? throw new KeyNotFoundException($"Automated corner selection {id} was not found.");
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
