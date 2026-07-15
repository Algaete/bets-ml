namespace CornersPrediction.Domain.MatchHistory;

public sealed record MatchHistoryBulkImportRequest(
    string League,
    string Season,
    string FocusTeam,
    string TeamGender,
    bool IsKnockout,
    string MatchesJson);

