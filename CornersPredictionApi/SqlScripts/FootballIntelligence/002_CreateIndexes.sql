SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballNewsDocument') AND name = N'UX_FootballNewsDocument_Fixture_UrlHash')
    CREATE UNIQUE INDEX UX_FootballNewsDocument_Fixture_UrlHash
        ON dbo.FootballNewsDocument(FixtureId, TeamId, UrlHash);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballNewsDocument') AND name = N'IX_FootballNewsDocument_Cutoff')
    CREATE INDEX IX_FootballNewsDocument_Cutoff
        ON dbo.FootballNewsDocument(FixtureId, FirstSeenAtUtc, PublishedAtUtc)
        INCLUDE (TeamId, SourceDomain, SourceTier, ContentHash, ExtractionStatus);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballNewsDocument') AND name = N'IX_FootballNewsDocument_ContentHash')
    CREATE INDEX IX_FootballNewsDocument_ContentHash
        ON dbo.FootballNewsDocument(ContentHash, FixtureId)
        WHERE ContentHash IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballNewsFact') AND name = N'IX_FootballNewsFact_FixtureCutoff')
    CREATE INDEX IX_FootballNewsFact_FixtureCutoff
        ON dbo.FootballNewsFact(FixtureId, FirstSeenAtUtc, IsCurrent, IsCurrentExtraction)
        INCLUDE (TeamId, PlayerId, EventType, AvailabilityStatus, EffectiveConfidence, NewsDocumentId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballNewsFact') AND name = N'IX_FootballNewsFact_Player')
    CREATE INDEX IX_FootballNewsFact_Player
        ON dbo.FootballNewsFact(PlayerId, FixtureId, IsCurrent)
        INCLUDE (TeamId, EventType, AvailabilityStatus, EffectiveConfidence, EventEffectiveAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballNewsFactResolution') AND name = N'IX_FootballNewsFactResolution_Current')
    CREATE INDEX IX_FootballNewsFactResolution_Current
        ON dbo.FootballNewsFactResolution(NewsFactId, IsCurrent)
        INCLUDE (ResolutionStatus, ResolvedTeamId, ResolvedPlayerId, Confidence);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballTeamAlias') AND name = N'UX_FootballTeamAlias_Normalized')
    CREATE UNIQUE INDEX UX_FootballTeamAlias_Normalized
        ON dbo.FootballTeamAlias(TeamId, NormalizedAlias);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballTeamAlias') AND name = N'IX_FootballTeamAlias_Lookup')
    CREATE INDEX IX_FootballTeamAlias_Lookup
        ON dbo.FootballTeamAlias(NormalizedAlias, IsEnabled)
        INCLUDE (TeamId, Alias);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballPlayerAlias') AND name = N'UX_FootballPlayerAlias_Normalized')
    CREATE UNIQUE INDEX UX_FootballPlayerAlias_Normalized
        ON dbo.FootballPlayerAlias(PlayerId, NormalizedAlias);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FootballPlayerAlias') AND name = N'IX_FootballPlayerAlias_Lookup')
    CREATE INDEX IX_FootballPlayerAlias_Lookup
        ON dbo.FootballPlayerAlias(NormalizedAlias, IsEnabled)
        INCLUDE (PlayerId, TeamId, Alias);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MatchTeamIntelligenceSnapshot') AND name = N'IX_IntelligenceSnapshot_Latest')
    CREATE INDEX IX_IntelligenceSnapshot_Latest
        ON dbo.MatchTeamIntelligenceSnapshot(FixtureId, CutoffAtUtc DESC, TeamId)
        INCLUDE (IsHomeTeam, KickoffAtUtc, ActionableFactCount, IndependentSourceCount, OverallNewsConfidence,
                 SnapshotAgeMinutes, AttackAvailabilityImpact, DefenceAvailabilityImpact,
                 WidthAvailabilityImpact, SetPieceAvailabilityImpact, ConflictCount);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MatchIntelligenceRun') AND name = N'IX_MatchIntelligenceRun_Status')
    CREATE INDEX IX_MatchIntelligenceRun_Status
        ON dbo.MatchIntelligenceRun(Status, StartedAtUtc)
        INCLUDE (FixtureId, CutoffAtUtc, FinishedAtUtc, ErrorMessage);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PlayerMarketImportance') AND name = N'IX_PlayerMarketImportance_Latest')
    CREATE INDEX IX_PlayerMarketImportance_Latest
        ON dbo.PlayerMarketImportance(PlayerId, TeamId, MarketType, CutoffAtUtc DESC)
        INCLUDE (Importance, StartRate, MinutesShare, RecentMinutesShare, MarketContribution, SetPieceShare, SampleSize);
