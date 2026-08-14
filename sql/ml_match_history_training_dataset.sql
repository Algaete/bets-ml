/*
    Dataset temporal para modelos prepartido.

    Reglas:
    - Una fila por partido, sin duplicados semanticos.
    - Las features rolling usan exclusivamente partidos anteriores.
    - Los datos del partido actual se exponen solo como targets.
    - No se usan LeagueStandingSnapshots: sus snapshots historicos pueden contener
      la posicion final de la temporada y provocar data leakage.
*/
SET NOCOUNT ON;

DECLARE @DateFrom DATE = CONVERT(DATE, '2018-01-01');
DECLARE @DateTo DATE = CONVERT(DATE, '2099-12-31');
DECLARE @MinimumGeneralHistory INT = 5;
DECLARE @OnlyApiFootball BIT = 0;

DROP TABLE IF EXISTS #CandidateMatches;
DROP TABLE IF EXISTS #DeduplicatedMatches;
DROP TABLE IF EXISTS #TeamMatchRows;

-- Standardized names are maintained by the ingestion pipeline. Reading them
-- directly avoids expensive scalar canonicalization on every historical row.
SELECT
    mh.Id,
    MatchDate = CONVERT(DATE, mh.MatchDate),
    CanonicalLeague = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedLeague)), ''), mh.League),
    CanonicalHomeTeam = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedHomeTeam)), ''), mh.HomeTeam),
    CanonicalAwayTeam = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedAwayTeam)), ''), mh.AwayTeam),
    mh.Season,
    mh.DataSource,
    mh.ApiFootballFixtureId,
    mh.IsKnockout,
    mh.HomeFormation,
    mh.AwayFormation,
    mh.HomeTeamPosition,
    mh.AwayTeamPosition,
    mh.HomeCorners,
    mh.AwayCorners,
    mh.HomeShots,
    mh.AwayShots,
    mh.HomeShotsOnGoal,
    mh.AwayShotsOnGoal,
    mh.HomePossession,
    mh.AwayPossession,
    mh.HomeGoals,
    mh.AwayGoals,
    SourcePriority = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL THEN 0 ELSE 1 END,
    CompletenessPriority = CASE
        WHEN mh.HomeShots IS NOT NULL AND mh.AwayShots IS NOT NULL
         AND mh.HomeShotsOnGoal IS NOT NULL AND mh.AwayShotsOnGoal IS NOT NULL
         AND mh.HomePossession IS NOT NULL AND mh.AwayPossession IS NOT NULL THEN 0
        ELSE 1
    END,
    EffectiveUpdatedAtUtc = COALESCE(mh.UpdatedAtUtc, mh.CreatedAtUtc)
INTO #CandidateMatches
FROM dbo.MatchHistory AS mh
WHERE mh.HomeCorners BETWEEN 0 AND 30
  AND mh.AwayCorners BETWEEN 0 AND 30
  AND mh.HomeCorners + mh.AwayCorners > 0
  AND ISNULL(NULLIF(mh.HomeTeamGender, ''), 'M') = 'M'
  AND ISNULL(NULLIF(mh.AwayTeamGender, ''), 'M') = 'M'
  AND (mh.FixtureStatus IS NULL OR mh.FixtureStatus IN ('FT', 'AET', 'PEN'))
  AND (@OnlyApiFootball = 0 OR mh.DataSource = 'API-Football')
  AND mh.MatchDate < DATEADD(DAY, 1, @DateTo)
OPTION (RECOMPILE);

CREATE CLUSTERED INDEX IX_CandidateMatches_Dedup
    ON #CandidateMatches
    (
        MatchDate,
        CanonicalHomeTeam,
        CanonicalAwayTeam,
        SourcePriority,
        CompletenessPriority,
        EffectiveUpdatedAtUtc DESC,
        Id DESC
    );

;WITH RankedMatches AS
(
    SELECT
        c.*,
        DuplicateRank = ROW_NUMBER() OVER
        (
            PARTITION BY c.MatchDate, c.CanonicalHomeTeam, c.CanonicalAwayTeam
            ORDER BY
                c.SourcePriority,
                c.CompletenessPriority,
                c.EffectiveUpdatedAtUtc DESC,
                c.Id DESC
        )
    FROM #CandidateMatches AS c
)
SELECT *
INTO #DeduplicatedMatches
FROM RankedMatches
WHERE DuplicateRank = 1;

CREATE UNIQUE CLUSTERED INDEX IX_DeduplicatedMatches_Id
    ON #DeduplicatedMatches(Id);

-- Create one chronological stream per team for the rolling pre-match features.
SELECT
    MatchHistoryId = m.Id,
    m.MatchDate,
    Team = m.CanonicalHomeTeam,
    IsHome = CONVERT(BIT, 1),
    CornersFor = m.HomeCorners,
    CornersAgainst = m.AwayCorners,
    ShotsFor = m.HomeShots,
    ShotsAgainst = m.AwayShots,
    ShotsOnGoalFor = m.HomeShotsOnGoal,
    ShotsOnGoalAgainst = m.AwayShotsOnGoal,
    PossessionFor = m.HomePossession,
    GoalsFor = m.HomeGoals,
    GoalsAgainst = m.AwayGoals
INTO #TeamMatchRows
FROM #DeduplicatedMatches AS m
UNION ALL
SELECT
    m.Id,
    m.MatchDate,
    m.CanonicalAwayTeam,
    CONVERT(BIT, 0),
    m.AwayCorners,
    m.HomeCorners,
    m.AwayShots,
    m.HomeShots,
    m.AwayShotsOnGoal,
    m.HomeShotsOnGoal,
    m.AwayPossession,
    m.AwayGoals,
    m.HomeGoals
FROM #DeduplicatedMatches AS m;

CREATE CLUSTERED INDEX IX_TeamMatchRows_Rolling
    ON #TeamMatchRows(Team, MatchDate, MatchHistoryId, IsHome);

CREATE INDEX IX_TeamMatchRows_VenueRolling
    ON #TeamMatchRows(Team, IsHome, MatchDate, MatchHistoryId)
    INCLUDE (CornersFor, CornersAgainst);

SELECT
    MatchHistoryId = m.Id,
    MatchDate = CAST(m.MatchDate AS DATE),
    League = m.CanonicalLeague,
    m.Season,
    HomeTeam = m.CanonicalHomeTeam,
    AwayTeam = m.CanonicalAwayTeam,
    m.DataSource,
    m.ApiFootballFixtureId,
    MatchYear = YEAR(m.MatchDate),
    MatchMonth = MONTH(m.MatchDate),
    DayOfWeekMonday1 = 1 + DATEDIFF(DAY, '19000101', CAST(m.MatchDate AS DATE)) % 7,
    IsKnockout = CONVERT(INT, ISNULL(m.IsKnockout, 0)),
    m.HomeFormation,
    m.AwayFormation,
    m.HomeTeamPosition,
    m.AwayTeamPosition,
    PositionDifference = CASE
        WHEN m.HomeTeamPosition IS NOT NULL AND m.AwayTeamPosition IS NOT NULL
        THEN m.AwayTeamPosition - m.HomeTeamPosition
    END,
    HomePositionMissing = CONVERT(INT, CASE WHEN m.HomeTeamPosition IS NULL THEN 1 ELSE 0 END),
    AwayPositionMissing = CONVERT(INT, CASE WHEN m.AwayTeamPosition IS NULL THEN 1 ELSE 0 END),
    HomeHistoryMatches10 = homeGeneral.HistoryMatches10,
    HomeAvgCornersFor10 = homeGeneral.AvgCornersFor10,
    HomeAvgCornersAgainst10 = homeGeneral.AvgCornersAgainst10,
    HomeAvgShotsFor10 = homeGeneral.AvgShotsFor10,
    HomeAvgShotsAgainst10 = homeGeneral.AvgShotsAgainst10,
    HomeAvgShotsOnGoalFor10 = homeGeneral.AvgShotsOnGoalFor10,
    HomeAvgShotsOnGoalAgainst10 = homeGeneral.AvgShotsOnGoalAgainst10,
    HomeAvgPossession10 = homeGeneral.AvgPossession10,
    HomeAvgGoalsFor10 = homeGeneral.AvgGoalsFor10,
    HomeAvgGoalsAgainst10 = homeGeneral.AvgGoalsAgainst10,
    HomeVenueMatches10 = homeVenue.VenueMatches10,
    HomeAvgHomeCornersFor10 = homeVenue.AvgVenueCornersFor10,
    HomeAvgHomeCornersAgainst10 = homeVenue.AvgVenueCornersAgainst10,
    HomeDaysRest = DATEDIFF(DAY, homeGeneral.PreviousMatchDate, m.MatchDate),
    AwayHistoryMatches10 = awayGeneral.HistoryMatches10,
    AwayAvgCornersFor10 = awayGeneral.AvgCornersFor10,
    AwayAvgCornersAgainst10 = awayGeneral.AvgCornersAgainst10,
    AwayAvgShotsFor10 = awayGeneral.AvgShotsFor10,
    AwayAvgShotsAgainst10 = awayGeneral.AvgShotsAgainst10,
    AwayAvgShotsOnGoalFor10 = awayGeneral.AvgShotsOnGoalFor10,
    AwayAvgShotsOnGoalAgainst10 = awayGeneral.AvgShotsOnGoalAgainst10,
    AwayAvgPossession10 = awayGeneral.AvgPossession10,
    AwayAvgGoalsFor10 = awayGeneral.AvgGoalsFor10,
    AwayAvgGoalsAgainst10 = awayGeneral.AvgGoalsAgainst10,
    AwayVenueMatches10 = awayVenue.VenueMatches10,
    AwayAvgAwayCornersFor10 = awayVenue.AvgVenueCornersFor10,
    AwayAvgAwayCornersAgainst10 = awayVenue.AvgVenueCornersAgainst10,
    AwayDaysRest = DATEDIFF(DAY, awayGeneral.PreviousMatchDate, m.MatchDate),
    m.HomeCorners AS TargetHomeCorners,
    m.AwayCorners AS TargetAwayCorners,
    m.HomeCorners + m.AwayCorners AS TargetTotalCorners,
    CONVERT(INT, CASE WHEN m.HomeCorners + m.AwayCorners >= 8 THEN 1 ELSE 0 END) AS TargetTotalCornersOver75,
    CONVERT(INT, CASE WHEN m.HomeCorners + m.AwayCorners >= 9 THEN 1 ELSE 0 END) AS TargetTotalCornersOver85,
    CONVERT(INT, CASE WHEN m.HomeCorners + m.AwayCorners >= 10 THEN 1 ELSE 0 END) AS TargetTotalCornersOver95,
    CONVERT(INT, CASE WHEN m.HomeCorners + m.AwayCorners >= 11 THEN 1 ELSE 0 END) AS TargetTotalCornersOver105,
    CONVERT(INT, CASE WHEN m.HomeCorners >= 4 THEN 1 ELSE 0 END) AS TargetHomeCornersOver35,
    CONVERT(INT, CASE WHEN m.HomeCorners >= 5 THEN 1 ELSE 0 END) AS TargetHomeCornersOver45,
    CONVERT(INT, CASE WHEN m.AwayCorners >= 4 THEN 1 ELSE 0 END) AS TargetAwayCornersOver35,
    CONVERT(INT, CASE WHEN m.AwayCorners >= 5 THEN 1 ELSE 0 END) AS TargetAwayCornersOver45,
    m.HomeShots AS TargetHomeShots,
    m.AwayShots AS TargetAwayShots,
    m.HomeShots + m.AwayShots AS TargetTotalShots,
    m.HomeShotsOnGoal AS TargetHomeShotsOnGoal,
    m.AwayShotsOnGoal AS TargetAwayShotsOnGoal,
    m.HomeShotsOnGoal + m.AwayShotsOnGoal AS TargetTotalShotsOnGoal,
    m.HomeGoals AS TargetHomeGoals,
    m.AwayGoals AS TargetAwayGoals,
    m.HomeGoals + m.AwayGoals AS TargetTotalGoals,
    CASE
        WHEN m.HomeGoals IS NULL OR m.AwayGoals IS NULL THEN NULL
        WHEN m.HomeGoals > 0 AND m.AwayGoals > 0 THEN 1
        ELSE 0
    END AS TargetBothTeamsScore
FROM #DeduplicatedMatches AS m
CROSS APPLY
(
    SELECT
        HistoryMatches10 = COUNT_BIG(*),
        AvgCornersFor10 = AVG(CONVERT(FLOAT, history.CornersFor)),
        AvgCornersAgainst10 = AVG(CONVERT(FLOAT, history.CornersAgainst)),
        AvgShotsFor10 = AVG(CONVERT(FLOAT, history.ShotsFor)),
        AvgShotsAgainst10 = AVG(CONVERT(FLOAT, history.ShotsAgainst)),
        AvgShotsOnGoalFor10 = AVG(CONVERT(FLOAT, history.ShotsOnGoalFor)),
        AvgShotsOnGoalAgainst10 = AVG(CONVERT(FLOAT, history.ShotsOnGoalAgainst)),
        AvgPossession10 = AVG(CONVERT(FLOAT, history.PossessionFor)),
        AvgGoalsFor10 = AVG(CONVERT(FLOAT, history.GoalsFor)),
        AvgGoalsAgainst10 = AVG(CONVERT(FLOAT, history.GoalsAgainst)),
        PreviousMatchDate = MAX(history.MatchDate)
    FROM
    (
        SELECT TOP (10) t.*
        FROM #TeamMatchRows AS t
        WHERE t.Team = m.CanonicalHomeTeam
          AND t.MatchDate < m.MatchDate
        ORDER BY t.MatchDate DESC, t.MatchHistoryId DESC
    ) AS history
) AS homeGeneral
CROSS APPLY
(
    SELECT
        VenueMatches10 = COUNT_BIG(*),
        AvgVenueCornersFor10 = AVG(CONVERT(FLOAT, history.CornersFor)),
        AvgVenueCornersAgainst10 = AVG(CONVERT(FLOAT, history.CornersAgainst))
    FROM
    (
        SELECT TOP (10) t.CornersFor, t.CornersAgainst
        FROM #TeamMatchRows AS t
        WHERE t.Team = m.CanonicalHomeTeam
          AND t.IsHome = 1
          AND t.MatchDate < m.MatchDate
        ORDER BY t.MatchDate DESC, t.MatchHistoryId DESC
    ) AS history
) AS homeVenue
CROSS APPLY
(
    SELECT
        HistoryMatches10 = COUNT_BIG(*),
        AvgCornersFor10 = AVG(CONVERT(FLOAT, history.CornersFor)),
        AvgCornersAgainst10 = AVG(CONVERT(FLOAT, history.CornersAgainst)),
        AvgShotsFor10 = AVG(CONVERT(FLOAT, history.ShotsFor)),
        AvgShotsAgainst10 = AVG(CONVERT(FLOAT, history.ShotsAgainst)),
        AvgShotsOnGoalFor10 = AVG(CONVERT(FLOAT, history.ShotsOnGoalFor)),
        AvgShotsOnGoalAgainst10 = AVG(CONVERT(FLOAT, history.ShotsOnGoalAgainst)),
        AvgPossession10 = AVG(CONVERT(FLOAT, history.PossessionFor)),
        AvgGoalsFor10 = AVG(CONVERT(FLOAT, history.GoalsFor)),
        AvgGoalsAgainst10 = AVG(CONVERT(FLOAT, history.GoalsAgainst)),
        PreviousMatchDate = MAX(history.MatchDate)
    FROM
    (
        SELECT TOP (10) t.*
        FROM #TeamMatchRows AS t
        WHERE t.Team = m.CanonicalAwayTeam
          AND t.MatchDate < m.MatchDate
        ORDER BY t.MatchDate DESC, t.MatchHistoryId DESC
    ) AS history
) AS awayGeneral
CROSS APPLY
(
    SELECT
        VenueMatches10 = COUNT_BIG(*),
        AvgVenueCornersFor10 = AVG(CONVERT(FLOAT, history.CornersFor)),
        AvgVenueCornersAgainst10 = AVG(CONVERT(FLOAT, history.CornersAgainst))
    FROM
    (
        SELECT TOP (10) t.CornersFor, t.CornersAgainst
        FROM #TeamMatchRows AS t
        WHERE t.Team = m.CanonicalAwayTeam
          AND t.IsHome = 0
          AND t.MatchDate < m.MatchDate
        ORDER BY t.MatchDate DESC, t.MatchHistoryId DESC
    ) AS history
) AS awayVenue
WHERE m.MatchDate BETWEEN @DateFrom AND @DateTo
  AND homeGeneral.HistoryMatches10 >= @MinimumGeneralHistory
  AND awayGeneral.HistoryMatches10 >= @MinimumGeneralHistory
ORDER BY m.MatchDate, m.Id
OPTION (RECOMPILE);
