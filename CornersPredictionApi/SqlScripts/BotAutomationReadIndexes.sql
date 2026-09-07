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

-- Research pages sort all C/D/E/F/H candidates by fixture date and audit id.
-- Keep the narrow count/page-key phase covered; JSON/features and official
-- outcomes are intentionally fetched from the base tables only for one page.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_ResearchPage'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_ResearchPage
        ON dbo.AutomatedBotPickEvaluations
           (MatchDate DESC, AutomatedBotPickEvaluationId DESC)
        INCLUDE
        (
            BotKey,
            MarketType,
            Decision,
            PublicationStatus,
            Published,
            PublishedSelectionId,
            ProductionDecision,
            IsResearchWinner
        );
END;

-- Scientific scorecards rank thousands of eligible C-F decisions. The general
-- BotDecisionDate index does not cover that ledger, so its key lookups read the
-- much larger feature/audit JSON rows. Restrict this index to the same valid
-- candidate predicates and include only ranking fields plus the legacy reason
-- JSON. Outcome and descriptive fields are still fetched after winner ranking.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_PerformanceCandidates'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_PerformanceCandidates
        ON dbo.AutomatedBotPickEvaluations(BotKey, Decision, MatchDate)
        INCLUDE
        (
            RunId,
            AutomationVersion,
            ApiFootballFixtureId,
            MarketType,
            SelectedSide,
            SelectedOdds,
            FinalProbability,
            FinalEdge,
            MarketNoVigProbability,
            RawImpliedProbability,
            FinalExpectedValue,
            SelectionScore,
            RuleBasedConfidenceScore,
            IsResearchWinner,
            PredictionTimestampUtc,
            EvaluatedAtUtc,
            DecisionReasonsJson
        )
        WHERE BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026')
          AND Decision IN (N'Approved', N'Rejected')
          AND ApiFootballFixtureId > 0
          AND SelectedSide IN (N'Over', N'Under')
          AND SelectedOdds > 1
          AND FinalProbability > 0
          AND FinalProbability < 1;
END;

-- Winners need descriptive fields and probabilities after the ranking stage.
-- Keep that projection separate from the large audit/feature JSON base rows.
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_PerformanceWinnerDetails'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_PerformanceWinnerDetails
        ON dbo.AutomatedBotPickEvaluations(AutomatedBotPickEvaluationId)
        INCLUDE
        (
            BotKey, AutomationVersion, ApiFootballFixtureId, MatchDate,
            League, HomeTeam, AwayTeam, Bookmaker, Source, MarketType,
            SelectedSide, LineValue, SelectedOdds, StakeUnits,
            FinalProbability, MarketNoVigProbability, RawImpliedProbability,
            FinalEdge, PredictionTimestampUtc, EvaluatedAtUtc
        )
        WHERE BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026')
          AND ApiFootballFixtureId > 0;
END;

-- Preserve the original LIKE predicates exactly, including their collation and
-- wildcard semantics, but compute the legacy compatibility flag only on writes.
-- A persisted flag also lets the legacy branch seek the rare matching rows
-- instead of evaluating both patterns over every otherwise valid rejection.
IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'PerformanceLegacyPublicationRejection') IS NULL
BEGIN
    EXEC(N'
        ALTER TABLE dbo.AutomatedBotPickEvaluations
        ADD PerformanceLegacyPublicationRejection AS
            CONVERT(BIT, CASE
                WHEN DecisionReasonsJson LIKE N''%REJECTED_PRODUCTION_GATE%''
                  OR DecisionReasonsJson LIKE N''%REJECTED_LOWER_RANKED_CANDIDATE%''
                    THEN 1
                ELSE 0
            END) PERSISTED;');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_PerformanceLegacyCandidates'
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_AutomatedBotPickEvaluations_PerformanceLegacyCandidates
            ON dbo.AutomatedBotPickEvaluations
                (BotKey, PerformanceLegacyPublicationRejection, MatchDate)
            INCLUDE
            (
                RunId,
                AutomationVersion,
                ApiFootballFixtureId,
                MarketType,
                SelectedSide,
                SelectedOdds,
                FinalProbability,
                FinalEdge,
                MarketNoVigProbability,
                RawImpliedProbability,
                FinalExpectedValue,
                SelectionScore,
                RuleBasedConfidenceScore,
                IsResearchWinner,
                PredictionTimestampUtc,
                EvaluatedAtUtc
            )
            WHERE BotKey IN (N''C2026'', N''D2026'', N''E2026'', N''F2026'')
              AND Decision = N''Rejected''
              AND ApiFootballFixtureId > 0
              AND SelectedSide IN (N''Over'', N''Under'')
              AND SelectedOdds > 1
              AND FinalProbability > 0
              AND FinalProbability < 1;');
END;

-- General Bot Picks merges the audit and archived published selections. Seek
-- linked ids once per published pick instead of scanning the complete audit.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_PublishedSelectionLookup'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_PublishedSelectionLookup
        ON dbo.AutomatedBotPickEvaluations(PublishedSelectionId)
        INCLUDE (BotKey)
        WHERE PublishedSelectionId IS NOT NULL;
END;

-- Filtered General Bot Picks reads compatibility metadata before selectively
-- fetching reason JSON for legacy publication rejections.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.index_columns AS ic
      ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    INNER JOIN sys.columns AS c
      ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND i.name = N'IX_AutomatedBotPickEvaluations_ResearchPage'
      AND c.name = N'PerformanceLegacyPublicationRejection'
)
BEGIN
    EXEC(N'
    CREATE INDEX IX_AutomatedBotPickEvaluations_ResearchPage
        ON dbo.AutomatedBotPickEvaluations
           (MatchDate DESC, AutomatedBotPickEvaluationId DESC)
        INCLUDE
        (
            BotKey,
            MarketType,
            Decision,
            PublicationStatus,
            Published,
            PublishedSelectionId,
            ProductionDecision,
            IsResearchWinner,
            PerformanceLegacyPublicationRejection
        )
        WITH (DROP_EXISTING = ON);');
END;

-- Column sorting must seek the same small date index as the default listing.
-- Cover numerical values and outcome identities without including audit JSON.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
    AND name = N'IX_AutomatedBotPickEvaluations_ResearchPage')
AND COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'PerformanceLegacyPublicationRejection') IS NOT NULL
AND NOT EXISTS
(
    SELECT 1 FROM sys.indexes AS i
    INNER JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    INNER JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND i.name=N'IX_AutomatedBotPickEvaluations_ResearchPage' AND c.name=N'SelectedOdds'
)
BEGIN
    EXEC(N'CREATE INDEX IX_AutomatedBotPickEvaluations_ResearchPage
        ON dbo.AutomatedBotPickEvaluations(MatchDate DESC, AutomatedBotPickEvaluationId DESC)
        INCLUDE (BotKey, MarketType, Decision, PublicationStatus, Published, PublishedSelectionId,
            ProductionDecision, IsResearchWinner, PerformanceLegacyPublicationRejection,
            SelectedOdds, LineValue, SelectedSide, FinalProbability, FinalEdge, FinalExpectedValue,
            SelectionScore, GSelectionScore, RuleBasedConfidenceScore,
            ApiFootballFixtureId, PredictionTimestampUtc, EvaluatedAtUtc, HomeTeam, AwayTeam)
        WITH (DROP_EXISTING = ON, ONLINE = ON);');
END;

-- The default General Bot Picks view contains model-approved rows regardless
-- of production status. Lead with Decision so this common path reads only that
-- subset instead of the complete monthly audit.
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_GeneralDecisionPage'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_GeneralDecisionPage
        ON dbo.AutomatedBotPickEvaluations
           (Decision, MatchDate DESC, AutomatedBotPickEvaluationId DESC)
        INCLUDE
        (
            BotKey, MarketType, PublicationStatus, Published, PublishedSelectionId,
            ProductionDecision, IsResearchWinner, PerformanceLegacyPublicationRejection,
            SelectedOdds, LineValue, SelectedSide, FinalProbability, FinalEdge, FinalExpectedValue,
            SelectionScore, GSelectionScore, RuleBasedConfidenceScore,
            ApiFootballFixtureId, PredictionTimestampUtc, EvaluatedAtUtc, HomeTeam, AwayTeam
        )
        WITH (ONLINE = ON);
END;
