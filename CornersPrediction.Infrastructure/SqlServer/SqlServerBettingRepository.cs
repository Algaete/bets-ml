using System.Data;
using CornersPrediction.Application.Betting;
using CornersPrediction.Domain.Betting;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerBettingRepository : IBettingRepository
{
    private static readonly SemaphoreSlim SchemaUpgradeLock = new(1, 1);
    private static bool _bettingSchemaUpgraded;
    private readonly string _connectionString;

    public SqlServerBettingRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<BettingRecord> AddAsync(BettingRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await EnsureBettingSchemaCompatibilityAsync(connection, cancellationToken);
        var supportedParameters = await GetStoredProcedureParametersAsync(connection, "dbo.sp_InsertBettingRecord", cancellationToken);
        var parameters = BuildRecordParameters(record, supportedParameters);
        AddParameter(parameters, supportedParameters, "InsertedId", dbType: DbType.Int64, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_InsertBettingRecord",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        record.Id = parameters.Get<long>("InsertedId");
        return record;
    }

    public async Task<int> UpdateAsync(long id, BettingRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await EnsureBettingSchemaCompatibilityAsync(connection, cancellationToken);
        var supportedParameters = await GetStoredProcedureParametersAsync(connection, "dbo.sp_UpdateBettingRecord", cancellationToken);
        var parameters = BuildRecordParameters(record, supportedParameters);
        AddParameter(parameters, supportedParameters, "Id", id, DbType.Int64);
        AddParameter(parameters, supportedParameters, "RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_UpdateBettingRecord",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        return parameters.Get<int>("RowsAffected");
    }

    public async Task<int> DeleteAsync(long id, string userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("UserId", userId, DbType.String, size: 450);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_DeleteBettingRecord",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        return parameters.Get<int>("RowsAffected");
    }

    public async Task<BettingRecord?> GetByIdAsync(long id, string userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("UserId", userId, DbType.String, size: 450);

        var command = new CommandDefinition(
            "dbo.sp_GetBettingRecordById",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<BettingRecord>(command);
    }

    public async Task<IReadOnlyList<BettingRecord>> GetAsync(
        BettingFiltersRequest filters,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            "dbo.sp_GetBettingRecords",
            BuildFilterParameters(filters),
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var records = await connection.QueryAsync<BettingRecord>(command);
        return records.ToArray();
    }

    public async Task<BettingSummaryDto> GetSummaryAsync(
        BettingFiltersRequest filters,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            "dbo.sp_GetBettingSummary",
            BuildFilterParameters(filters),
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<BettingSummaryDto>(command) ??
            new BettingSummaryDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    public async Task<BankrollTransaction> AddBankrollTransactionAsync(
        BankrollTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("UserId", transaction.UserId, DbType.String, size: 450);
        parameters.Add("CurrencyCode", transaction.CurrencyCode, DbType.String, size: 3);
        parameters.Add("TransactionDate", transaction.TransactionDate.Date, DbType.Date);
        parameters.Add("Type", transaction.Type, DbType.String, size: 30);
        parameters.Add("Amount", transaction.Amount, DbType.Decimal);
        parameters.Add("BettingRecordId", transaction.BettingRecordId, DbType.Int64);
        parameters.Add("Notes", transaction.Notes, DbType.String);
        parameters.Add("InsertedId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        parameters.Add("BalanceAfter", dbType: DbType.Decimal, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_InsertBankrollTransaction",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);

        transaction.Id = parameters.Get<long>("InsertedId");
        transaction.BalanceAfter = parameters.Get<decimal>("BalanceAfter");
        return transaction;
    }

    public async Task<BankrollTransaction?> ReconcileBetSettlementAsync(
        BettingRecord record,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("UserId", record.UserId, DbType.String, size: 450);
        parameters.Add("CurrencyCode", record.CurrencyCode, DbType.String, size: 3);
        parameters.Add("TransactionDate", DateTime.UtcNow.Date, DbType.Date);
        parameters.Add("BettingRecordId", record.Id, DbType.Int64);
        parameters.Add("DesiredAmount", GetSettlementAmount(record), DbType.Decimal);
        parameters.Add("Notes", $"Auto settlement for bet #{record.Id} ({record.Status})", DbType.String);
        parameters.Add("InsertedId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        parameters.Add("BalanceAfter", dbType: DbType.Decimal, direction: ParameterDirection.Output);
        parameters.Add("AdjustmentAmount", dbType: DbType.Decimal, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_ReconcileBetSettlementTransaction",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);

        var insertedId = parameters.Get<long>("InsertedId");
        if (insertedId <= 0)
        {
            return null;
        }

        return new BankrollTransaction
        {
            Id = insertedId,
            UserId = record.UserId,
            CurrencyCode = record.CurrencyCode,
            TransactionDate = DateTime.UtcNow.Date,
            Type = BankrollTransactionTypes.BetSettlement,
            Amount = parameters.Get<decimal>("AdjustmentAmount"),
            BalanceAfter = parameters.Get<decimal>("BalanceAfter"),
            BettingRecordId = record.Id,
            Notes = $"Auto settlement for bet #{record.Id} ({record.Status})",
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<BankrollTransaction>> GetBankrollTransactionsAsync(
        string userId,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId, DbType.String, size: 450);
        parameters.Add("CurrencyCode", currencyCode, DbType.String, size: 3);

        var command = new CommandDefinition(
            "dbo.sp_GetBankrollTransactions",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var transactions = await connection.QueryAsync<BankrollTransaction>(command);
        return transactions.ToArray();
    }

    public async Task<decimal> GetCurrentBankrollAsync(string userId, string currencyCode, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId, DbType.String, size: 450);
        parameters.Add("CurrencyCode", currencyCode, DbType.String, size: 3);

        var command = new CommandDefinition(
            "dbo.sp_GetCurrentBankroll",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<decimal>(command);
    }

    private static DynamicParameters BuildRecordParameters(
        BettingRecord record,
        IReadOnlySet<string>? supportedParameters = null)
    {
        var parameters = new DynamicParameters();
        AddParameter(parameters, supportedParameters, "UserId", record.UserId, DbType.String, size: 450);
        AddParameter(parameters, supportedParameters, "CurrencyCode", record.CurrencyCode, DbType.String, size: 3);
        AddParameter(parameters, supportedParameters, "League", record.League, DbType.String, size: 100);
        AddParameter(parameters, supportedParameters, "Season", record.Season, DbType.String, size: 20);
        AddParameter(parameters, supportedParameters, "MatchDate", record.MatchDate.Date, DbType.Date);
        AddParameter(parameters, supportedParameters, "HomeTeam", record.HomeTeam, DbType.String, size: 150);
        AddParameter(parameters, supportedParameters, "AwayTeam", record.AwayTeam, DbType.String, size: 150);
        AddParameter(parameters, supportedParameters, "Bookmaker", record.Bookmaker, DbType.String, size: 100);
        AddParameter(parameters, supportedParameters, "MarketType", record.MarketType, DbType.String, size: 50);
        AddParameter(parameters, supportedParameters, "BetSelection", record.BetSelection, DbType.String, size: 50);
        AddParameter(parameters, supportedParameters, "Line", record.Line, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "Odds", record.Odds, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "Stake", record.Stake, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "Status", record.Status, DbType.String, size: 20);
        AddParameter(parameters, supportedParameters, "ActualHomeCorners", record.ActualHomeCorners, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualAwayCorners", record.ActualAwayCorners, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualTotalCorners", record.ActualTotalCorners, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualHomeShots", record.ActualHomeShots, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualAwayShots", record.ActualAwayShots, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualTotalShots", record.ActualTotalShots, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualHomeShotsOnGoal", record.ActualHomeShotsOnGoal, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualAwayShotsOnGoal", record.ActualAwayShotsOnGoal, DbType.Int32);
        AddParameter(parameters, supportedParameters, "ActualTotalShotsOnGoal", record.ActualTotalShotsOnGoal, DbType.Int32);
        AddParameter(parameters, supportedParameters, "CashoutAmount", record.CashoutAmount, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "PotentialReturn", record.PotentialReturn, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "NetReturn", record.NetReturn, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "ProfitLoss", record.ProfitLoss, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "RoiPercent", record.RoiPercent, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "BankrollBefore", record.BankrollBefore, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "BankrollAfter", record.BankrollAfter, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "ClosingOdds", record.ClosingOdds, DbType.Decimal);
        AddParameter(parameters, supportedParameters, "ConfidenceLevel", record.ConfidenceLevel, DbType.String, size: 20);
        AddParameter(parameters, supportedParameters, "PredictionModel", record.PredictionModel, DbType.String, size: 50);
        AddParameter(parameters, supportedParameters, "Notes", record.Notes, DbType.String);
        return parameters;
    }

    private static void AddParameter(
        DynamicParameters parameters,
        IReadOnlySet<string>? supportedParameters,
        string name,
        object? value = null,
        DbType? dbType = null,
        ParameterDirection? direction = null,
        int? size = null)
    {
        if (supportedParameters is not null && !supportedParameters.Contains(name))
        {
            return;
        }

        parameters.Add(name, value, dbType, direction, size);
    }

    private static async Task<IReadOnlySet<string>> GetStoredProcedureParametersAsync(
        SqlConnection connection,
        string procedureName,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            SELECT ParameterName = REPLACE(p.name, '@', '')
            FROM sys.parameters p
            WHERE p.object_id = OBJECT_ID(@ProcedureName);
            """,
            new { ProcedureName = procedureName },
            cancellationToken: cancellationToken);

        var names = await connection.QueryAsync<string>(command);
        var parameters = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parameters.Count == 0)
        {
            throw new InvalidOperationException($"Stored procedure '{procedureName}' was not found or has no parameters.");
        }

        return parameters;
    }

    private static async Task EnsureBettingSchemaCompatibilityAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (_bettingSchemaUpgraded)
        {
            return;
        }

        await SchemaUpgradeLock.WaitAsync(cancellationToken);
        try
        {
            if (_bettingSchemaUpgraded)
            {
                return;
            }

            var command = new CommandDefinition(
                """
                IF COL_LENGTH('dbo.BettingRecords', 'ActualHomeShots') IS NULL
                BEGIN
                    ALTER TABLE dbo.BettingRecords ADD
                        ActualHomeShots INT NULL,
                        ActualAwayShots INT NULL,
                        ActualTotalShots INT NULL,
                        ActualHomeShotsOnGoal INT NULL,
                        ActualAwayShotsOnGoal INT NULL,
                        ActualTotalShotsOnGoal INT NULL;
                END;

                IF COL_LENGTH('dbo.BettingRecords', 'PredictionModel') IS NULL
                BEGIN
                    ALTER TABLE dbo.BettingRecords
                        ADD PredictionModel NVARCHAR(50) NOT NULL
                            CONSTRAINT DF_BettingRecords_PredictionModel DEFAULT 'Manual' WITH VALUES;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = 'CK_BettingRecords_MarketType'
                      AND parent_object_id = OBJECT_ID('dbo.BettingRecords')
                )
                BEGIN
                    ALTER TABLE dbo.BettingRecords DROP CONSTRAINT CK_BettingRecords_MarketType;
                END;

                ALTER TABLE dbo.BettingRecords
                    ADD CONSTRAINT CK_BettingRecords_MarketType CHECK
                    (
                        MarketType IN
                        (
                            'TotalCorners',
                            'HomeCorners',
                            'AwayCorners',
                            'FirstHalfCorners',
                            'TotalShots',
                            'HomeShots',
                            'AwayShots',
                            'TotalShotsOnGoal',
                            'HomeShotsOnGoal',
                            'AwayShotsOnGoal',
                            'TotalGoals',
                            'HomeGoals',
                            'AwayGoals',
                            'Other'
                        )
                    );

                IF EXISTS
                (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = 'CK_BettingRecords_PredictionModel'
                      AND parent_object_id = OBJECT_ID('dbo.BettingRecords')
                )
                BEGIN
                    ALTER TABLE dbo.BettingRecords DROP CONSTRAINT CK_BettingRecords_PredictionModel;
                END;

                ALTER TABLE dbo.BettingRecords
                    ADD CONSTRAINT CK_BettingRecords_PredictionModel CHECK
                    (
                        PredictionModel IN
                        (
                            'Manual',
                            'TotalCornersModel',
                            'OverUnderLineModel',
                            'ShotsOnGoalModel',
                            'GoalsModel',
                            'AutomatedCornersBot'
                        )
                    );

                IF EXISTS
                (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = 'CK_BettingRecords_ConfidenceLevel'
                      AND parent_object_id = OBJECT_ID('dbo.BettingRecords')
                )
                BEGIN
                    ALTER TABLE dbo.BettingRecords DROP CONSTRAINT CK_BettingRecords_ConfidenceLevel;
                END;

                ALTER TABLE dbo.BettingRecords
                    ADD CONSTRAINT CK_BettingRecords_ConfidenceLevel CHECK
                    (
                        ConfidenceLevel IS NULL
                        OR ConfidenceLevel IN ('Low', 'Medium', 'High', 'VeryHigh')
                    );
                """,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);
            _bettingSchemaUpgraded = true;
        }
        finally
        {
            SchemaUpgradeLock.Release();
        }
    }

    private static decimal GetSettlementAmount(BettingRecord record)
    {
        return record.Status is BetStatuses.Won or BetStatuses.Lost or BetStatuses.Cashout
            ? record.ProfitLoss
            : 0;
    }

    private static DynamicParameters BuildFilterParameters(BettingFiltersRequest filters)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", filters.UserId, DbType.String, size: 450);
        parameters.Add("CurrencyCode", filters.CurrencyCode, DbType.String, size: 3);
        parameters.Add("League", filters.League, DbType.String, size: 100);
        parameters.Add("Season", filters.Season, DbType.String, size: 20);
        parameters.Add("HomeTeam", filters.HomeTeam, DbType.String, size: 150);
        parameters.Add("AwayTeam", filters.AwayTeam, DbType.String, size: 150);
        parameters.Add("Status", filters.Status, DbType.String, size: 20);
        parameters.Add("MarketType", filters.MarketType, DbType.String, size: 50);
        parameters.Add("Bookmaker", filters.Bookmaker, DbType.String, size: 100);
        parameters.Add("DateFrom", filters.DateFrom?.Date, DbType.Date);
        parameters.Add("DateTo", filters.DateTo?.Date, DbType.Date);
        return parameters;
    }
}
