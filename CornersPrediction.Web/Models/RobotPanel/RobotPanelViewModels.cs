using System.Text.Json;

namespace CornersPrediction.Web.Models.RobotPanel;

public sealed class RobotPanelIndexViewModel
{
    public IReadOnlyList<int> DayOptions { get; init; } = new[] { 2, 4, 7, 14 };

    public int SelectedDays { get; init; } = 7;

    public int UpcomingDays { get; init; } = 7;
}

public sealed class RobotPanelStepRequestViewModel
{
    public int Days { get; init; } = 7;
}

public sealed class RobotPanelBotsRequestViewModel
{
    public bool ExcludeExistingSelections { get; init; }

    public int BatchNumber { get; init; } = 1;

    public int BatchSize { get; init; } = 100;

    public bool RunAllEnabledBots { get; init; } = true;
}

public sealed class RobotPanelFullRunRequestViewModel
{
    public int MatchHistoryDays { get; init; } = 7;

    public int UpcomingDays { get; init; } = 7;

    public bool ExcludeExistingSelections { get; init; }

    public int BotBatchNumber { get; init; } = 1;

    public int BotBatchSize { get; init; } = 100;

    public bool RunAllEnabledBots { get; init; } = true;
}

public sealed class RobotPanelBotAvailabilityViewModel
{
    public DateOnly DateFrom { get; init; }
    public DateOnly DateTo { get; init; }
    public int TotalOddsRows { get; init; }
    public int TotalMatches { get; init; }
    public int BatchSize { get; init; }
    public int TotalBatches { get; init; }
}

public sealed class ApiFootballHistoricalBatchViewModel
{
    public string Status { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public DateOnly Month { get; init; }
    public int CompetitionOffset { get; init; }
    public DateOnly NextMonth { get; init; }
    public int NextCompetitionOffset { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public int? DiscoveredFixtures { get; init; }
    public int? EligibleCompetitions { get; init; }
    public int? ProcessedCompetitions { get; init; }
    public int? ProcessedFixtures { get; init; }
    public int? Inserted { get; init; }
    public int? Updated { get; init; }
    public int? Skipped { get; init; }
    public int? Errors { get; init; }
    public bool? StoppedByQuota { get; init; }
    public string? DailyRemaining { get; init; }
    public string? MinuteRemaining { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class RobotPanelStepResultViewModel
{
    public string StepKey { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
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
    public IReadOnlyDictionary<string, int> BotCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MissingHistoryMatchViewModel> MissingHistoryMatches { get; init; } = Array.Empty<MissingHistoryMatchViewModel>();
    public JsonElement? RawResponse { get; init; }
}

public sealed class MissingHistoryMatchViewModel
{
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public DateTime MatchDate { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class RobotPanelRunResultViewModel
{
    public string PipelineName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int MatchHistoryDays { get; init; }
    public int UpcomingDays { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public int SuccessfulSteps { get; init; }
    public int FailedSteps { get; init; }
    public int TimedOutSteps { get; init; }
    public int RecommendationsGenerated { get; init; }
    public IReadOnlyList<RobotPanelStepResultViewModel> Steps { get; init; } = Array.Empty<RobotPanelStepResultViewModel>();
}
