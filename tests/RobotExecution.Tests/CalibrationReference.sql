WITH EligibleEvaluations AS
(
    SELECT
        e.AutomatedBotPickEvaluationId,
        e.ApiFootballFixtureId,
        e.PublishedSelectionId,
        e.MatchDate,
        ExpectedUtcDate = CAST(
            e.MatchDate AT TIME ZONE 'Pacific SA Standard Time' AT TIME ZONE 'UTC'
            AS DATE),
        e.HomeTeam,
        e.AwayTeam,
        e.MarketType,
        e.SelectedSide,
        e.LineValue,
        e.SelectedOdds,
        e.BaseCalibratedProbability,
        e.MarketNoVigProbability,
        e.DataQualityScore,
        e.FeatureSnapshotJson,
        e.BaseModelTrainedThroughUtc,
        BaseModelVersion = COALESCE(NULLIF(e.BaseModelVersion, N''), N'unknown')
    FROM dbo.AutomatedBotPickEvaluations e
    WHERE e.BotKey = @SourceBotKey
      AND e.MatchDate < @AsOfDateUtc
      AND e.Decision IN (N'Approved', N'Rejected')
      AND e.SelectedSide IN (N'Over', N'Under')
      AND e.SelectedOdds > 1
      AND e.MarketNoVigProbability > 0 AND e.MarketNoVigProbability < 1
      AND e.DataQualityScore BETWEEN 0 AND 1
      AND e.BaseModelTrainedThroughUtc IS NOT NULL
      AND CAST(
            e.MatchDate AT TIME ZONE 'Pacific SA Standard Time' AT TIME ZONE 'UTC'
            AS DATETIME2) > e.BaseModelTrainedThroughUtc
),
CandidateMatchesRaw AS
(
    SELECT
        e.AutomatedBotPickEvaluationId,
        MatchHistoryId = CONVERT(BIGINT, mh.Id),
        mh.ApiFootballFixtureId,
        LinkPriority = 0,
        DateDistanceDays = ABS(DATEDIFF(DAY, e.ExpectedUtcDate, mh.MatchDate))
    FROM EligibleEvaluations e
    INNER JOIN dbo.AutomatedCornerBetSelections s
        ON s.AutomatedCornerBetSelectionId = e.PublishedSelectionId
    INNER JOIN dbo.MatchHistory mh
        ON mh.Id = s.MatchHistoryId
    WHERE e.PublishedSelectionId IS NOT NULL
      AND s.MatchHistoryId IS NOT NULL

    UNION ALL

    SELECT
        e.AutomatedBotPickEvaluationId,
        MatchHistoryId = CONVERT(BIGINT, mh.Id),
        mh.ApiFootballFixtureId,
        LinkPriority = 1,
        DateDistanceDays = ABS(DATEDIFF(DAY, e.ExpectedUtcDate, mh.MatchDate))
    FROM EligibleEvaluations e
    INNER JOIN dbo.MatchHistory mh
        ON mh.ApiFootballFixtureId = e.ApiFootballFixtureId
    WHERE e.ApiFootballFixtureId IS NOT NULL

    UNION ALL

    SELECT
        e.AutomatedBotPickEvaluationId,
        MatchHistoryId = CONVERT(BIGINT, mh.Id),
        mh.ApiFootballFixtureId,
        LinkPriority = 2,
        DateDistanceDays = ABS(DATEDIFF(DAY, e.ExpectedUtcDate, mh.MatchDate))
    FROM EligibleEvaluations e
    INNER JOIN dbo.MatchHistory mh
        ON mh.MatchDate BETWEEN DATEADD(DAY, -1, e.ExpectedUtcDate)
                            AND DATEADD(DAY, 1, e.ExpectedUtcDate)
       AND COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam)
            COLLATE Latin1_General_100_CI_AI = e.HomeTeam COLLATE Latin1_General_100_CI_AI
       AND COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam)
            COLLATE Latin1_General_100_CI_AI = e.AwayTeam COLLATE Latin1_General_100_CI_AI
    WHERE e.PublishedSelectionId IS NULL
      AND e.ApiFootballFixtureId IS NULL
),
CandidateMatches AS
(
    SELECT
        AutomatedBotPickEvaluationId,
        MatchHistoryId,
        ApiFootballFixtureId = MAX(ApiFootballFixtureId),
        LinkPriority = MIN(LinkPriority),
        DateDistanceDays = MIN(DateDistanceDays)
    FROM CandidateMatchesRaw
    GROUP BY AutomatedBotPickEvaluationId, MatchHistoryId
),
RankedCandidateMatches AS
(
    SELECT
        candidate.*,
        CandidateRank = DENSE_RANK() OVER
        (
            PARTITION BY candidate.AutomatedBotPickEvaluationId
            ORDER BY candidate.LinkPriority, candidate.DateDistanceDays,
                CASE WHEN candidate.ApiFootballFixtureId IS NULL THEN 1 ELSE 0 END
        )
    FROM CandidateMatches candidate
),
MatchedEvaluations AS
(
    SELECT
        AutomatedBotPickEvaluationId,
        MatchCandidateCount = SUM(CASE WHEN CandidateRank = 1 THEN 1 ELSE 0 END),
        MatchHistoryId = MAX(CASE WHEN CandidateRank = 1 THEN MatchHistoryId END)
    FROM RankedCandidateMatches
    GROUP BY AutomatedBotPickEvaluationId
)
SELECT
    EvaluationId = e.AutomatedBotPickEvaluationId,
    FixtureId = mh.ApiFootballFixtureId,
    MatchDateUtc = CAST(
        e.MatchDate AT TIME ZONE 'Pacific SA Standard Time' AT TIME ZONE 'UTC'
        AS DATETIME2),
    e.MarketType,
    e.SelectedSide,
    LineValue = e.LineValue,
    Odds = e.SelectedOdds,
    ActualValue = CONVERT(INT, CASE e.MarketType
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
    END),
    e.BaseCalibratedProbability,
    e.MarketNoVigProbability,
    e.DataQualityScore,
    e.FeatureSnapshotJson,
    e.BaseModelVersion
FROM EligibleEvaluations e
INNER JOIN MatchedEvaluations matched
    ON matched.AutomatedBotPickEvaluationId = e.AutomatedBotPickEvaluationId
   AND matched.MatchCandidateCount = 1
INNER JOIN dbo.MatchHistory mh
    ON mh.Id = matched.MatchHistoryId
WHERE mh.ApiFootballFixtureId IS NOT NULL
  AND UPPER(LTRIM(RTRIM(COALESCE(mh.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
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
  AND CASE e.MarketType
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
      END IS NOT NULL
ORDER BY e.MatchDate, e.AutomatedBotPickEvaluationId
OPTION (RECOMPILE);
