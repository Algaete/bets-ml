SET NOCOUNT ON;

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
                api.Id DESC
        ),
        PairCount = COUNT_BIG(1) OVER (PARTITION BY donor.Id)
    FROM dbo.MatchHistory api
    INNER JOIN dbo.MatchHistory donor
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
)
SELECT
    CandidateRows = COUNT_BIG(1),
    AmbiguousCandidateRows = COALESCE(SUM(CASE WHEN PairCount > 1 THEN 1 ELSE 0 END), 0)
FROM CandidatePairs
WHERE PairRank = 1;
