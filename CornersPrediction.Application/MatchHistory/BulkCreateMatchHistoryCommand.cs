namespace CornersPrediction.Application.MatchHistory;

public sealed record BulkCreateMatchHistoryCommand(
    string League,
    string Season,
    string FocusTeam,
    string? TeamGender,
    bool IsKnockout,
    string MatchesJson);

