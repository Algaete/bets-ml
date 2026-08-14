namespace CornersPrediction.Infrastructure.Options;

public sealed class CornersAutomationOptions
{
    public const string SectionName = "CornersAutomation";

    public string CornersDataApiBaseUrl { get; init; } = "http://localhost:5070";

    public string CornersBotApiBaseUrl { get; init; } = "http://localhost:5070";

    public int DefaultTimeoutSeconds { get; init; } = 120;

    public int BetanoTimeoutSeconds { get; init; } = 900;

    public int BotTimeoutSeconds { get; init; } = 300;

    public int ApiFootballMaxCompetitions { get; init; } = 500;

    public int ApiFootballMaxFixturesPerCompetition { get; init; } = 1000;

    public int ApiFootballMaxTotalFixtures { get; init; } = 7000;

    public int ApiFootballMinimumDailyRemaining { get; init; } = 0;

    public int ApiFootballTimeoutSeconds { get; init; } = 900;

    public int PinnacleTake { get; init; } = 100;

    public int BetanoTake { get; init; } = 40;

    public int DefaultUpcomingDays { get; init; } = 7;

    public bool BotExcludeExistingSelections { get; init; } = false;
}
