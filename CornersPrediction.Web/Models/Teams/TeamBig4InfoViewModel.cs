namespace CornersPrediction.Web.Models.Teams;

public sealed record TeamBi3InfoViewModel(
    string League,
    string Season,
    string Team,
    bool IsBig3,
    DateTime CreatedAt);
