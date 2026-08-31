using System.Data;
using CornersPrediction.Application.Automation;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerRecommendationJobRepository : IRecommendationJobRepository
{
    private readonly string _connectionString;

    public SqlServerRecommendationJobRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<RecommendationJobDto> EnqueueAsync(
        CreateRecommendationJobCommand command,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("RecommendationJobId", Guid.NewGuid(), DbType.Guid);
        parameters.Add("Name", command.Name, DbType.String, size: 150);
        parameters.Add("Mode", command.Mode, DbType.String, size: 30);
        parameters.Add("DateFrom", command.DateFrom.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("DateTo", command.DateTo.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("BotKeys", string.Join(',', command.BotKeys!), DbType.String, size: 500);
        parameters.Add("MarketFamilies", string.Join(',', command.MarketFamilies!), DbType.String, size: 200);
        parameters.Add("BatchSize", command.BatchSize, DbType.Int32);
        parameters.Add("MaxAttempts", command.MaxAttempts, DbType.Int32);
        parameters.Add("RequestHash", requestHash, DbType.String, size: 64);

        var row = await QuerySingleAsync(
            "dbo.sp_EnqueueAutomatedRecommendationJob",
            parameters,
            cancellationToken);
        return row ?? throw new InvalidOperationException("The recommendation job could not be enqueued.");
    }

    public Task<RecommendationJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken) =>
        QuerySingleAsync(
            "dbo.sp_GetAutomatedRecommendationJob",
            new { RecommendationJobId = jobId },
            cancellationToken);

    public async Task<IReadOnlyList<RecommendationJobDto>> ListAsync(
        int take,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            "dbo.sp_ListAutomatedRecommendationJobs",
            new { Take = Math.Clamp(take, 1, 200) },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<RecommendationJobRow>(command);
        return rows.Select(ToDto).ToArray();
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            "dbo.sp_CancelAutomatedRecommendationJob",
            new { RecommendationJobId = jobId },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<int>(command) > 0;
    }

    public Task<RecommendationJobDto?> TryClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            "dbo.sp_ClaimNextAutomatedRecommendationJob",
            new
            {
                WorkerId = workerId,
                LeaseSeconds = Math.Clamp((int)leaseDuration.TotalSeconds, 60, 86400)
            },
            cancellationToken);

    public async Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            "dbo.sp_HeartbeatAutomatedRecommendationJob",
            new
            {
                RecommendationJobId = jobId,
                WorkerId = workerId,
                LeaseSeconds = Math.Clamp((int)leaseDuration.TotalSeconds, 60, 86400)
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 30,
            cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<int>(command) > 0;
    }

    public Task<RecommendationJobDto?> CompleteBatchAsync(
        Guid jobId,
        string workerId,
        RecommendationJobBatchProgress progress,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            "dbo.sp_CompleteAutomatedRecommendationJobBatch",
            new
            {
                RecommendationJobId = jobId,
                WorkerId = workerId,
                progress.CompletedBatchNumber,
                progress.TotalBatches,
                progress.RunId,
                progress.SelectedMatches,
                progress.InsertedRows,
                progress.UpdatedRows,
                progress.SkippedMatches,
                progress.ErrorMatches
            },
            cancellationToken);

    public Task<RecommendationJobDto?> RecordFailureAsync(
        Guid jobId,
        string workerId,
        string error,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            "dbo.sp_FailAutomatedRecommendationJobBatch",
            new
            {
                RecommendationJobId = jobId,
                WorkerId = workerId,
                ErrorMessage = error.Length <= 2000 ? error : error[..2000]
            },
            cancellationToken);

    private async Task<RecommendationJobDto?> QuerySingleAsync(
        string procedureName,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 120,
            cancellationToken: cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RecommendationJobRow>(command);
        return row is null ? null : ToDto(row);
    }

    private static RecommendationJobDto ToDto(RecommendationJobRow row) =>
        new(
            row.RecommendationJobId,
            row.Name,
            row.Status,
            row.Mode,
            DateOnly.FromDateTime(row.DateFrom),
            DateOnly.FromDateTime(row.DateTo),
            SplitValues(row.BotKeys),
            SplitValues(row.MarketFamilies),
            row.BatchSize,
            row.NextBatchNumber,
            row.TotalBatches,
            row.ProcessedBatches,
            row.SelectedMatches,
            row.InsertedRows,
            row.UpdatedRows,
            row.SkippedMatches,
            row.ErrorMatches,
            row.AttemptCount,
            row.MaxAttempts,
            row.LastRunId,
            row.LastError,
            row.CreatedAtUtc,
            row.StartedAtUtc,
            row.UpdatedAtUtc,
            row.CompletedAtUtc);

    private static IReadOnlyList<string> SplitValues(string values) =>
        values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class RecommendationJobRow
    {
        public Guid RecommendationJobId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public DateTime DateFrom { get; init; }
        public DateTime DateTo { get; init; }
        public string BotKeys { get; init; } = string.Empty;
        public string MarketFamilies { get; init; } = string.Empty;
        public int BatchSize { get; init; }
        public int NextBatchNumber { get; init; }
        public int? TotalBatches { get; init; }
        public int ProcessedBatches { get; init; }
        public int SelectedMatches { get; init; }
        public int InsertedRows { get; init; }
        public int UpdatedRows { get; init; }
        public int SkippedMatches { get; init; }
        public int ErrorMatches { get; init; }
        public int AttemptCount { get; init; }
        public int MaxAttempts { get; init; }
        public Guid? LastRunId { get; init; }
        public string? LastError { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? StartedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
    }
}
