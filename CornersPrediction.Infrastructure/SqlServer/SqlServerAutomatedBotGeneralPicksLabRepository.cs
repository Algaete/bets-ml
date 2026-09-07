using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed partial class SqlServerAutomatedBotResearchRepository
{
    public async Task<GeneralPickLab> GetLabAsync(
        AutomatedBotResearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!string.Equals(query.ModelDecision, "Approved", StringComparison.Ordinal))
            throw new ArgumentException("The General Picks lab only accepts model-approved observations.", nameof(query));

        var parameters = new
        {
            query.DateFromUtc,
            query.DateToExclusiveUtc,
            query.MarketFamily,
            query.MarketType,
            query.BotKey,
            query.PublicationStatus
        };
        await using var connection = new SqlConnection(_connectionString);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            GeneralPickLabSql,
            parameters,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        var summary = await results.ReadSingleAsync<GeneralPickLabSummary>();
        var timeline = (await results.ReadAsync<GeneralPickLabDailyPoint>()).AsList();
        var calibration = (await results.ReadAsync<GeneralPickLabCalibrationBin>()).AsList();
        var segments = (await results.ReadAsync<GeneralPickLabSegment>()).AsList();
        return new GeneralPickLab(summary, timeline, calibration, segments, DateTime.UtcNow);
    }

    private const string GeneralPickLabSql = """
        SET NOCOUNT ON;

        """ + GeneralApprovedAuditKeysSql + """

        DELETE approved
        FROM #GeneralAuditKeys AS approved
        WHERE @PublicationStatus IS NOT NULL
          AND NOT
          (
              (@PublicationStatus = N'NotPublished'
                  AND approved.PublicationStatus <> N'Published')
              OR (@PublicationStatus <> N'NotPublished'
                  AND approved.PublicationStatus = @PublicationStatus)
          );

        ;WITH CandidateBase AS
        (
            SELECT
                evaluation.AutomatedBotPickEvaluationId,
                evaluation.PublishedSelectionId,
                evaluation.BotKey,
                evaluation.ApiFootballFixtureId,
                FixtureKey = CASE WHEN evaluation.ApiFootballFixtureId > 0
                    THEN CONCAT(N'api:', evaluation.ApiFootballFixtureId)
                    ELSE CONCAT(N'name:', CONVERT(CHAR(10), evaluation.MatchDate, 23), N'|',
                        UPPER(LTRIM(RTRIM(evaluation.HomeTeam))), N'|',
                        UPPER(LTRIM(RTRIM(evaluation.AwayTeam)))) END,
                evaluation.MatchDate,
                evaluation.MarketType,
                evaluation.SelectedSide,
                evaluation.LineValue,
                evaluation.SelectedOdds,
                evaluation.FinalProbability,
                DecisionAtUtc = COALESCE(
                    evaluation.PredictionTimestampUtc,
                    CONVERT(DATETIME2(3), evaluation.EvaluatedAtUtc)),
                evaluation.IsResearchWinner
            FROM #GeneralAuditKeys AS approved
            INNER JOIN dbo.AutomatedBotPickEvaluations AS evaluation
                WITH (INDEX(IX_AutomatedBotPickEvaluations_GeneralDecisionPage))
              ON evaluation.AutomatedBotPickEvaluationId = approved.EvaluationId
            WHERE evaluation.SelectedSide IN (N'Over', N'Under')
              AND evaluation.SelectedOdds > 1
        )
        SELECT candidate.*,
            SignalSequence = ROW_NUMBER() OVER
            (
                PARTITION BY candidate.BotKey, candidate.FixtureKey, candidate.MarketType,
                    candidate.SelectedSide, candidate.LineValue
                ORDER BY ISNULL(candidate.IsResearchWinner, 0) DESC,
                    candidate.DecisionAtUtc, candidate.AutomatedBotPickEvaluationId
            )
        INTO #LabRankedSignals
        FROM CandidateBase AS candidate
        OPTION (RECOMPILE);

        SELECT
            AutomatedBotPickEvaluationId,
            PublishedSelectionId,
            BotKey,
            ApiFootballFixtureId,
            FixtureKey,
            MatchDate,
            MarketType,
            SelectedSide,
            LineValue,
            SelectedOdds,
            FinalProbability,
            DecisionAtUtc
        INTO #LabSignals
        FROM #LabRankedSignals
        WHERE SignalSequence = 1;

        CREATE UNIQUE CLUSTERED INDEX IX_LabSignalsId
            ON #LabSignals(AutomatedBotPickEvaluationId);

        SELECT DISTINCT ApiFootballFixtureId
        INTO #LabFixtureIds
        FROM #LabSignals
        WHERE ApiFootballFixtureId > 0;

        CREATE UNIQUE CLUSTERED INDEX IX_LabFixtureIds
            ON #LabFixtureIds(ApiFootballFixtureId);

        -- Read each official fixture once. A single match can back dozens of
        -- approved bot/market/line signals, so resolving per signal would make
        -- the dashboard compete with the robot for the same Azure pages.
        SELECT fixture.ApiFootballFixtureId,
            history.ApiFootballUpdatedAtUtc,
            history.ApiFootballGoalsAvailable,
            history.ApiFootballCornersAvailable,
            history.ApiFootballShotsAvailable,
            history.ApiFootballShotsOnGoalAvailable,
            history.HomeGoals,
            history.AwayGoals,
            history.HomeCorners,
            history.AwayCorners,
            history.HomeShots,
            history.AwayShots,
            history.HomeShotsOnGoal,
            history.AwayShotsOnGoal
        INTO #LabOfficialFixtures
        FROM #LabFixtureIds AS fixture
        CROSS APPLY
        (
            SELECT TOP (1)
                candidate.ApiFootballUpdatedAtUtc,
                candidate.ApiFootballGoalsAvailable,
                candidate.ApiFootballCornersAvailable,
                candidate.ApiFootballShotsAvailable,
                candidate.ApiFootballShotsOnGoalAvailable,
                candidate.HomeGoals,
                candidate.AwayGoals,
                candidate.HomeCorners,
                candidate.AwayCorners,
                candidate.HomeShots,
                candidate.AwayShots,
                candidate.HomeShotsOnGoal,
                candidate.AwayShotsOnGoal
            FROM dbo.MatchHistory AS candidate
            WHERE candidate.ApiFootballFixtureId = fixture.ApiFootballFixtureId
              AND candidate.ApiFootballUpdatedAtUtc <= SYSUTCDATETIME()
              AND UPPER(LTRIM(RTRIM(COALESCE(candidate.FixtureStatus, N''))))
                  IN (N'FT', N'AET', N'PEN')
            ORDER BY candidate.ApiFootballUpdatedAtUtc DESC, candidate.Id DESC
        ) AS history
        OPTION (RECOMPILE);

        CREATE UNIQUE CLUSTERED INDEX IX_LabOfficialFixtures
            ON #LabOfficialFixtures(ApiFootballFixtureId);

        SELECT
            evaluation.AutomatedBotPickEvaluationId,
            evaluation.BotKey,
            evaluation.MarketType,
            evaluation.FixtureKey,
            evaluation.ApiFootballFixtureId,
            MatchDay = CONVERT(DATE, evaluation.MatchDate),
            evaluation.FinalProbability,
            OutcomeStatus = CASE
                WHEN manual.OutcomeStatus = N'Void' THEN N'Void'
                WHEN actual.ActualValue IS NULL AND evaluation.MatchDate > SYSUTCDATETIME()
                    THEN N'Pending'
                WHEN actual.ActualValue IS NULL THEN N'Unavailable'
                WHEN settlement.SettlementFactor = 1.0000 THEN N'Win'
                WHEN settlement.SettlementFactor = 0.5000 THEN N'HalfWin'
                WHEN settlement.SettlementFactor = 0.0000 THEN N'Push'
                WHEN settlement.SettlementFactor = -0.5000 THEN N'HalfLoss'
                WHEN settlement.SettlementFactor = -1.0000 THEN N'Loss'
                ELSE N'Unavailable'
            END,
            settlement.SettlementFactor,
            ProfitLossUnits = CONVERT(DECIMAL(18,6), CASE
                WHEN manual.OutcomeStatus = N'Void' THEN 0
                WHEN settlement.SettlementFactor > 0
                    THEN settlement.SettlementFactor * (evaluation.SelectedOdds - 1.0)
                WHEN settlement.SettlementFactor <= 0
                    THEN settlement.SettlementFactor
            END),
            BinaryOutcome = CONVERT(FLOAT, CASE
                WHEN settlement.SettlementFactor > 0 THEN 1.0
                WHEN settlement.SettlementFactor < 0 THEN 0.0
            END)
        INTO #LabOutcomes
        FROM #LabSignals AS evaluation
        """ + GeneralManualOutcomeApplySql + """
        LEFT JOIN #LabOfficialFixtures AS history
          ON history.ApiFootballFixtureId = evaluation.ApiFootballFixtureId
        OUTER APPLY
        (
            SELECT ActualValue = COALESCE(manual.ActualValue,
                CONVERT(DECIMAL(12,4), CASE
                    WHEN evaluation.DecisionAtUtc >= evaluation.MatchDate
                      OR history.ApiFootballUpdatedAtUtc <= evaluation.DecisionAtUtc
                      OR (evaluation.BotKey = N'H2026' AND NOT EXISTS
                          (SELECT 1 FROM dbo.BotH2026ShadowEvaluations AS shadow
                           WHERE shadow.SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId))
                        THEN NULL
                    WHEN evaluation.MarketType = N'TotalGoals'
                      AND ISNULL(history.ApiFootballGoalsAvailable, 0) = 1
                        THEN history.HomeGoals + history.AwayGoals
                    WHEN evaluation.MarketType = N'HomeTeamGoals'
                      AND ISNULL(history.ApiFootballGoalsAvailable, 0) = 1
                        THEN history.HomeGoals
                    WHEN evaluation.MarketType = N'AwayTeamGoals'
                      AND ISNULL(history.ApiFootballGoalsAvailable, 0) = 1
                        THEN history.AwayGoals
                    WHEN evaluation.MarketType = N'TotalCorners'
                      AND ISNULL(history.ApiFootballCornersAvailable, 0) = 1
                        THEN history.HomeCorners + history.AwayCorners
                    WHEN evaluation.MarketType = N'HomeTeamCorners'
                      AND ISNULL(history.ApiFootballCornersAvailable, 0) = 1
                        THEN history.HomeCorners
                    WHEN evaluation.MarketType = N'AwayTeamCorners'
                      AND ISNULL(history.ApiFootballCornersAvailable, 0) = 1
                        THEN history.AwayCorners
                    WHEN evaluation.MarketType = N'TotalShots'
                      AND ISNULL(history.ApiFootballShotsAvailable, 0) = 1
                        THEN history.HomeShots + history.AwayShots
                    WHEN evaluation.MarketType = N'HomeTeamShots'
                      AND ISNULL(history.ApiFootballShotsAvailable, 0) = 1
                        THEN history.HomeShots
                    WHEN evaluation.MarketType = N'AwayTeamShots'
                      AND ISNULL(history.ApiFootballShotsAvailable, 0) = 1
                        THEN history.AwayShots
                    WHEN evaluation.MarketType = N'TotalShotsOnGoal'
                      AND ISNULL(history.ApiFootballShotsOnGoalAvailable, 0) = 1
                        THEN history.HomeShotsOnGoal + history.AwayShotsOnGoal
                    WHEN evaluation.MarketType = N'HomeTeamShotsOnGoal'
                      AND ISNULL(history.ApiFootballShotsOnGoalAvailable, 0) = 1
                        THEN history.HomeShotsOnGoal
                    WHEN evaluation.MarketType = N'AwayTeamShotsOnGoal'
                      AND ISNULL(history.ApiFootballShotsOnGoalAvailable, 0) = 1
                        THEN history.AwayShotsOnGoal
                END))
        ) AS actual
        OUTER APPLY
        (
            SELECT
                FirstLine = CASE
                    WHEN evaluation.LineValue - FLOOR(evaluation.LineValue) IN (0.25, 0.75)
                        THEN evaluation.LineValue - 0.25
                    ELSE evaluation.LineValue END,
                SecondLine = CASE
                    WHEN evaluation.LineValue - FLOOR(evaluation.LineValue) IN (0.25, 0.75)
                        THEN evaluation.LineValue + 0.25
                    ELSE evaluation.LineValue END
        ) AS split
        OUTER APPLY
        (
            SELECT SettlementFactor = CONVERT(DECIMAL(9,4), CASE
                WHEN actual.ActualValue IS NULL OR manual.OutcomeStatus = N'Void' THEN NULL
                ELSE
                (
                    CONVERT(DECIMAL(9,4), CASE evaluation.SelectedSide
                        WHEN N'Over' THEN CASE
                            WHEN actual.ActualValue > split.FirstLine THEN 1.0
                            WHEN actual.ActualValue = split.FirstLine THEN 0.0 ELSE -1.0 END
                        WHEN N'Under' THEN CASE
                            WHEN actual.ActualValue < split.FirstLine THEN 1.0
                            WHEN actual.ActualValue = split.FirstLine THEN 0.0 ELSE -1.0 END
                    END)
                    +
                    CONVERT(DECIMAL(9,4), CASE evaluation.SelectedSide
                        WHEN N'Over' THEN CASE
                            WHEN actual.ActualValue > split.SecondLine THEN 1.0
                            WHEN actual.ActualValue = split.SecondLine THEN 0.0 ELSE -1.0 END
                        WHEN N'Under' THEN CASE
                            WHEN actual.ActualValue < split.SecondLine THEN 1.0
                            WHEN actual.ActualValue = split.SecondLine THEN 0.0 ELSE -1.0 END
                    END)
                ) / 2.0
            END)
        ) AS settlement
        OPTION (RECOMPILE);

        SELECT
            ApprovedEvaluations = (SELECT COUNT_BIG(*) FROM #GeneralAuditKeys),
            IndependentSignals = COUNT(1),
            UniqueFixtures = COUNT(DISTINCT outcome.FixtureKey),
            ResolvedSignals = COALESCE(SUM(CASE WHEN outcome.SettlementFactor IS NOT NULL THEN 1 ELSE 0 END), 0),
            PendingSignals = COALESCE(SUM(CASE WHEN outcome.OutcomeStatus = N'Pending' THEN 1 ELSE 0 END), 0),
            UnavailableSignals = COALESCE(SUM(CASE WHEN outcome.OutcomeStatus = N'Unavailable' THEN 1 ELSE 0 END), 0),
            VoidSignals = COALESCE(SUM(CASE WHEN outcome.OutcomeStatus = N'Void' THEN 1 ELSE 0 END), 0),
            ProfitLossUnits = COALESCE(SUM(outcome.ProfitLossUnits), 0),
            Yield = CONVERT(DECIMAL(18,6), CASE
                WHEN SUM(CASE WHEN outcome.SettlementFactor IS NOT NULL THEN 1 ELSE 0 END) > 0
                THEN SUM(outcome.ProfitLossUnits)
                    / SUM(CASE WHEN outcome.SettlementFactor IS NOT NULL THEN 1.0 ELSE 0.0 END)
            END),
            ObservedWinRate = AVG(outcome.BinaryOutcome),
            AverageModelProbability = AVG(CASE WHEN outcome.BinaryOutcome IS NOT NULL
                AND outcome.FinalProbability > 0 AND outcome.FinalProbability < 1
                THEN CONVERT(FLOAT, outcome.FinalProbability) END),
            CalibrationGap = AVG(CASE WHEN outcome.BinaryOutcome IS NOT NULL
                AND outcome.FinalProbability > 0 AND outcome.FinalProbability < 1
                THEN CONVERT(FLOAT, outcome.FinalProbability) END)
                - AVG(CASE WHEN outcome.BinaryOutcome IS NOT NULL
                    AND outcome.FinalProbability > 0 AND outcome.FinalProbability < 1
                    THEN outcome.BinaryOutcome END),
            BrierScore = AVG(CASE WHEN outcome.BinaryOutcome IS NOT NULL
                AND outcome.FinalProbability > 0 AND outcome.FinalProbability < 1
                THEN POWER(CONVERT(FLOAT, outcome.FinalProbability) - outcome.BinaryOutcome, 2) END)
        FROM #LabOutcomes AS outcome;

        ;WITH Daily AS
        (
            SELECT outcome.MatchDay,
                ResolvedSignals = COUNT(1),
                DailyProfitLossUnits = SUM(outcome.ProfitLossUnits)
            FROM #LabOutcomes AS outcome
            WHERE outcome.SettlementFactor IS NOT NULL
            GROUP BY outcome.MatchDay
        )
        SELECT [Date] = CONVERT(DATETIME2, daily.MatchDay),
            daily.ResolvedSignals,
            DailyProfitLossUnits = CONVERT(DECIMAL(18,6), daily.DailyProfitLossUnits),
            CumulativeProfitLossUnits = CONVERT(DECIMAL(18,6),
                SUM(daily.DailyProfitLossUnits) OVER
                    (ORDER BY daily.MatchDay ROWS UNBOUNDED PRECEDING))
        FROM Daily AS daily
        ORDER BY daily.MatchDay;

        ;WITH Predictive AS
        (
            SELECT outcome.*,
                BinNumber = CASE
                    WHEN FLOOR(outcome.FinalProbability * 5) >= 5 THEN 4
                    ELSE CONVERT(INT, FLOOR(outcome.FinalProbability * 5)) END
            FROM #LabOutcomes AS outcome
            WHERE outcome.BinaryOutcome IS NOT NULL
              AND outcome.FinalProbability > 0 AND outcome.FinalProbability < 1
        )
        SELECT
            ProbabilityFrom = CONVERT(DECIMAL(5,2), predictive.BinNumber * 0.20),
            ProbabilityTo = CONVERT(DECIMAL(5,2), (predictive.BinNumber + 1) * 0.20),
            Signals = COUNT(1),
            AverageModelProbability = AVG(CONVERT(FLOAT, predictive.FinalProbability)),
            ObservedWinRate = AVG(predictive.BinaryOutcome)
        FROM Predictive AS predictive
        GROUP BY predictive.BinNumber
        ORDER BY predictive.BinNumber;

        SELECT TOP (12)
            outcome.BotKey,
            outcome.MarketType,
            ResolvedSignals = SUM(CASE WHEN outcome.SettlementFactor IS NOT NULL THEN 1 ELSE 0 END),
            ProfitLossUnits = COALESCE(SUM(outcome.ProfitLossUnits), 0),
            Yield = CONVERT(DECIMAL(18,6), CASE
                WHEN SUM(CASE WHEN outcome.SettlementFactor IS NOT NULL THEN 1 ELSE 0 END) > 0
                THEN SUM(outcome.ProfitLossUnits)
                    / SUM(CASE WHEN outcome.SettlementFactor IS NOT NULL THEN 1.0 ELSE 0.0 END)
            END),
            ObservedWinRate = AVG(outcome.BinaryOutcome)
        FROM #LabOutcomes AS outcome
        GROUP BY outcome.BotKey, outcome.MarketType
        HAVING SUM(CASE WHEN outcome.SettlementFactor IS NOT NULL THEN 1 ELSE 0 END) > 0
        ORDER BY ResolvedSignals DESC, outcome.BotKey, outcome.MarketType;
        """;
}
