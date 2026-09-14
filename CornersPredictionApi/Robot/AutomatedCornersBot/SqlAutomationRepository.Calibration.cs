using System.Data;
using Microsoft.Data.SqlClient;

namespace AutomatedCornersBot.Api;

public sealed partial class SqlAutomationRepository
{
    private async Task PrepareCalibrationEvaluationCacheAsync(SqlConnection connection,
        string sourceBotKey, DateTime asOfDateUtc, CancellationToken cancellationToken,
        Func<int, Task>? reportPrepared)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = "CREATE TABLE #CalibrationProjectionMissing (EvaluationId BIGINT PRIMARY KEY);";
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = CalibrationProjectionMissingSql;
        command.Parameters.Add(new SqlParameter("@SourceBotKey", SqlDbType.NVarChar, 50)
            { Value = sourceBotKey.Trim().ToUpperInvariant() });
        command.Parameters.Add(new SqlParameter("@AsOfDateUtc", SqlDbType.DateTime2) { Value = asOfDateUtc });
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.Parameters.Clear();
        var prepared = 0;
        while (true)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            command.Transaction = transaction;
            command.CommandText = CalibrationProjectionBatchSql;
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            command.Transaction = null;
            if (count == 0) break;
            prepared += count;
            _logger.LogInformation("Calibration metadata prepared. SourceBot={SourceBot}, Rows={Rows}", sourceBotKey, prepared);
            if (reportPrepared is not null) await reportPrepared(prepared);
        }
    }

    internal const string CalibrationProjectionMissingSql = """
        INSERT #CalibrationProjectionMissing (EvaluationId)
        SELECT e.AutomatedBotPickEvaluationId
        FROM dbo.AutomatedBotPickEvaluations AS e
            WITH (INDEX(IX_AutomatedBotPickEvaluations_BotDecisionDate))
        WHERE e.BotKey = @SourceBotKey
          AND e.Decision IN (N'Approved', N'Rejected')
          AND e.MatchDate < @AsOfDateUtc AND e.MatchDate < SYSUTCDATETIME()
          AND e.SelectedSide IN (N'Over', N'Under') AND e.SelectedOdds > 1
          AND NOT EXISTS (SELECT 1 FROM dbo.AutomatedBotCalibrationEvaluationCache AS cached
              WHERE cached.AutomatedBotPickEvaluationId = e.AutomatedBotPickEvaluationId)
        OPTION (RECOMPILE);
        """;

    internal const string CalibrationProjectionBatchSql = """
        DROP TABLE IF EXISTS #CalibrationProjectionBatch;
        SELECT TOP (250) EvaluationId INTO #CalibrationProjectionBatch
        FROM #CalibrationProjectionMissing ORDER BY EvaluationId;
        INSERT dbo.AutomatedBotCalibrationEvaluationCache
            (AutomatedBotPickEvaluationId, BotKey, Decision, ApiFootballFixtureId, PublishedSelectionId,
             MatchDate, HomeTeam, AwayTeam, MarketType, SelectedSide, LineValue, SelectedOdds,
             BaseCalibratedProbability, MarketNoVigProbability, DataQualityScore,
             BaseModelTrainedThroughUtc, BaseModelVersion)
        SELECT e.AutomatedBotPickEvaluationId, e.BotKey, e.Decision,
            e.ApiFootballFixtureId, e.PublishedSelectionId, e.MatchDate, e.HomeTeam, e.AwayTeam,
            e.MarketType, e.SelectedSide, e.LineValue, e.SelectedOdds, e.BaseCalibratedProbability,
            e.MarketNoVigProbability, e.DataQualityScore, e.BaseModelTrainedThroughUtc, e.BaseModelVersion
        FROM #CalibrationProjectionBatch AS batch
        INNER LOOP JOIN dbo.AutomatedBotPickEvaluations AS e WITH (UPDLOCK, ROWLOCK, FORCESEEK)
          ON e.AutomatedBotPickEvaluationId = batch.EvaluationId
        WHERE NOT EXISTS (SELECT 1 FROM dbo.AutomatedBotCalibrationEvaluationCache AS cached WITH (UPDLOCK, HOLDLOCK)
            WHERE cached.AutomatedBotPickEvaluationId = e.AutomatedBotPickEvaluationId)
        OPTION (RECOMPILE, FORCE ORDER);
        DELETE missing FROM #CalibrationProjectionMissing AS missing
        INNER JOIN #CalibrationProjectionBatch AS batch ON batch.EvaluationId = missing.EvaluationId;
        SELECT COUNT(*) FROM #CalibrationProjectionBatch;
        """;

    internal const string CalibrationPreparationSql = """
        SET NOCOUNT ON;
        -- Many evaluations share a kickoff. Convert each distinct timestamp
        -- once instead of running AT TIME ZONE repeatedly over the full ledger.
        SELECT DISTINCT e.MatchDate INTO #CalibrationMatchDates
        FROM dbo.AutomatedBotCalibrationEvaluationCache AS e
        WHERE e.BotKey = @SourceBotKey
          AND e.Decision IN (N'Approved', N'Rejected')
          AND e.MatchDate < @AsOfDateUtc AND e.MatchDate < SYSUTCDATETIME();
        SELECT MatchDate, MatchDateUtc = CAST(MatchDate AT TIME ZONE 'Pacific SA Standard Time'
            AT TIME ZONE 'UTC' AS DATETIME2)
        INTO #CalibrationConvertedDates FROM #CalibrationMatchDates;
        CREATE UNIQUE CLUSTERED INDEX IX_CalibrationConvertedDates ON #CalibrationConvertedDates(MatchDate);

        -- Metadata is prepared once in bounded, committed blocks. Reading this
        -- compact projection avoids key lookups into the multi-GB JSON ledger.
        SELECT e.AutomatedBotPickEvaluationId, e.ApiFootballFixtureId,
            e.PublishedSelectionId, e.MatchDate,
            ExpectedUtcDate = CAST(dates.MatchDateUtc AS DATE), dates.MatchDateUtc,
            e.HomeTeam, e.AwayTeam, e.MarketType, e.SelectedSide,
            e.LineValue, e.SelectedOdds, e.BaseCalibratedProbability,
            e.MarketNoVigProbability, e.DataQualityScore, e.BaseModelTrainedThroughUtc,
            BaseModelVersion = COALESCE(NULLIF(e.BaseModelVersion, N''), N'unknown')
        INTO #CalibrationEvaluations
        FROM dbo.AutomatedBotCalibrationEvaluationCache AS e
        INNER JOIN #CalibrationConvertedDates AS dates ON dates.MatchDate = e.MatchDate
        WHERE e.BotKey = @SourceBotKey
          AND e.MatchDate < @AsOfDateUtc
          AND e.MatchDate < SYSUTCDATETIME()
          AND e.Decision IN (N'Approved', N'Rejected')
          AND e.SelectedSide IN (N'Over', N'Under')
          AND e.SelectedOdds > 1
          AND e.MarketNoVigProbability > 0 AND e.MarketNoVigProbability < 1
          AND e.DataQualityScore BETWEEN 0 AND 1
          AND e.BaseModelTrainedThroughUtc IS NOT NULL
          AND dates.MatchDateUtc > e.BaseModelTrainedThroughUtc
        OPTION (RECOMPILE);
        CREATE UNIQUE CLUSTERED INDEX IX_CalibrationEvaluations ON #CalibrationEvaluations(AutomatedBotPickEvaluationId);

        -- Repeated evaluations of one fixture share the same fallback identity lookup.
        SELECT DISTINCT HomeTeam, AwayTeam, ExpectedUtcDate
        INTO #CalibrationFallbackScopes
        FROM #CalibrationEvaluations
        WHERE PublishedSelectionId IS NULL AND ApiFootballFixtureId IS NULL;

        SELECT mh.Id, mh.ApiFootballFixtureId, mh.MatchDate,
            HomeTeam = COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam),
            AwayTeam = COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam)
        INTO #CalibrationFallbackHistory
        FROM dbo.MatchHistory AS mh
        WHERE mh.MatchDate >= (SELECT DATEADD(DAY,-1,MIN(ExpectedUtcDate)) FROM #CalibrationFallbackScopes)
          AND mh.MatchDate <= (SELECT DATEADD(DAY,1,MAX(ExpectedUtcDate)) FROM #CalibrationFallbackScopes)
        OPTION (RECOMPILE);

        SELECT scope.HomeTeam, scope.AwayTeam, scope.ExpectedUtcDate,
            MatchHistoryId = CONVERT(BIGINT, history.Id), history.ApiFootballFixtureId,
            DateDistanceDays = ABS(DATEDIFF(DAY, scope.ExpectedUtcDate, history.MatchDate))
        INTO #CalibrationFallbackMatches
        FROM #CalibrationFallbackScopes AS scope
        INNER JOIN #CalibrationFallbackHistory AS history
          ON history.MatchDate BETWEEN DATEADD(DAY,-1,scope.ExpectedUtcDate) AND DATEADD(DAY,1,scope.ExpectedUtcDate)
         AND history.HomeTeam COLLATE Latin1_General_100_CI_AI = scope.HomeTeam COLLATE Latin1_General_100_CI_AI
         AND history.AwayTeam COLLATE Latin1_General_100_CI_AI = scope.AwayTeam COLLATE Latin1_General_100_CI_AI;

        WITH CandidateMatchesRaw AS
        (
            SELECT
                e.AutomatedBotPickEvaluationId,
                MatchHistoryId = CONVERT(BIGINT, mh.Id),
                mh.ApiFootballFixtureId,
                LinkPriority = 0,
                DateDistanceDays = ABS(DATEDIFF(DAY, e.ExpectedUtcDate, mh.MatchDate))
            FROM #CalibrationEvaluations e
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
            FROM #CalibrationEvaluations e
            INNER JOIN dbo.MatchHistory mh
                ON mh.ApiFootballFixtureId = e.ApiFootballFixtureId
            WHERE e.ApiFootballFixtureId IS NOT NULL

            UNION ALL

            SELECT e.AutomatedBotPickEvaluationId, fallback.MatchHistoryId,
                fallback.ApiFootballFixtureId, LinkPriority = 2, fallback.DateDistanceDays
            FROM #CalibrationEvaluations AS e
            INNER JOIN #CalibrationFallbackMatches AS fallback
              ON fallback.HomeTeam = e.HomeTeam AND fallback.AwayTeam = e.AwayTeam
             AND fallback.ExpectedUtcDate = e.ExpectedUtcDate
            WHERE e.PublishedSelectionId IS NULL AND e.ApiFootballFixtureId IS NULL
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
            OriginalMatchDate = e.MatchDate,
            FixtureId = mh.ApiFootballFixtureId,
            e.MatchDateUtc,
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
            e.BaseModelVersion
        INTO #CalibrationObservations
        FROM #CalibrationEvaluations e
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
        OPTION (RECOMPILE);


        -- Only uncached observations read a feature snapshot. The cache is
        -- invalidated by evaluation writes; source rows are locked until each cache block commits.
        SELECT cached.EvaluationId, cached.SourceProbability
        INTO #CalibrationCachedSources
        FROM #CalibrationObservations AS observation
        INNER JOIN dbo.AutomatedBotCalibrationProbabilityCache AS cached
          ON cached.EvaluationId = observation.EvaluationId;

        SELECT observation.EvaluationId
        INTO #CalibrationMissingSources
        FROM #CalibrationObservations AS observation
        LEFT JOIN #CalibrationCachedSources AS cached
          ON cached.EvaluationId = observation.EvaluationId
        WHERE cached.EvaluationId IS NULL;

        """;

    private const string CalibrationSnapshotColumnsSql = """
        -- Fetch only the missing ids. Parse JSON in the application: OPENJSON
        -- over the audit blob makes compilation request expensive blob-column
        -- statistics before even reading this bounded block.
        SELECT evaluation.AutomatedBotPickEvaluationId AS EvaluationId,
            evaluation.FeatureSnapshotJson, evaluation.BaseCalibratedProbability,
            SourceHash = CONVERT(BINARY(32), NULL)
        """;

    private const string CalibrationSnapshotSourceSql = """
        FROM (SELECT TOP (@SnapshotBatchSize) EvaluationId FROM #CalibrationMissingSources ORDER BY EvaluationId) AS missing
        INNER LOOP JOIN dbo.AutomatedBotPickEvaluations AS evaluation
            WITH (UPDLOCK, ROWLOCK, INDEX(PK_AutomatedBotPickEvaluations), FORCESEEK)
          ON evaluation.AutomatedBotPickEvaluationId = missing.EvaluationId
        OPTION (RECOMPILE, FORCE ORDER);

        """;

    internal const string CalibrationSnapshotReadSql = CalibrationSnapshotColumnsSql + "\n" + CalibrationSnapshotSourceSql;
    internal const string CalibrationSnapshotBatchSql = "DROP TABLE IF EXISTS #CalibrationMissingSnapshots;\n"
        + CalibrationSnapshotColumnsSql + "\nINTO #CalibrationMissingSnapshots\n" + CalibrationSnapshotSourceSql;

    internal const string CalibrationCachedResultsSql = """
        SELECT observation.EvaluationId, observation.FixtureId, observation.MatchDateUtc,
            observation.MarketType, observation.SelectedSide, observation.LineValue,
            observation.Odds, observation.ActualValue, observation.BaseCalibratedProbability,
            observation.MarketNoVigProbability, observation.DataQualityScore,
            FeatureSnapshotJson = CONVERT(NVARCHAR(MAX), NULL), observation.BaseModelVersion,
            HasCachedProbability = CONVERT(BIT, 1), cached.SourceProbability AS CachedProbability
        FROM #CalibrationObservations AS observation
        INNER JOIN #CalibrationCachedSources AS cached ON cached.EvaluationId = observation.EvaluationId
        ORDER BY observation.OriginalMatchDate, observation.EvaluationId;
        """;

    internal const string CalibrationResultsSql = """
        SELECT observation.EvaluationId, observation.FixtureId, observation.MatchDateUtc,
            observation.MarketType, observation.SelectedSide, observation.LineValue,
            observation.Odds, observation.ActualValue,
            BaseCalibratedProbability = CASE WHEN missing.EvaluationId IS NOT NULL
                THEN missing.BaseCalibratedProbability ELSE observation.BaseCalibratedProbability END,
            observation.MarketNoVigProbability, observation.DataQualityScore,
            missing.FeatureSnapshotJson, observation.BaseModelVersion, missing.SourceHash,
            HasCachedProbability = CONVERT(BIT, CASE WHEN cached.EvaluationId IS NULL THEN 0 ELSE 1 END),
            cached.SourceProbability AS CachedProbability
        FROM #CalibrationObservations AS observation
        LEFT JOIN #CalibrationMissingSnapshots AS missing ON missing.EvaluationId = observation.EvaluationId
        LEFT JOIN #CalibrationCachedSources AS cached ON cached.EvaluationId = observation.EvaluationId
        ORDER BY observation.OriginalMatchDate, observation.EvaluationId
        OPTION (RECOMPILE);
        """;
    internal const string CalibrationHistorySql = "DECLARE @SnapshotBatchSize INT = 2147483647;\n"
        + CalibrationPreparationSql + "\n" + CalibrationSnapshotBatchSql + "\n" + CalibrationResultsSql;
}
