namespace CornersPredictionApi.ApiFootball;

public sealed class ApiFootballOptions
{
    public const string SectionName = "ApiFootball";

    public string BaseUrl { get; init; } = "https://v3.football.api-sports.io";
    public string? ApiKey { get; init; }
    public int RequestDelayMilliseconds { get; init; } = 250;
    public int FixtureParallelism { get; init; } = 8;
    public int CompetitionParallelism { get; init; } = 4;
    public int DatabaseWriteParallelism { get; init; } = 8;
    public int TimeoutSeconds { get; init; } = 45;
}

public sealed record ApiFootballSyncRequest(
    int LeagueId,
    int Season,
    string? DbLeague = null,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    int MaxFixtures = 20,
    bool DryRun = true,
    bool UpdateExisting = true,
    bool SyncStandings = true,
    bool SyncLineups = true);

public sealed record ApiFootballBotPickReconciliationRequest(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    int MaxSelections = 5000,
    bool DryRun = false);

public sealed record ApiFootballBotPickReconciliationResult(
    DateOnly? DateFrom,
    DateOnly DateTo,
    bool DryRun,
    int InitialReviewed,
    int InitialSettled,
    int PendingAfterLocalSettlement,
    int FixtureDatesQueried,
    int FixturesDiscovered,
    int MatchedSelections,
    int UniqueMatchedFixtures,
    int SyncedFixtures,
    int LinkedSelections,
    int UnmatchedSelections,
    int AmbiguousSelections,
    int MissingMarketStatistics,
    int FinalReviewed,
    int FinalSettled,
    int FinalWon,
    int FinalLost,
    int FinalPush,
    int StillPending,
    string? DailyRemaining,
    string? MinuteRemaining,
    IReadOnlyList<ApiFootballBotPickReconciliationRow> Rows);

public sealed record ApiFootballBotPickReconciliationRow(
    long SelectionId,
    string BotKey,
    string MarketType,
    DateTime MatchDate,
    string HomeTeam,
    string AwayTeam,
    long? FixtureId,
    long? MatchHistoryId,
    string MatchStatus,
    double? Confidence,
    string Result,
    string Message);

public sealed record ApiFootballDiscoveryRequest(
    DateOnly DateFrom,
    DateOnly DateTo);

public sealed record ApiFootballUpcomingSyncRequest(
    DateOnly DateFrom,
    DateOnly DateTo);

public sealed record ApiFootballUpcomingSyncResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    int DiscoveredFixtures,
    int EligibleFixtures,
    int ExcludedFixtures,
    int PersistedFixtures,
    string? DailyRemaining,
    string? MinuteRemaining,
    IReadOnlyList<ApiFootballUpcomingDailySummary> Daily);

public sealed record ApiFootballUpcomingDailySummary(
    DateOnly Date,
    int Fixtures);

public sealed record ApiFootballDiscoveryResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    int FinishedFixtures,
    int Competitions,
    string? DailyRemaining,
    string? MinuteRemaining,
    IReadOnlyList<ApiFootballCompetitionSummary> Rows);

public sealed record ApiFootballCompetitionSummary(
    int LeagueId,
    string League,
    string Country,
    int Season,
    int FinishedFixtures,
    DateOnly FirstMatchDate,
    DateOnly LastMatchDate);

public sealed record ApiFootballBulkSyncRequest(
    DateOnly DateFrom,
    DateOnly DateTo,
    int CompetitionOffset = 0,
    int MaxCompetitions = 250,
    int MaxFixturesPerCompetition = 1000,
    int MaxTotalFixtures = 5000,
    int MinimumDailyRemaining = 400,
    bool DryRun = true,
    bool UpdateExisting = true,
    bool SyncStandings = true,
    bool SyncLineups = false,
    bool SeniorMenOnly = true);

public sealed record ApiFootballBulkSyncResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    bool DryRun,
    int DiscoveredFixtures,
    int DiscoveredCompetitions,
    int EligibleCompetitions,
    int ProcessedCompetitions,
    int ProcessedFixtures,
    int Inserted,
    int Updated,
    int Skipped,
    int Errors,
    bool StoppedByQuota,
    string? DailyRemaining,
    string? MinuteRemaining,
    IReadOnlyList<ApiFootballBulkSyncRow> Rows);

public sealed record ApiFootballBulkSyncRow(
    int LeagueId,
    string League,
    string Country,
    int Season,
    int AvailableFixtures,
    int RequestedFixtures,
    int ProcessedFixtures,
    int Inserted,
    int Updated,
    int Skipped,
    int Errors,
    string Status,
    string? Message = null);

public sealed record ApiFootballHistoricalBatchRequest(
    DateOnly? Month = null,
    int? CompetitionOffset = null,
    int MaxCompetitions = 500,
    int MaxFixturesPerCompetition = 1000,
    int MaxTotalFixtures = 7000,
    int MinimumDailyRemaining = 5);

public sealed record ApiFootballHistoricalBatchState(
    string Status,
    bool IsRunning,
    DateOnly Month,
    int CompetitionOffset,
    DateOnly NextMonth,
    int NextCompetitionOffset,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int? DiscoveredFixtures,
    int? EligibleCompetitions,
    int? ProcessedCompetitions,
    int? ProcessedFixtures,
    int? Inserted,
    int? Updated,
    int? Skipped,
    int? Errors,
    bool? StoppedByQuota,
    string? DailyRemaining,
    string? MinuteRemaining,
    string Message);

public sealed record ApiFootballStatusResult(
    string Plan,
    int RequestsCurrent,
    int RequestsLimit,
    string? DailyRemaining,
    string? MinuteRemaining);

public sealed record ApiFootballDatabaseAudit(
    int MatchHistoryRows,
    int ApiFootballRows,
    int ApiFootballWomenRows,
    int ApiFootballTeams,
    int LeagueSeasons,
    int StandingSnapshots,
    int SyncRuns,
    IReadOnlyList<string> ExtendedColumns,
    IReadOnlyList<string> KnownLeagues,
    IReadOnlyList<string> UpcomingLeagues);

public sealed record ApiFootballSyncResult(
    Guid SyncRunId,
    int LeagueId,
    string League,
    string DbLeague,
    int Season,
    bool DryRun,
    bool FixtureStatisticsCovered,
    int Discovered,
    int Processed,
    int Inserted,
    int Updated,
    int Skipped,
    int Errors,
    string? DailyRemaining,
    string? MinuteRemaining,
    IReadOnlyList<ApiFootballSyncRow> Rows);

public sealed record ApiFootballSyncRow(
    long FixtureId,
    DateOnly MatchDate,
    string HomeTeam,
    string AwayTeam,
    string Status,
    string Message,
    long? MatchHistoryId = null);

internal sealed record ApiFootballLeagueSeason(
    int LeagueId,
    string LeagueName,
    string Country,
    string CompetitionType,
    int Season,
    bool IsCurrent,
    bool Events,
    bool Lineups,
    bool FixtureStatistics,
    bool PlayerStatistics,
    bool Standings,
    bool Predictions,
    bool Odds);

internal sealed record ApiFootballFixture(
    long FixtureId,
    DateTimeOffset Date,
    string Status,
    string Round,
    string? Referee,
    string? VenueName,
    string? VenueCity,
    int LeagueId,
    string LeagueName,
    string Country,
    int Season,
    int HomeTeamId,
    string HomeTeam,
    string? HomeLogo,
    int AwayTeamId,
    string AwayTeam,
    string? AwayLogo,
    int? HomeGoals,
    int? AwayGoals,
    int? HomeHalfTimeGoals,
    int? AwayHalfTimeGoals);

internal sealed class ApiFootballMatchData
{
    public required ApiFootballFixture Fixture { get; init; }
    public string? HomeFormation { get; set; }
    public string? AwayFormation { get; set; }
    public int? HomeCorners { get; set; }
    public int? AwayCorners { get; set; }
    public int? HomeShots { get; set; }
    public int? AwayShots { get; set; }
    public int? HomeShotsOnGoal { get; set; }
    public int? AwayShotsOnGoal { get; set; }
    public decimal? HomePossession { get; set; }
    public decimal? AwayPossession { get; set; }
    public int? HomeFouls { get; set; }
    public int? AwayFouls { get; set; }
    public int? HomeOffsides { get; set; }
    public int? AwayOffsides { get; set; }
    public int? HomeYellowCards { get; set; }
    public int? AwayYellowCards { get; set; }
    public int? HomeRedCards { get; set; }
    public int? AwayRedCards { get; set; }
    public int? HomeTotalPasses { get; set; }
    public int? AwayTotalPasses { get; set; }
    public decimal? HomePassAccuracy { get; set; }
    public decimal? AwayPassAccuracy { get; set; }

    public bool HasRequiredStatistics =>
        Fixture.HomeGoals.HasValue && Fixture.AwayGoals.HasValue &&
        HomeCorners.HasValue && AwayCorners.HasValue &&
        HomeShots.HasValue && AwayShots.HasValue &&
        HomeShotsOnGoal.HasValue && AwayShotsOnGoal.HasValue &&
        HomePossession.HasValue && AwayPossession.HasValue;
}

internal sealed record ApiFootballStanding(
    string GroupName,
    int TeamId,
    string TeamName,
    int Rank,
    int? Points,
    int? GoalsDifference,
    int? Played,
    int? Won,
    int? Drawn,
    int? Lost,
    int? GoalsFor,
    int? GoalsAgainst,
    string? Form,
    string? Description);

internal sealed record ApiFootballPersistResult(string Action, long MatchHistoryId);

internal sealed record ApiFootballSettlementFixtureSyncResult(
    long FixtureId,
    long? MatchHistoryId,
    string Action,
    bool HasGoals,
    bool HasCorners,
    bool HasShots,
    bool HasShotsOnGoal,
    string Message);

internal sealed record ApiFootballHistoricalCheckpoint(
    DateTime Month,
    int CompetitionOffset,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int? DiscoveredFixtures,
    int? EligibleCompetitions,
    int? ProcessedCompetitions,
    int? ProcessedFixtures,
    int? Inserted,
    int? Updated,
    int? Skipped,
    int? Errors,
    bool? StoppedByQuota,
    string? DailyRemaining,
    string? MinuteRemaining,
    string? Message);
