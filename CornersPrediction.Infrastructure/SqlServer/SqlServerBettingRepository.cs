using System.Data;
using CornersPrediction.Application.Betting;
using CornersPrediction.Domain.Betting;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerBettingRepository : IBettingRepository
{
    private readonly string _connectionString;

    public SqlServerBettingRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<BettingRecord> AddAsync(BettingRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = BuildRecordParameters(record);
        parameters.Add("InsertedId", dbType: DbType.Int64, direction: ParameterDirection.Output);

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
        var parameters = BuildRecordParameters(record);
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_UpdateBettingRecord",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        return parameters.Get<int>("RowsAffected");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_DeleteBettingRecord",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        return parameters.Get<int>("RowsAffected");
    }

    public async Task<BettingRecord?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);

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
        string currencyCode,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("CurrencyCode", currencyCode, DbType.String, size: 3);

        var command = new CommandDefinition(
            "dbo.sp_GetBankrollTransactions",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var transactions = await connection.QueryAsync<BankrollTransaction>(command);
        return transactions.ToArray();
    }

    public async Task<decimal> GetCurrentBankrollAsync(string currencyCode, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("CurrencyCode", currencyCode, DbType.String, size: 3);

        var command = new CommandDefinition(
            "dbo.sp_GetCurrentBankroll",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<decimal>(command);
    }

    private static DynamicParameters BuildRecordParameters(BettingRecord record)
    {
        var parameters = new DynamicParameters();
        parameters.Add("CurrencyCode", record.CurrencyCode, DbType.String, size: 3);
        parameters.Add("League", record.League, DbType.String, size: 100);
        parameters.Add("Season", record.Season, DbType.String, size: 20);
        parameters.Add("MatchDate", record.MatchDate.Date, DbType.Date);
        parameters.Add("HomeTeam", record.HomeTeam, DbType.String, size: 150);
        parameters.Add("AwayTeam", record.AwayTeam, DbType.String, size: 150);
        parameters.Add("Bookmaker", record.Bookmaker, DbType.String, size: 100);
        parameters.Add("MarketType", record.MarketType, DbType.String, size: 50);
        parameters.Add("BetSelection", record.BetSelection, DbType.String, size: 50);
        parameters.Add("Line", record.Line, DbType.Decimal);
        parameters.Add("Odds", record.Odds, DbType.Decimal);
        parameters.Add("Stake", record.Stake, DbType.Decimal);
        parameters.Add("Status", record.Status, DbType.String, size: 20);
        parameters.Add("ActualHomeCorners", record.ActualHomeCorners, DbType.Int32);
        parameters.Add("ActualAwayCorners", record.ActualAwayCorners, DbType.Int32);
        parameters.Add("ActualTotalCorners", record.ActualTotalCorners, DbType.Int32);
        parameters.Add("CashoutAmount", record.CashoutAmount, DbType.Decimal);
        parameters.Add("PotentialReturn", record.PotentialReturn, DbType.Decimal);
        parameters.Add("NetReturn", record.NetReturn, DbType.Decimal);
        parameters.Add("ProfitLoss", record.ProfitLoss, DbType.Decimal);
        parameters.Add("RoiPercent", record.RoiPercent, DbType.Decimal);
        parameters.Add("BankrollBefore", record.BankrollBefore, DbType.Decimal);
        parameters.Add("BankrollAfter", record.BankrollAfter, DbType.Decimal);
        parameters.Add("ClosingOdds", record.ClosingOdds, DbType.Decimal);
        parameters.Add("ConfidenceLevel", record.ConfidenceLevel, DbType.String, size: 20);
        parameters.Add("Notes", record.Notes, DbType.String);
        return parameters;
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
