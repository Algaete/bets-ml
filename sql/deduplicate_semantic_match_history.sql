SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

;WITH CandidatePairs AS
(
    SELECT
        DuplicateId = donor.Id,
        KeeperId = api.Id,
        PairRank = ROW_NUMBER() OVER
        (
            PARTITION BY donor.Id
            ORDER BY
                CASE
                    WHEN dbo.fn_CanonicalTeamName(COALESCE(NULLIF(api.StandardizedHomeTeam, N''), api.HomeTeam))
                       = dbo.fn_CanonicalTeamName(COALESCE(NULLIF(donor.StandardizedHomeTeam, N''), donor.HomeTeam))
                     AND dbo.fn_CanonicalTeamName(COALESCE(NULLIF(api.StandardizedAwayTeam, N''), api.AwayTeam))
                       = dbo.fn_CanonicalTeamName(COALESCE(NULLIF(donor.StandardizedAwayTeam, N''), donor.AwayTeam))
                    THEN 0
                    ELSE 1
                END,
                COALESCE(api.UpdatedAtUtc, api.CreatedAtUtc) DESC,
                api.Id DESC
        )
    FROM dbo.MatchHistory api WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.MatchHistory donor WITH (UPDLOCK, HOLDLOCK)
        ON donor.ApiFootballFixtureId IS NULL
       AND donor.Id <> api.Id
       AND donor.MatchDate = api.MatchDate
       AND ISNULL(NULLIF(donor.HomeTeamGender, N''), N'M') =
           ISNULL(NULLIF(api.HomeTeamGender, N''), N'M')
       AND ISNULL(NULLIF(donor.AwayTeamGender, N''), N'M') =
           ISNULL(NULLIF(api.AwayTeamGender, N''), N'M')
       AND donor.HomeGoals = api.HomeGoals
       AND donor.AwayGoals = api.AwayGoals
       AND donor.HomeCorners = api.HomeCorners
       AND donor.AwayCorners = api.AwayCorners
       AND
       (
           dbo.fn_CanonicalTeamName(COALESCE(NULLIF(donor.StandardizedHomeTeam, N''), donor.HomeTeam))
               COLLATE Latin1_General_100_CI_AI =
           dbo.fn_CanonicalTeamName(COALESCE(NULLIF(api.StandardizedHomeTeam, N''), api.HomeTeam))
               COLLATE Latin1_General_100_CI_AI
           OR dbo.fn_CanonicalTeamName(COALESCE(NULLIF(donor.StandardizedAwayTeam, N''), donor.AwayTeam))
               COLLATE Latin1_General_100_CI_AI =
           dbo.fn_CanonicalTeamName(COALESCE(NULLIF(api.StandardizedAwayTeam, N''), api.AwayTeam))
               COLLATE Latin1_General_100_CI_AI
       )
    WHERE api.ApiFootballFixtureId IS NOT NULL
      AND api.HomeGoals IS NOT NULL
      AND api.AwayGoals IS NOT NULL
      AND api.HomeCorners IS NOT NULL
      AND api.AwayCorners IS NOT NULL
),
SelectedPairs AS
(
    SELECT DuplicateId, KeeperId
    FROM CandidatePairs
    WHERE PairRank = 1
)
SELECT DuplicateId, KeeperId
INTO #SemanticDuplicateMap
FROM SelectedPairs;

IF OBJECT_ID(N'dbo.MatchHistorySemanticDuplicateArchive_20260723', N'U') IS NOT NULL
BEGIN
    THROW 50032, 'The semantic duplicate archive table already exists; cleanup was not executed.', 1;
END;

SELECT
    ArchivedAtUtc = SYSUTCDATETIME(),
    mapping.KeeperId,
    duplicate.*
INTO dbo.MatchHistorySemanticDuplicateArchive_20260723
FROM #SemanticDuplicateMap mapping
INNER JOIN dbo.MatchHistory duplicate ON duplicate.Id = mapping.DuplicateId;

;WITH DonorValues AS
(
    SELECT
        mapping.KeeperId,
        HomeFormation = MAX(duplicate.HomeFormation),
        AwayFormation = MAX(duplicate.AwayFormation),
        HomeShots = MAX(duplicate.HomeShots),
        AwayShots = MAX(duplicate.AwayShots),
        HomeShotsOnGoal = MAX(duplicate.HomeShotsOnGoal),
        AwayShotsOnGoal = MAX(duplicate.AwayShotsOnGoal),
        HomePossession = MAX(duplicate.HomePossession),
        AwayPossession = MAX(duplicate.AwayPossession),
        HomeTeamPosition = MAX(duplicate.HomeTeamPosition),
        AwayTeamPosition = MAX(duplicate.AwayTeamPosition),
        TotalTeams = MAX(duplicate.TotalTeams)
    FROM #SemanticDuplicateMap mapping
    INNER JOIN dbo.MatchHistory duplicate ON duplicate.Id = mapping.DuplicateId
    GROUP BY mapping.KeeperId
)
UPDATE keeper
SET
    HomeFormation = COALESCE(keeper.HomeFormation, donor.HomeFormation),
    AwayFormation = COALESCE(keeper.AwayFormation, donor.AwayFormation),
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
INNER JOIN #SemanticDuplicateMap mapping ON mapping.DuplicateId = duplicate.Id;

DECLARE @RemovedRows INT = @@ROWCOUNT;

COMMIT TRANSACTION;

SELECT
    RemovedRows = @RemovedRows,
    ArchivedRows =
    (
        SELECT COUNT_BIG(1)
        FROM dbo.MatchHistorySemanticDuplicateArchive_20260723
    );
