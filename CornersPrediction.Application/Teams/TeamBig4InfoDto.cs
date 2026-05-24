namespace CornersPrediction.Application.Teams;

public sealed record TeamBi3InfoDto(
    string League,
    string Season,
    string Team,
    bool IsBig3,
    DateTime CreatedAt);
