SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PartidosProximosCuotas', N'U') IS NULL
    THROW 52310, 'Bot automation read indexes require dbo.PartidosProximosCuotas.', 1;

-- The recommendation runner reads an upcoming date window while the odds
-- refresh is writing. The natural key starts with Source, so it cannot seek
-- this window and previously saturated Azure data I/O during some refreshes.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.PartidosProximosCuotas')
      AND name = N'IX_PartidosProximosCuotas_AutomationWindow'
)
BEGIN
    CREATE INDEX IX_PartidosProximosCuotas_AutomationWindow
        ON dbo.PartidosProximosCuotas(MatchDate, MarketType)
        INCLUDE
        (
            Source,
            SourceMatchId,
            SourceUrl,
            League,
            HomeTeam,
            AwayTeam,
            StandardizedLeague,
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            HomeTeamGender,
            AwayTeamGender,
            LineValue,
            OverOdds,
            UnderOdds,
            UpdatedAtUtc
        );
END;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NULL
    THROW 52311, 'Bot automation read indexes require dbo.AutomatedBotPickEvaluations.', 1;

-- Bot Picks needs a small operational summary by date and market. Keep every
-- field used by that aggregation inside the index so the query never reads the
-- large JSON evidence columns from the base table.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_MonitoringWindow'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_MonitoringWindow
        ON dbo.AutomatedBotPickEvaluations(MatchDate, MarketType, BotKey, Decision)
        INCLUDE
        (
            FixtureIdentity,
            ApiFootballFixtureId,
            HomeTeam,
            AwayTeam,
            PublishedSelectionId,
            EvaluatedAtUtc,
            Explanation
        );
END;
