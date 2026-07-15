CREATE OR ALTER PROCEDURE dbo.sp_GetAutomatedCornerBetSelections
    @DateFrom DATE = NULL,
    @DateTo DATE = NULL,
    @Status NVARCHAR(20) = NULL,
    @League NVARCHAR(200) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @OnlyPending BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

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
        s.SourceMarketType,
        s.MarketType,
        Recommendation = CONCAT(s.SelectedSide, ' ', CONVERT(VARCHAR(20), s.LineValue)),
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
      AND (@MarketType IS NULL OR s.MarketType = @MarketType)
      AND (@OnlyPending = 0 OR s.Status = N'Pending')
    ORDER BY
        CASE WHEN s.Status = N'Pending' THEN 0 ELSE 1 END,
        s.MatchDate ASC,
        s.SelectionScore DESC,
        s.AutomatedCornerBetSelectionId DESC;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateAutomatedCornerBetSelectionStatus
    @AutomatedCornerBetSelectionId BIGINT,
    @Status NVARCHAR(20),
    @ActualHomeCorners INT = NULL,
    @ActualAwayCorners INT = NULL,
    @ActualTotalCorners INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Status NOT IN (N'Pending', N'Won', N'Lost', N'Void')
    BEGIN
        THROW 50000, 'Invalid status. Allowed: Pending, Won, Lost, Void.', 1;
    END;

    UPDATE dbo.AutomatedCornerBetSelections
    SET
        Status = @Status,
        ActualHomeCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualHomeCorners, ActualHomeCorners) END,
        ActualAwayCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualAwayCorners, ActualAwayCorners) END,
        ActualTotalCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualTotalCorners, ActualTotalCorners) END,
        ProfitLoss = CASE
            WHEN @Status = N'Won' THEN ROUND(Stake * (Odds - 1), 2)
            WHEN @Status = N'Lost' THEN ROUND(-Stake, 2)
            WHEN @Status = N'Void' THEN 0
            ELSE NULL
        END,
        YieldPct = CASE
            WHEN @Status = N'Won' THEN ROUND(((Stake * (Odds - 1)) / NULLIF(Stake, 0)) * 100, 4)
            WHEN @Status = N'Lost' THEN -100
            WHEN @Status = N'Void' THEN 0
            ELSE NULL
        END,
        SettledAtUtc = CASE
            WHEN @Status = N'Pending' THEN NULL
            ELSE SYSUTCDATETIME()
        END,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;
END;
