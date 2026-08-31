using System.Security.Cryptography;
using System.Text;

namespace CornersPrediction.Application.Automation;

public static class RecommendationJobModes
{
    public const string HistoricalBackfill = "HistoricalBackfill";
    public const string Live = "Live";
}

public static class RecommendationJobStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public sealed record CreateRecommendationJobCommand(
    DateOnly DateFrom,
    DateOnly DateTo,
    string? Name = null,
    IReadOnlyCollection<string>? BotKeys = null,
    IReadOnlyCollection<string>? MarketFamilies = null,
    string Mode = RecommendationJobModes.HistoricalBackfill,
    int BatchSize = 25,
    int MaxAttempts = 3);

public sealed record RecommendationJobDto(
    Guid RecommendationJobId,
    string Name,
    string Status,
    string Mode,
    DateOnly DateFrom,
    DateOnly DateTo,
    IReadOnlyList<string> BotKeys,
    IReadOnlyList<string> MarketFamilies,
    int BatchSize,
    int NextBatchNumber,
    int? TotalBatches,
    int ProcessedBatches,
    int SelectedMatches,
    int InsertedRows,
    int UpdatedRows,
    int SkippedMatches,
    int ErrorMatches,
    int AttemptCount,
    int MaxAttempts,
    Guid? LastRunId,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc)
{
    public bool IsTerminal =>
        Status is RecommendationJobStatuses.Completed
            or RecommendationJobStatuses.Failed
            or RecommendationJobStatuses.Cancelled;
}

public sealed record RecommendationJobBatchProgress(
    int CompletedBatchNumber,
    int TotalBatches,
    Guid RunId,
    int SelectedMatches,
    int InsertedRows,
    int UpdatedRows,
    int SkippedMatches,
    int ErrorMatches);

public interface IRecommendationJobRepository
{
    Task<RecommendationJobDto> EnqueueAsync(
        CreateRecommendationJobCommand command,
        string requestHash,
        CancellationToken cancellationToken);

    Task<RecommendationJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationJobDto>> ListAsync(int take, CancellationToken cancellationToken);

    Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken);

    Task<RecommendationJobDto?> TryClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<RecommendationJobDto?> CompleteBatchAsync(
        Guid jobId,
        string workerId,
        RecommendationJobBatchProgress progress,
        CancellationToken cancellationToken);

    Task<RecommendationJobDto?> RecordFailureAsync(
        Guid jobId,
        string workerId,
        string error,
        CancellationToken cancellationToken);
}

public interface IRecommendationJobsUseCase
{
    Task<RecommendationJobDto> EnqueueAsync(
        CreateRecommendationJobCommand command,
        CancellationToken cancellationToken);

    Task<RecommendationJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationJobDto>> ListAsync(int take, CancellationToken cancellationToken);

    Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken);
}

public sealed class RecommendationJobsUseCase : IRecommendationJobsUseCase
{
    private static readonly string[] DefaultBotKeys = ["C2026"];
    private static readonly string[] DefaultMarketFamilies = ["CORNERS", "GOALS", "SHOTS", "SOG"];
    private static readonly HashSet<string> AllowedMarketFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "CORNERS",
        "GOALS",
        "SHOTS",
        "SOG"
    };

    private readonly IRecommendationJobRepository _repository;
    private readonly IRecommendationBotDefinitionRepository _botDefinitionRepository;

    public RecommendationJobsUseCase(
        IRecommendationJobRepository repository,
        IRecommendationBotDefinitionRepository botDefinitionRepository)
    {
        _repository = repository;
        _botDefinitionRepository = botDefinitionRepository;
    }

    public async Task<RecommendationJobDto> EnqueueAsync(
        CreateRecommendationJobCommand command,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(command);
        var definitions = await _botDefinitionRepository.GetByKeysAsync(
            normalized.BotKeys!,
            cancellationToken);
        var foundKeys = definitions.Select(definition => definition.BotKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = normalized.BotKeys!.Where(botKey => !foundKeys.Contains(botKey)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown bot keys: {string.Join(", ", missing)}.");
        }

        var disabled = definitions
            .Where(definition =>
                !definition.IsEnabled ||
                RecommendationBotLifecycle.IsRetired(definition.BotKey))
            .Select(definition => definition.BotKey)
            .ToArray();
        if (disabled.Length > 0)
        {
            throw new ArgumentException($"Disabled bot keys cannot be executed: {string.Join(", ", disabled)}.");
        }

        return await _repository.EnqueueAsync(normalized, BuildRequestHash(normalized), cancellationToken);
    }

    public Task<RecommendationJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Recommendation job id is required.");
        }

        return _repository.GetAsync(jobId, cancellationToken);
    }

    public Task<IReadOnlyList<RecommendationJobDto>> ListAsync(int take, CancellationToken cancellationToken) =>
        _repository.ListAsync(Math.Clamp(take, 1, 200), cancellationToken);

    public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Recommendation job id is required.");
        }

        return _repository.CancelAsync(jobId, cancellationToken);
    }

    private static CreateRecommendationJobCommand Normalize(CreateRecommendationJobCommand command)
    {
        if (command.DateTo < command.DateFrom)
        {
            throw new ArgumentException("DateTo cannot be earlier than DateFrom.");
        }

        if (command.DateTo.DayNumber - command.DateFrom.DayNumber > 3650)
        {
            throw new ArgumentException("A recommendation job cannot cover more than 3650 days.");
        }

        var mode = string.IsNullOrWhiteSpace(command.Mode)
            ? RecommendationJobModes.HistoricalBackfill
            : command.Mode.Trim();
        if (!mode.Equals(RecommendationJobModes.HistoricalBackfill, StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals(RecommendationJobModes.Live, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Mode must be HistoricalBackfill or Live.");
        }

        mode = mode.Equals(RecommendationJobModes.Live, StringComparison.OrdinalIgnoreCase)
            ? RecommendationJobModes.Live
            : RecommendationJobModes.HistoricalBackfill;
        var botKeys = (command.BotKeys is null || command.BotKeys.Count == 0 ? DefaultBotKeys : command.BotKeys)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(RecommendationBotDefinitionsUseCase.NormalizeBotKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (botKeys.Length == 0)
        {
            throw new ArgumentException("At least one bot is required.");
        }
        var marketFamilies = NormalizeValues(
            command.MarketFamilies,
            DefaultMarketFamilies,
            static value => value.Trim().ToUpperInvariant(),
            AllowedMarketFamilies,
            "market family");
        var name = string.IsNullOrWhiteSpace(command.Name)
            ? $"{mode} {command.DateFrom:yyyy-MM-dd}..{command.DateTo:yyyy-MM-dd}"
            : command.Name.Trim();
        if (name.Length > 150)
        {
            throw new ArgumentException("Recommendation job name cannot exceed 150 characters.");
        }

        return command with
        {
            Name = name,
            BotKeys = botKeys,
            MarketFamilies = marketFamilies,
            Mode = mode,
            BatchSize = Math.Clamp(command.BatchSize, 1, 100),
            MaxAttempts = Math.Clamp(command.MaxAttempts, 1, 10)
        };
    }

    private static IReadOnlyList<string> NormalizeValues(
        IReadOnlyCollection<string>? values,
        IReadOnlyCollection<string> defaults,
        Func<string, string> normalize,
        IReadOnlySet<string> allowed,
        string label)
    {
        var normalized = (values is null || values.Count == 0 ? defaults : values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"At least one {label} is required.");
        }

        var unsupported = normalized.Where(value => !allowed.Contains(value)).ToArray();
        if (unsupported.Length > 0)
        {
            throw new ArgumentException($"Unsupported {label}: {string.Join(", ", unsupported)}.");
        }

        return normalized;
    }

    private static string BuildRequestHash(CreateRecommendationJobCommand command)
    {
        var canonical = string.Join(
            "|",
            command.Mode,
            command.DateFrom.ToString("yyyy-MM-dd"),
            command.DateTo.ToString("yyyy-MM-dd"),
            string.Join(',', command.BotKeys!),
            string.Join(',', command.MarketFamilies!),
            command.BatchSize,
            command.MaxAttempts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
