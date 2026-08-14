/*
  Dataset temporal para scripts/train_bot_c_meta_model.py.
  Incluye TODOS los candidatos Bot C (Approved y Rejected) cuyo fixture ya terminó
  y cuya estadística del mercado está confirmada por API-Football en MatchHistory.
  No usa el resultado para construir features: FeatureSnapshotJson ya es inmutable
  y fue creado con MatchDate histórica estrictamente anterior al candidato.
*/
DECLARE @DateFrom DATE = NULL;
DECLARE @DateTo DATE = NULL;

WITH SettledCandidates AS
(
    SELECT
        e.AutomatedBotPickEvaluationId,
        MatchDateUtc = e.MatchDate,
        e.MarketType,
        e.SelectedSide,
        e.LineValue,
        e.SelectedOdds,
        e.FeatureSchemaVersion,
        e.FeatureSnapshotJson,
        e.Decision,
        ActualValue = CONVERT(DECIMAL(12,4), CASE e.MarketType
            WHEN N'TotalGoals' THEN mh.HomeGoals + mh.AwayGoals
            WHEN N'HomeTeamGoals' THEN mh.HomeGoals
            WHEN N'AwayTeamGoals' THEN mh.AwayGoals
            WHEN N'TotalCorners' THEN mh.HomeCorners + mh.AwayCorners
            WHEN N'HomeTeamCorners' THEN mh.HomeCorners
            WHEN N'AwayTeamCorners' THEN mh.AwayCorners
            WHEN N'TotalShots' THEN mh.HomeShots + mh.AwayShots
            WHEN N'HomeTeamShots' THEN mh.HomeShots
            WHEN N'AwayTeamShots' THEN mh.AwayShots
            WHEN N'TotalShotsOnGoal' THEN mh.HomeShotsOnGoal + mh.AwayShotsOnGoal
            WHEN N'HomeTeamShotsOnGoal' THEN mh.HomeShotsOnGoal
            WHEN N'AwayTeamShotsOnGoal' THEN mh.AwayShotsOnGoal
        END)
    FROM dbo.AutomatedBotPickEvaluations e
    INNER JOIN dbo.MatchHistory mh
        ON mh.ApiFootballFixtureId = e.ApiFootballFixtureId
    WHERE e.BotKey = N'C2026'
      AND e.Decision IN (N'Approved', N'Rejected')
      AND e.SelectedSide IN (N'Over', N'Under')
      AND e.SelectedOdds > 1
      AND UPPER(LTRIM(RTRIM(COALESCE(mh.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
      AND (@DateFrom IS NULL OR e.MatchDate >= @DateFrom)
      AND (@DateTo IS NULL OR e.MatchDate < DATEADD(DAY, 1, @DateTo))
      AND
      (
          (e.MarketType IN (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals')
              AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 1)
          OR (e.MarketType IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')
              AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 1)
          OR (e.MarketType IN (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots')
              AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 1)
          OR (e.MarketType IN (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal')
              AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 1)
      )
)
SELECT
    AutomatedBotPickEvaluationId,
    MatchDateUtc,
    MarketType,
    SelectedSide,
    LineValue,
    SelectedOdds,
    ActualValue,
    FeatureSchemaVersion,
    FeatureSnapshotJson,
    OriginalDecision = Decision
FROM SettledCandidates
WHERE ActualValue IS NOT NULL
ORDER BY MatchDateUtc, AutomatedBotPickEvaluationId;
