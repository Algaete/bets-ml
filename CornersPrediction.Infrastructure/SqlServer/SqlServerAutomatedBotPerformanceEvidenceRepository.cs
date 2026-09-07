using System.Data;
using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

/// <summary>
/// Reads the scientific C-F decision ledger and settles approved candidates
/// against timestamped, official API-Football outcomes. Publication columns are
/// deliberately absent from both the predicate and the projection.
/// </summary>
public sealed class SqlServerAutomatedBotPerformanceEvidenceRepository
    : IAutomatedBotPerformanceEvidenceRepository
{
    private readonly string _connectionString;

    public SqlServerAutomatedBotPerformanceEvidenceRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<AutomatedBotPerformanceEvidence>> GetSettledEvidenceAsync(
        DateTime fixtureFromUtc,
        DateTime fixtureToUtc,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET NOCOUNT ON;

            -- Resolve the date/decision predicates first. Keeping the direct
            -- Approved and legacy publication-rejection paths separate lets the
            -- filtered covering indexes seek both branches without loading the
            -- much wider feature/audit rows. The legacy branch uses a persisted
            -- flag with the original LIKE semantics, avoiding repeated JSON
            -- scans for every scorecard request.
            -- Keep this temp table deliberately narrow. Descriptive fields,
            -- prices and probabilities are fetched by primary key only after
            -- one research winner per fixture/family has been selected.
            CREATE TABLE #Candidates
            (
                EvidenceId BIGINT NOT NULL,
                RunId UNIQUEIDENTIFIER NOT NULL,
                BotKey NVARCHAR(50) NOT NULL,
                AutomationVersion NVARCHAR(50) NOT NULL,
                ApiFootballFixtureId BIGINT NOT NULL,
                MarketFamily NVARCHAR(10) NOT NULL,
                ProbabilityEdge DECIMAL(10,6) NULL,
                FinalExpectedValue DECIMAL(9,6) NULL,
                SelectionScore DECIMAL(9,6) NULL,
                IsResearchWinner BIT NULL,
                DecisionAtUtc DATETIME2(3) NOT NULL
            );

            INSERT INTO #Candidates
            SELECT
                evaluation.AutomatedBotPickEvaluationId,
                evaluation.RunId,
                evaluation.BotKey,
                evaluation.AutomationVersion,
                evaluation.ApiFootballFixtureId,
                CASE
                    WHEN evaluation.MarketType IN
                        (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners') THEN N'CORNERS'
                    WHEN evaluation.MarketType IN
                        (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals') THEN N'GOALS'
                    WHEN evaluation.MarketType IN
                        (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots') THEN N'SHOTS'
                    WHEN evaluation.MarketType IN
                        (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal') THEN N'SOG'
                END,
                COALESCE(
                    evaluation.FinalEdge,
                    evaluation.FinalProbability - COALESCE(
                        evaluation.MarketNoVigProbability,
                        evaluation.RawImpliedProbability)),
                evaluation.FinalExpectedValue,
                COALESCE(evaluation.SelectionScore, evaluation.RuleBasedConfidenceScore),
                evaluation.IsResearchWinner,
                COALESCE(
                    evaluation.PredictionTimestampUtc,
                    CONVERT(DATETIME2(3), evaluation.EvaluatedAtUtc))
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
            WHERE evaluation.BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026')
              AND evaluation.Decision = N'Approved'
              AND evaluation.MatchDate >= @FixtureFromUtc
              AND evaluation.MatchDate < @FixtureToUtc
              AND evaluation.MatchDate < @AsOfUtc
              AND evaluation.ApiFootballFixtureId > 0
              AND evaluation.SelectedSide IN (N'Over', N'Under')
              AND evaluation.SelectedOdds > 1
              AND evaluation.FinalProbability > 0
              AND evaluation.FinalProbability < 1
              AND evaluation.MarketType IN
                  (
                      N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals',
                      N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners',
                      N'TotalShots', N'HomeTeamShots', N'AwayTeamShots',
                      N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal'
                  )
            OPTION (RECOMPILE);

            INSERT INTO #Candidates
            SELECT
                evaluation.AutomatedBotPickEvaluationId,
                evaluation.RunId,
                evaluation.BotKey,
                evaluation.AutomationVersion,
                evaluation.ApiFootballFixtureId,
                CASE
                    WHEN evaluation.MarketType IN
                        (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners') THEN N'CORNERS'
                    WHEN evaluation.MarketType IN
                        (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals') THEN N'GOALS'
                    WHEN evaluation.MarketType IN
                        (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots') THEN N'SHOTS'
                    WHEN evaluation.MarketType IN
                        (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal') THEN N'SOG'
                END,
                COALESCE(
                    evaluation.FinalEdge,
                    evaluation.FinalProbability - COALESCE(
                        evaluation.MarketNoVigProbability,
                        evaluation.RawImpliedProbability)),
                evaluation.FinalExpectedValue,
                COALESCE(evaluation.SelectionScore, evaluation.RuleBasedConfidenceScore),
                evaluation.IsResearchWinner,
                COALESCE(
                    evaluation.PredictionTimestampUtc,
                    CONVERT(DATETIME2(3), evaluation.EvaluatedAtUtc))
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
            WHERE evaluation.BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026')
              AND evaluation.Decision = N'Rejected'
              AND evaluation.PerformanceLegacyPublicationRejection = 1
              AND evaluation.MatchDate >= @FixtureFromUtc
              AND evaluation.MatchDate < @FixtureToUtc
              AND evaluation.MatchDate < @AsOfUtc
              AND evaluation.ApiFootballFixtureId > 0
              AND evaluation.SelectedSide IN (N'Over', N'Under')
              AND evaluation.SelectedOdds > 1
              AND evaluation.FinalProbability > 0
              AND evaluation.FinalProbability < 1
              AND evaluation.MarketType IN
                  (
                      N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals',
                      N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners',
                      N'TotalShots', N'HomeTeamShots', N'AwayTeamShots',
                      N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal'
                  )
            OPTION (RECOMPILE);

            CREATE UNIQUE CLUSTERED INDEX IX_PerformanceCandidatesScope
                ON #Candidates
                   (BotKey, AutomationVersion, ApiFootballFixtureId, MarketFamily,
                    RunId, DecisionAtUtc, EvidenceId);

            SELECT
                candidate.BotKey,
                candidate.AutomationVersion,
                candidate.ApiFootballFixtureId,
                candidate.MarketFamily,
                candidate.RunId,
                RunFirstDecisionAtUtc = MIN(candidate.DecisionAtUtc)
            INTO #CandidateRuns
            FROM #Candidates AS candidate
            GROUP BY
                candidate.BotKey,
                candidate.AutomationVersion,
                candidate.ApiFootballFixtureId,
                candidate.MarketFamily,
                candidate.RunId;

            CREATE UNIQUE CLUSTERED INDEX IX_PerformanceCandidateRunsScope
                ON #CandidateRuns
                   (BotKey, AutomationVersion, ApiFootballFixtureId, MarketFamily, RunId);

            CREATE TABLE #ResearchWinnerIds
            (
                BotKey NVARCHAR(50) NOT NULL,
                AutomationVersion NVARCHAR(50) NOT NULL,
                ApiFootballFixtureId BIGINT NOT NULL,
                MarketFamily NVARCHAR(10) NOT NULL,
                EvidenceId BIGINT NOT NULL,
                PRIMARY KEY CLUSTERED
                    (BotKey, AutomationVersion, ApiFootballFixtureId, MarketFamily),
                UNIQUE (EvidenceId)
            );

            -- New rows carry an explicit research winner. Rank only those rows,
            -- preserving the original earliest-run tie break across retries.
            ;WITH RankedExplicitWinners AS
            (
                SELECT
                    candidate.BotKey,
                    candidate.AutomationVersion,
                    candidate.ApiFootballFixtureId,
                    candidate.MarketFamily,
                    candidate.EvidenceId,
                    ResearchSequence = ROW_NUMBER() OVER
                    (
                        PARTITION BY candidate.BotKey, candidate.AutomationVersion,
                                     candidate.ApiFootballFixtureId, candidate.MarketFamily
                        ORDER BY
                            candidateRun.RunFirstDecisionAtUtc,
                            candidate.RunId,
                            candidate.SelectionScore DESC,
                            candidate.FinalExpectedValue DESC,
                            candidate.ProbabilityEdge DESC,
                            candidate.EvidenceId
                    )
                FROM #Candidates AS candidate
                INNER JOIN #CandidateRuns AS candidateRun
                  ON candidateRun.BotKey = candidate.BotKey
                 AND candidateRun.AutomationVersion = candidate.AutomationVersion
                 AND candidateRun.ApiFootballFixtureId = candidate.ApiFootballFixtureId
                 AND candidateRun.MarketFamily = candidate.MarketFamily
                 AND candidateRun.RunId = candidate.RunId
                WHERE candidate.IsResearchWinner = 1
            )
            INSERT INTO #ResearchWinnerIds
                (BotKey, AutomationVersion, ApiFootballFixtureId, MarketFamily, EvidenceId)
            SELECT
                ranked.BotKey,
                ranked.AutomationVersion,
                ranked.ApiFootballFixtureId,
                ranked.MarketFamily,
                ranked.EvidenceId
            FROM RankedExplicitWinners AS ranked
            WHERE ranked.ResearchSequence = 1;

            -- Legacy evaluations have no explicit flag. Only scopes not covered
            -- above pay the fallback ranking cost: earliest run, then score.
            ;WITH RankedLegacyRuns AS
            (
                SELECT
                    candidateRun.*,
                    RunSequence = ROW_NUMBER() OVER
                    (
                        PARTITION BY candidateRun.BotKey, candidateRun.AutomationVersion,
                                     candidateRun.ApiFootballFixtureId, candidateRun.MarketFamily
                        ORDER BY candidateRun.RunFirstDecisionAtUtc, candidateRun.RunId
                    )
                FROM #CandidateRuns AS candidateRun
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM #ResearchWinnerIds AS explicitWinner
                    WHERE explicitWinner.BotKey = candidateRun.BotKey
                      AND explicitWinner.AutomationVersion = candidateRun.AutomationVersion
                      AND explicitWinner.ApiFootballFixtureId = candidateRun.ApiFootballFixtureId
                      AND explicitWinner.MarketFamily = candidateRun.MarketFamily
                )
            ),
            RankedLegacyWinners AS
            (
                SELECT
                    candidate.BotKey,
                    candidate.AutomationVersion,
                    candidate.ApiFootballFixtureId,
                    candidate.MarketFamily,
                    candidate.EvidenceId,
                    ResearchSequence = ROW_NUMBER() OVER
                    (
                        PARTITION BY candidate.BotKey, candidate.AutomationVersion,
                                     candidate.ApiFootballFixtureId, candidate.MarketFamily
                        ORDER BY
                            candidate.SelectionScore DESC,
                            candidate.FinalExpectedValue DESC,
                            candidate.ProbabilityEdge DESC,
                            candidate.EvidenceId
                    )
                FROM #Candidates AS candidate
                INNER JOIN RankedLegacyRuns AS candidateRun
                  ON candidateRun.BotKey = candidate.BotKey
                 AND candidateRun.AutomationVersion = candidate.AutomationVersion
                 AND candidateRun.ApiFootballFixtureId = candidate.ApiFootballFixtureId
                 AND candidateRun.MarketFamily = candidate.MarketFamily
                 AND candidateRun.RunId = candidate.RunId
                 AND candidateRun.RunSequence = 1
            )
            INSERT INTO #ResearchWinnerIds
                (BotKey, AutomationVersion, ApiFootballFixtureId, MarketFamily, EvidenceId)
            SELECT
                ranked.BotKey,
                ranked.AutomationVersion,
                ranked.ApiFootballFixtureId,
                ranked.MarketFamily,
                ranked.EvidenceId
            FROM RankedLegacyWinners AS ranked
            WHERE ranked.ResearchSequence = 1;

            ;WITH MatchedOutcomes AS
            (
                SELECT
                    EvidenceId = evaluation.AutomatedBotPickEvaluationId,
                    evaluation.BotKey,
                    evaluation.AutomationVersion,
                    evaluation.ApiFootballFixtureId,
                    FixtureDateUtc = evaluation.MatchDate,
                    evaluation.League,
                    evaluation.HomeTeam,
                    evaluation.AwayTeam,
                    Bookmaker = COALESCE(NULLIF(evaluation.Bookmaker, N''), evaluation.Source),
                    evaluation.MarketType,
                    evaluation.SelectedSide,
                    evaluation.LineValue,
                    Odds = evaluation.SelectedOdds,
                    StakeUnits = COALESCE(
                        NULLIF(evaluation.StakeUnits, 0),
                        CONVERT(DECIMAL(9,4), 1)),
                    ModelProbability = evaluation.FinalProbability,
                    MarketProbability = COALESCE(
                        evaluation.MarketNoVigProbability,
                        evaluation.RawImpliedProbability),
                    ProbabilityEdge = COALESCE(
                        evaluation.FinalEdge,
                        evaluation.FinalProbability - COALESCE(
                            evaluation.MarketNoVigProbability,
                            evaluation.RawImpliedProbability)),
                    DecisionAtUtc = COALESCE(
                        evaluation.PredictionTimestampUtc,
                        CONVERT(DATETIME2(3), evaluation.EvaluatedAtUtc)),
                    history.Id AS MatchHistoryId,
                    history.FixtureStatus,
                    history.ApiFootballUpdatedAtUtc AS OutcomeAvailableAtUtc,
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
                FROM #ResearchWinnerIds AS winner
                INNER JOIN dbo.AutomatedBotPickEvaluations AS evaluation
                  ON evaluation.AutomatedBotPickEvaluationId = winner.EvidenceId
                -- The runtime schema installs an ApiFootballFixtureId-leading
                -- covering index. TOP (1) turns each winner into one exact seek
                -- without expanding MatchHistory into a joined/windowed set.
                CROSS APPLY
                (
                    SELECT TOP (1)
                        exactHistory.Id,
                        exactHistory.FixtureStatus,
                        exactHistory.ApiFootballUpdatedAtUtc,
                        exactHistory.ApiFootballGoalsAvailable,
                        exactHistory.ApiFootballCornersAvailable,
                        exactHistory.ApiFootballShotsAvailable,
                        exactHistory.ApiFootballShotsOnGoalAvailable,
                        exactHistory.HomeGoals,
                        exactHistory.AwayGoals,
                        exactHistory.HomeCorners,
                        exactHistory.AwayCorners,
                        exactHistory.HomeShots,
                        exactHistory.AwayShots,
                        exactHistory.HomeShotsOnGoal,
                        exactHistory.AwayShotsOnGoal
                    FROM dbo.MatchHistory AS exactHistory
                    WHERE exactHistory.ApiFootballFixtureId = evaluation.ApiFootballFixtureId
                    ORDER BY exactHistory.ApiFootballUpdatedAtUtc DESC, exactHistory.Id DESC
                ) AS history
                -- These invariants were already required in both candidate
                -- branches. Restating them lets SQL use the filtered, covered
                -- winner projection instead of reading the audit JSON rows.
                WHERE evaluation.BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026')
                  AND evaluation.ApiFootballFixtureId > 0
            ),
            SafeOutcomes AS
            (
                SELECT
                    matched.*,
                    ActualValue = CONVERT(DECIMAL(12,4), CASE matched.MarketType
                        WHEN N'TotalGoals' THEN matched.HomeGoals + matched.AwayGoals
                        WHEN N'HomeTeamGoals' THEN matched.HomeGoals
                        WHEN N'AwayTeamGoals' THEN matched.AwayGoals
                        WHEN N'TotalCorners' THEN matched.HomeCorners + matched.AwayCorners
                        WHEN N'HomeTeamCorners' THEN matched.HomeCorners
                        WHEN N'AwayTeamCorners' THEN matched.AwayCorners
                        WHEN N'TotalShots' THEN matched.HomeShots + matched.AwayShots
                        WHEN N'HomeTeamShots' THEN matched.HomeShots
                        WHEN N'AwayTeamShots' THEN matched.AwayShots
                        WHEN N'TotalShotsOnGoal' THEN matched.HomeShotsOnGoal + matched.AwayShotsOnGoal
                        WHEN N'HomeTeamShotsOnGoal' THEN matched.HomeShotsOnGoal
                        WHEN N'AwayTeamShotsOnGoal' THEN matched.AwayShotsOnGoal
                    END)
                FROM MatchedOutcomes AS matched
                WHERE UPPER(LTRIM(RTRIM(COALESCE(matched.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
                  AND matched.OutcomeAvailableAtUtc IS NOT NULL
                  AND matched.OutcomeAvailableAtUtc > matched.DecisionAtUtc
                  AND matched.OutcomeAvailableAtUtc <= @AsOfUtc
                  AND matched.DecisionAtUtc < matched.FixtureDateUtc
                  AND
                  (
                      (matched.MarketType IN (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals')
                       AND ISNULL(matched.ApiFootballGoalsAvailable, 0) = 1)
                      OR
                      (matched.MarketType IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')
                       AND ISNULL(matched.ApiFootballCornersAvailable, 0) = 1)
                      OR
                      (matched.MarketType IN (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots')
                       AND ISNULL(matched.ApiFootballShotsAvailable, 0) = 1)
                      OR
                      (matched.MarketType IN (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal')
                       AND ISNULL(matched.ApiFootballShotsOnGoalAvailable, 0) = 1)
                  )
            ),
            SplitLines AS
            (
                SELECT
                    outcome.*,
                    FirstLine = CASE
                        WHEN outcome.LineValue - FLOOR(outcome.LineValue) IN (0.25, 0.75)
                            THEN outcome.LineValue - 0.25
                        ELSE outcome.LineValue
                    END,
                    SecondLine = CASE
                        WHEN outcome.LineValue - FLOOR(outcome.LineValue) IN (0.25, 0.75)
                            THEN outcome.LineValue + 0.25
                        ELSE outcome.LineValue
                    END
                FROM SafeOutcomes AS outcome
                WHERE outcome.ActualValue IS NOT NULL
            ),
            Factorized AS
            (
                SELECT
                    split.*,
                    SettlementFactor = CONVERT(DECIMAL(9,4),
                    (
                        CONVERT(DECIMAL(9,4), CASE split.SelectedSide
                            WHEN N'Over' THEN CASE
                                WHEN split.ActualValue > split.FirstLine THEN 1.0
                                WHEN split.ActualValue = split.FirstLine THEN 0.0
                                ELSE -1.0 END
                            WHEN N'Under' THEN CASE
                                WHEN split.ActualValue < split.FirstLine THEN 1.0
                                WHEN split.ActualValue = split.FirstLine THEN 0.0
                                ELSE -1.0 END
                        END)
                        +
                        CONVERT(DECIMAL(9,4), CASE split.SelectedSide
                            WHEN N'Over' THEN CASE
                                WHEN split.ActualValue > split.SecondLine THEN 1.0
                                WHEN split.ActualValue = split.SecondLine THEN 0.0
                                ELSE -1.0 END
                            WHEN N'Under' THEN CASE
                                WHEN split.ActualValue < split.SecondLine THEN 1.0
                                WHEN split.ActualValue = split.SecondLine THEN 0.0
                                ELSE -1.0 END
                        END)
                    ) / 2.0)
                FROM SplitLines AS split
            )
            SELECT
                factor.EvidenceId,
                EvidenceKey = CONCAT(N'EVALUATION|', factor.EvidenceId),
                factor.BotKey,
                factor.AutomationVersion,
                factor.ApiFootballFixtureId,
                factor.FixtureDateUtc,
                factor.League,
                factor.HomeTeam,
                factor.AwayTeam,
                factor.Bookmaker,
                factor.MarketType,
                factor.SelectedSide,
                factor.LineValue,
                factor.Odds,
                factor.StakeUnits,
                ModelDecision = CONVERT(NVARCHAR(20), N'Approved'),
                Result = CASE
                    WHEN factor.SettlementFactor > 0 THEN N'Won'
                    WHEN factor.SettlementFactor < 0 THEN N'Lost'
                    ELSE N'Push'
                END,
                factor.SettlementFactor,
                ProfitLoss = CONVERT(DECIMAL(12,4), CASE
                    WHEN factor.SettlementFactor > 0
                        THEN factor.StakeUnits * (factor.Odds - 1) * factor.SettlementFactor
                    WHEN factor.SettlementFactor < 0
                        THEN factor.StakeUnits * factor.SettlementFactor
                    ELSE 0
                END),
                factor.ModelProbability,
                factor.MarketProbability,
                factor.ProbabilityEdge,
                factor.DecisionAtUtc,
                factor.OutcomeAvailableAtUtc
            FROM Factorized AS factor
            ORDER BY factor.FixtureDateUtc, factor.EvidenceId
            OPTION (RECOMPILE);
            """;

        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<AutomatedBotPerformanceEvidence>(
            new CommandDefinition(
                sql,
                new
                {
                    FixtureFromUtc = fixtureFromUtc,
                    FixtureToUtc = fixtureToUtc,
                    AsOfUtc = asOfUtc
                },
                commandType: CommandType.Text,
                commandTimeout: 300,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }
}
