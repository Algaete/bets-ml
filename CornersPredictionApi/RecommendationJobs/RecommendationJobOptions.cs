namespace CornersPredictionApi.RecommendationJobs;

public sealed class RecommendationJobOptions
{
    public const string SectionName = "RecommendationJobs";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int LeaseMinutes { get; set; } = 60;
    public bool ReconcileBotPicksAfterCompletion { get; set; } = true;
    public int ReconciliationMaxSelections { get; set; } = 20000;
    public RecurringRecommendationJobOptions Recurring { get; set; } = new();
}

public sealed class RecurringRecommendationJobOptions
{
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 360;
    public int LookAheadDays { get; set; } = 7;
    public int BatchSize { get; set; } = 25;
    public int MaxAttempts { get; set; } = 3;
    public string[] BotKeys { get; set; } = ["C2026"];
    public string[] MarketFamilies { get; set; } = ["CORNERS", "GOALS", "SHOTS", "SOG"];
}
