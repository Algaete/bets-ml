namespace CornersPredictionApi.Requests;

public sealed record RunFullPipelineRequest(
    int MatchHistoryDays = 7,
    int UpcomingDays = 7,
    bool ExcludeExistingSelections = false);
