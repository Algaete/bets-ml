namespace CornersPrediction.Infrastructure.Options;

public sealed class CornersAutomationOptions
{
    public const string SectionName = "CornersAutomation";

    public string CornersDataApiBaseUrl { get; init; } = "http://localhost:5070";

    public string CornersBotApiBaseUrl { get; init; } = "http://localhost:5070";

    public int DefaultTimeoutSeconds { get; init; } = 120;

    public int BetanoTimeoutSeconds { get; init; } = 420;

    public int BotTimeoutSeconds { get; init; } = 300;

    public int MatchHistoryTake { get; init; } = 10000;

    public int MatchHistoryParallelism { get; init; } = 4;

    public int PinnacleTake { get; init; } = 30;

    public int BetanoTake { get; init; } = 10;

    public int DefaultUpcomingDays { get; init; } = 7;

    public bool BotExcludeExistingSelections { get; init; } = false;
}
