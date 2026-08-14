using System.Text.Json;

namespace CornersPrediction.Application.Automation;

public static class CornersPipelineStatuses
{
    public const string Success = "Success";
    public const string PartialSuccess = "PartialSuccess";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
}

public sealed record RunPipelineStepCommand(int Days = 7);

public sealed record RunBotsCommand(
    bool ExcludeExistingSelections = false,
    int BatchNumber = 1,
    int BatchSize = 100,
    bool RunBotC = true);

public sealed record RunFullPipelineCommand(
    int MatchHistoryDays = 7,
    int UpcomingDays = 7,
    bool ExcludeExistingSelections = false,
    int BotBatchNumber = 1,
    int BotBatchSize = 100,
    bool RunBotC = true);

public sealed record BotOddsAvailability(
    DateOnly DateFrom,
    DateOnly DateTo,
    int TotalOddsRows,
    int TotalMatches,
    int BatchSize,
    int TotalBatches);

public sealed record CornersPipelineStepResult
{
    public string StepKey { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string Status { get; init; } = CornersPipelineStatuses.Success;
    public bool IsSuccess { get; init; }
    public bool TimedOut { get; init; }
    public string? Message { get; init; }
    public string? ErrorMessage { get; init; }
    public int? Days { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public int? Discovered { get; init; }
    public int? Processed { get; init; }
    public int? Inserted { get; init; }
    public int? Updated { get; init; }
    public int? Upserted { get; init; }
    public int? Duplicates { get; init; }
    public int? Skipped { get; init; }
    public int? Errors { get; init; }
    public int? RecommendationsGenerated { get; init; }
    public int? BotACount { get; init; }
    public int? BotBCount { get; init; }
    public int? BotCCount { get; init; }
    public IReadOnlyList<MissingHistoryMatch> MissingHistoryMatches { get; init; } = Array.Empty<MissingHistoryMatch>();
    public JsonElement? RawResponse { get; init; }
}

public sealed record MissingHistoryMatch(
    string League,
    string HomeTeam,
    string AwayTeam,
    DateTime MatchDate,
    string Reason);

public sealed record CornersPipelineRunResult
{
    public string PipelineName { get; init; } = "Full robot pipeline";
    public string Status { get; init; } = CornersPipelineStatuses.Success;
    public int MatchHistoryDays { get; init; }
    public int UpcomingDays { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public int SuccessfulSteps { get; init; }
    public int FailedSteps { get; init; }
    public int TimedOutSteps { get; init; }
    public int RecommendationsGenerated { get; init; }
    public IReadOnlyList<CornersPipelineStepResult> Steps { get; init; } = Array.Empty<CornersPipelineStepResult>();
}
