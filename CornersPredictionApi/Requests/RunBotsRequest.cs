namespace CornersPredictionApi.Requests;

public sealed record RunBotsRequest(
    bool ExcludeExistingSelections = false,
    int BatchNumber = 1,
    int BatchSize = 100,
    bool RunBotC = true,
    bool RunAllEnabledBots = true);
