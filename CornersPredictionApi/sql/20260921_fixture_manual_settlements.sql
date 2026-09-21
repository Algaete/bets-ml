IF OBJECT_ID(N'dbo.GeneralBotFixtureManualSettlements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GeneralBotFixtureManualSettlements
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId UNIQUEIDENTIFIER NOT NULL UNIQUE,
        RecordId BIGINT NOT NULL,
        ApiFootballFixtureId BIGINT NULL,
        MatchDate DATETIME2(0) NOT NULL,
        League NVARCHAR(200) NOT NULL,
        HomeTeam NVARCHAR(150) NOT NULL,
        AwayTeam NVARCHAR(150) NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        ActualValue INT NOT NULL CHECK (ActualValue BETWEEN 0 AND 1000),
        OutcomeStatus NVARCHAR(20) NOT NULL,
        ProfitLoss DECIMAL(12,4) NOT NULL,
        Reason NVARCHAR(1000) NOT NULL,
        SettledBy NVARCHAR(256) NOT NULL,
        SettledAtUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        AffectedEvaluations INT NOT NULL,
        AffectedPublishedPicks INT NOT NULL,
        AffectedBots INT NOT NULL
    );
    CREATE INDEX IX_GeneralFixtureManual_Fixture
        ON dbo.GeneralBotFixtureManualSettlements(ApiFootballFixtureId, MarketType, Id DESC);
    CREATE INDEX IX_GeneralFixtureManual_Identity
        ON dbo.GeneralBotFixtureManualSettlements(MatchDate, MarketType, Id DESC)
        INCLUDE (ApiFootballFixtureId, League, HomeTeam, AwayTeam);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
    AND name=N'IX_AutomatedBotPickEvaluations_ManualFixture')
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_ManualFixture
        ON dbo.AutomatedBotPickEvaluations(ApiFootballFixtureId, MarketType)
        WHERE ApiFootballFixtureId IS NOT NULL WITH (MAXDOP=1);
END;
GO

-- An exact fixture id wins. Missing ids require exact kickoff/league/home/away;
-- different known fixture ids are never merged. No team-name-only matching.
CREATE OR ALTER FUNCTION dbo.fn_GeneralBotFixtureManualOutcome
(
    @FixtureId BIGINT, @MatchDate DATETIME2(0), @League NVARCHAR(200),
    @HomeTeam NVARCHAR(150), @AwayTeam NVARCHAR(150), @MarketType NVARCHAR(50)
)
RETURNS TABLE
AS RETURN
(
    SELECT TOP (1) m.Id, m.RequestId, m.ActualValue, m.Reason, m.SettledBy, m.SettledAtUtc
    FROM dbo.GeneralBotFixtureManualSettlements AS m
    WHERE m.MarketType = @MarketType
      AND
      (
          (@FixtureId > 0 AND m.ApiFootballFixtureId = @FixtureId)
          OR
          ((NULLIF(@FixtureId, 0) IS NULL OR m.ApiFootballFixtureId IS NULL)
           AND m.MatchDate = @MatchDate
           AND m.League COLLATE Latin1_General_100_CI_AI = LTRIM(RTRIM(@League)) COLLATE Latin1_General_100_CI_AI
           AND m.HomeTeam COLLATE Latin1_General_100_CI_AI = LTRIM(RTRIM(@HomeTeam)) COLLATE Latin1_General_100_CI_AI
           AND m.AwayTeam COLLATE Latin1_General_100_CI_AI = LTRIM(RTRIM(@AwayTeam)) COLLATE Latin1_General_100_CI_AI)
      )
    ORDER BY m.Id DESC
);
GO

CREATE OR ALTER PROCEDURE dbo.sp_ApplyGeneralBotFixtureManualSettlement
    @SettlementId BIGINT = NULL,
    @NowLocal DATETIME2(0)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.GeneralBotFixtureManualSettlements)
    BEGIN
        SELECT 0;
        RETURN;
    END;
    UPDATE s SET
        ActualHomeCorners = CASE WHEN s.MarketType LIKE N'HomeTeam%' THEN m.ActualValue ELSE s.ActualHomeCorners END,
        ActualAwayCorners = CASE WHEN s.MarketType LIKE N'AwayTeam%' THEN m.ActualValue ELSE s.ActualAwayCorners END,
        ActualTotalCorners = CASE WHEN s.MarketType LIKE N'Total%' THEN m.ActualValue ELSE s.ActualTotalCorners END,
        SettlementActualValue = m.ActualValue,
        SettlementFactor = outcome.Factor,
        SettlementSource = N'Manual',
        SettlementReason = LEFT(CONCAT(N'Resultado manual compartido: ', m.Reason), 500),
        SettlementSnapshotJson = CONCAT(N'{"source":"Manual","fixtureSettlementId":', m.Id,
            N',"actualValue":', m.ActualValue, N',"factor":', outcome.Factor, N'}'),
        Status = CASE WHEN outcome.Factor > 0 THEN N'Won' WHEN outcome.Factor < 0 THEN N'Lost' ELSE N'Push' END,
        ProfitLoss = ROUND(s.Stake * CASE WHEN outcome.Factor > 0 THEN (s.Odds - 1) * outcome.Factor ELSE outcome.Factor END, 2),
        YieldPct = CASE WHEN s.Stake = 0 THEN NULL WHEN outcome.Factor > 0 THEN (s.Odds - 1) * outcome.Factor ELSE outcome.Factor END,
        SettledAtUtc = m.SettledAtUtc,
        LastSettlementCheckReason = N'Resultado manual compartido entre bots del mismo partido y mercado.',
        LastSettlementCheckAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME()
    FROM dbo.AutomatedCornerBetSelections AS s
    CROSS APPLY dbo.fn_GeneralBotFixtureManualOutcome(s.ApiFootballFixtureId, s.MatchDate,
        COALESCE(NULLIF(s.StandardizedLeague, N''), s.League),
        COALESCE(NULLIF(s.StandardizedHomeTeam, N''), s.HomeTeam),
        COALESCE(NULLIF(s.StandardizedAwayTeam, N''), s.AwayTeam), s.MarketType) AS m
    CROSS APPLY (SELECT Factor = CONVERT(DECIMAL(6,3),
        CASE
            WHEN s.LineValue - FLOOR(s.LineValue) = 0.25 AND m.ActualValue = FLOOR(s.LineValue)
                THEN CASE WHEN s.SelectedSide = N'Over' THEN -0.5 ELSE 0.5 END
            WHEN s.LineValue - FLOOR(s.LineValue) = 0.75 AND m.ActualValue = CEILING(s.LineValue)
                THEN CASE WHEN s.SelectedSide = N'Over' THEN 0.5 ELSE -0.5 END
            WHEN m.ActualValue = s.LineValue THEN 0
            WHEN s.SelectedSide = N'Over' AND m.ActualValue > s.LineValue THEN 1
            WHEN s.SelectedSide = N'Under' AND m.ActualValue < s.LineValue THEN 1 ELSE -1 END)) AS outcome
    WHERE ((@SettlementId IS NULL AND s.Status = N'Pending') OR m.Id = @SettlementId)
      AND s.MatchDate <= @NowLocal AND s.LineValue >= 0 AND s.Odds > 1 AND s.Stake >= 0
      AND s.SelectedSide IN (N'Over', N'Under')
      AND (s.SettlementSource IS NULL OR s.SettlementSource <> N'Manual'
           OR s.SettledAtUtc <= CONVERT(DATETIME2(0),m.SettledAtUtc));
    SELECT @@ROWCOUNT;
END;
GO
