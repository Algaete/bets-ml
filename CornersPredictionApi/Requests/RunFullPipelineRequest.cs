namespace CornersPredictionApi.Requests;

public sealed record RunFullPipelineRequest(
    int MatchHistoryDays = 7,
    int UpcomingDays = 7,
    bool ExcludeExistingSelections = false,
    int BotBatchNumber = 1,
    int BotBatchSize = 100,
    bool RunBotC = true);
