DECLARE @DateFrom DATE = NULL;
DECLARE @DateTo DATE = NULL;
DECLARE @Status NVARCHAR(20) = NULL;
DECLARE @League NVARCHAR(200) = NULL;
DECLARE @OnlyPending BIT = 0;

SELECT
    s.AutomatedCornerBetSelectionId,
    s.RunId,
    s.AutomationVersion,
    s.Source,
    s.SourceMatchId,
    s.SourceUrl,
    s.MatchDate,
    MatchDay = CAST(s.MatchDate AS DATE),
    s.League,
    s.StandardizedLeague,
    s.HomeTeam,
    s.AwayTeam,
    s.StandardizedHomeTeam,
    s.StandardizedAwayTeam,
    Recommendation = CONCAT(s.MarketType, ' ', s.SelectedSide, ' ', CONVERT(VARCHAR(20), s.LineValue)),
    s.SelectedSide,
    s.LineValue,
    s.Odds,
    s.Stake,
    s.FlatStake,
    s.KellyFraction,
    s.ImpliedProbability,
    s.ModelProbability,
    s.ProbabilityEdge,
    s.ExpectedValue,
    s.SelectionScore,
    s.PredictedTotalCorners,
    s.PredTotalDirect,
    s.PredHomeCorners,
    s.PredAwayCorners,
    s.PredTotalCombined,
    s.DistanceToLine,
    s.ConfidenceLevel,
    s.OverUnderConfidenceLevel,
    s.ModelConsensus,
    s.ContextTotalCorners,
    s.ContextDifference,
    s.RecommendedSide,
    s.Status,
    s.ActualHomeCorners,
    s.ActualAwayCorners,
    s.ActualTotalCorners,
    s.ProfitLoss,
    s.YieldPct,
    s.DecisionReason,
    s.CreatedAtUtc,
    s.UpdatedAtUtc,
    s.SettledAtUtc
FROM dbo.AutomatedCornerBetSelections s
WHERE (@DateFrom IS NULL OR CAST(s.MatchDate AS DATE) >= @DateFrom)
  AND (@DateTo IS NULL OR CAST(s.MatchDate AS DATE) <= @DateTo)
  AND (@Status IS NULL OR s.Status = @Status)
  AND (@League IS NULL OR COALESCE(s.StandardizedLeague, s.League) = @League)
  AND (@OnlyPending = 0 OR s.Status = N'Pending')
ORDER BY
    CASE WHEN s.Status = N'Pending' THEN 0 ELSE 1 END,
    s.MatchDate ASC,
    s.SelectionScore DESC,
    s.AutomatedCornerBetSelectionId DESC;
