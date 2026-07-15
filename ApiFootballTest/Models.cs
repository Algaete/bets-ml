namespace ApiFootballTest;

internal sealed record Coverage(
    bool Events,
    bool Lineups,
    bool FixtureStatistics,
    bool PlayerStatistics,
    bool Predictions,
    bool Odds);

internal sealed record LeagueSeason(
    int Year,
    DateOnly? Start,
    DateOnly? End,
    bool Current,
    Coverage Coverage);

internal sealed record LeagueInfo(
    int Id,
    string Name,
    string Type,
    string Country,
    IReadOnlyList<LeagueSeason> Seasons);

internal sealed record TeamInfo(
    int Id,
    string Name,
    string Country,
    int? Founded,
    string? Venue);

internal sealed record FixtureInfo(
    long Id,
    DateTimeOffset Date,
    string Status,
    string League,
    int Season,
    string Round,
    int HomeTeamId,
    string HomeTeam,
    int AwayTeamId,
    string AwayTeam,
    int? HomeGoals,
    int? AwayGoals);

internal sealed record MatchHistoryCandidate(
    DateOnly MatchDate,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation,
    string? AwayFormation,
    int? HomeGoals,
    int? AwayGoals,
    int? HomeCorners,
    int? AwayCorners,
    int? HomeShots,
    int? AwayShots,
    int? HomeShotsOnGoal,
    int? AwayShotsOnGoal,
    double? HomePossession,
    double? AwayPossession,
    string SourceMatchId);

internal sealed record FixtureProbe(
    FixtureInfo Fixture,
    MatchHistoryCandidate Candidate,
    bool HasStatistics,
    bool HasLineups,
    IReadOnlySet<string> StatisticTypes,
    ValidationResult Validation);

internal sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Reasons);
