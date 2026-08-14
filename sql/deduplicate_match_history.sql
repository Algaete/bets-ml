SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

;WITH RankedMatches AS
(
    SELECT
        Id,
        KeeperId = FIRST_VALUE(Id) OVER
        (
            PARTITION BY
                MatchDate,
                dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)),
                dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam)),
                ISNULL(NULLIF(HomeTeamGender, N''), N'M'),
                ISNULL(NULLIF(AwayTeamGender, N''), N'M')
            ORDER BY
                CASE WHEN ApiFootballFixtureId IS NOT NULL THEN 0 ELSE 1 END,
                CASE WHEN HomeCorners IS NOT NULL AND AwayCorners IS NOT NULL THEN 0 ELSE 1 END,
                CASE
                    WHEN HomeShots IS NOT NULL AND AwayShots IS NOT NULL
                     AND HomeShotsOnGoal IS NOT NULL AND AwayShotsOnGoal IS NOT NULL
                     AND HomePossession IS NOT NULL AND AwayPossession IS NOT NULL THEN 0
                    ELSE 1
                END,
                COALESCE(UpdatedAtUtc, CreatedAtUtc) DESC,
                Id DESC
        ),
        DuplicateRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                MatchDate,
                dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)),
                dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam)),
                ISNULL(NULLIF(HomeTeamGender, N''), N'M'),
                ISNULL(NULLIF(AwayTeamGender, N''), N'M')
            ORDER BY
                CASE WHEN ApiFootballFixtureId IS NOT NULL THEN 0 ELSE 1 END,
                CASE WHEN HomeCorners IS NOT NULL AND AwayCorners IS NOT NULL THEN 0 ELSE 1 END,
                CASE
                    WHEN HomeShots IS NOT NULL AND AwayShots IS NOT NULL
                     AND HomeShotsOnGoal IS NOT NULL AND AwayShotsOnGoal IS NOT NULL
                     AND HomePossession IS NOT NULL AND AwayPossession IS NOT NULL THEN 0
                    ELSE 1
                END,
                COALESCE(UpdatedAtUtc, CreatedAtUtc) DESC,
                Id DESC
        )
    FROM dbo.MatchHistory WITH (UPDLOCK, HOLDLOCK)
)
SELECT Id AS DuplicateId, KeeperId
INTO #DuplicateMap
FROM RankedMatches
WHERE DuplicateRank > 1;

IF OBJECT_ID(N'dbo.MatchHistoryDuplicateArchive_20260723', N'U') IS NOT NULL
BEGIN
    THROW 50031, 'The duplicate archive table already exists; cleanup was not executed.', 1;
END;

SELECT
    ArchivedAtUtc = SYSUTCDATETIME(),
    mapping.KeeperId,
    duplicate.*
INTO dbo.MatchHistoryDuplicateArchive_20260723
FROM #DuplicateMap mapping
INNER JOIN dbo.MatchHistory duplicate ON duplicate.Id = mapping.DuplicateId;

;WITH DonorValues AS
(
    SELECT
        mapping.KeeperId,
        HomeFormation = MAX(duplicate.HomeFormation),
        AwayFormation = MAX(duplicate.AwayFormation),
        HomeGoals = MAX(duplicate.HomeGoals),
        AwayGoals = MAX(duplicate.AwayGoals),
        HomeCorners = MAX(duplicate.HomeCorners),
        AwayCorners = MAX(duplicate.AwayCorners),
        HomeShots = MAX(duplicate.HomeShots),
        AwayShots = MAX(duplicate.AwayShots),
        HomeShotsOnGoal = MAX(duplicate.HomeShotsOnGoal),
        AwayShotsOnGoal = MAX(duplicate.AwayShotsOnGoal),
        HomePossession = MAX(duplicate.HomePossession),
        AwayPossession = MAX(duplicate.AwayPossession),
        HomeTeamPosition = MAX(duplicate.HomeTeamPosition),
        AwayTeamPosition = MAX(duplicate.AwayTeamPosition),
        TotalTeams = MAX(duplicate.TotalTeams)
    FROM #DuplicateMap mapping
    INNER JOIN dbo.MatchHistory duplicate ON duplicate.Id = mapping.DuplicateId
    GROUP BY mapping.KeeperId
)
UPDATE keeper
SET
    HomeFormation = COALESCE(keeper.HomeFormation, donor.HomeFormation),
    AwayFormation = COALESCE(keeper.AwayFormation, donor.AwayFormation),
    HomeGoals = COALESCE(keeper.HomeGoals, donor.HomeGoals),
    AwayGoals = COALESCE(keeper.AwayGoals, donor.AwayGoals),
    HomeCorners = COALESCE(keeper.HomeCorners, donor.HomeCorners),
    AwayCorners = COALESCE(keeper.AwayCorners, donor.AwayCorners),
    HomeShots = COALESCE(keeper.HomeShots, donor.HomeShots),
    AwayShots = COALESCE(keeper.AwayShots, donor.AwayShots),
    HomeShotsOnGoal = COALESCE(keeper.HomeShotsOnGoal, donor.HomeShotsOnGoal),
    AwayShotsOnGoal = COALESCE(keeper.AwayShotsOnGoal, donor.AwayShotsOnGoal),
    HomePossession = COALESCE(keeper.HomePossession, donor.HomePossession),
    AwayPossession = COALESCE(keeper.AwayPossession, donor.AwayPossession),
    HomeTeamPosition = COALESCE(keeper.HomeTeamPosition, donor.HomeTeamPosition),
    AwayTeamPosition = COALESCE(keeper.AwayTeamPosition, donor.AwayTeamPosition),
    TotalTeams = COALESCE(keeper.TotalTeams, donor.TotalTeams),
    UpdatedAtUtc = SYSUTCDATETIME()
FROM dbo.MatchHistory keeper
INNER JOIN DonorValues donor ON donor.KeeperId = keeper.Id;

DELETE duplicate
FROM dbo.MatchHistory duplicate
INNER JOIN #DuplicateMap mapping ON mapping.DuplicateId = duplicate.Id;

DECLARE @RemovedRows INT = @@ROWCOUNT;

IF EXISTS
(
    SELECT 1
    FROM dbo.MatchHistory
    GROUP BY
        MatchDate,
        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)),
        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam)),
        ISNULL(NULLIF(HomeTeamGender, N''), N'M'),
        ISNULL(NULLIF(AwayTeamGender, N''), N'M')
    HAVING COUNT_BIG(1) > 1
)
BEGIN
    THROW 50030, 'Canonical MatchHistory duplicates remain after cleanup.', 1;
END;

COMMIT TRANSACTION;

SELECT RemovedRows = @RemovedRows;
