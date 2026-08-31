SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.FootballNewsDocument', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FootballNewsDocument
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FootballNewsDocument PRIMARY KEY,
        FixtureId BIGINT NOT NULL,
        TeamId INT NULL,
        Url NVARCHAR(2048) NOT NULL,
        CanonicalUrl NVARCHAR(2048) NULL,
        UrlHash CHAR(64) NOT NULL,
        ContentHash CHAR(64) NULL,
        SourceDomain NVARCHAR(255) NOT NULL,
        SourceTier NVARCHAR(40) NOT NULL,
        Title NVARCHAR(1000) NOT NULL,
        Author NVARCHAR(300) NULL,
        LanguageCode NVARCHAR(20) NULL,
        PublishedAtUtc DATETIME2(0) NULL,
        UpdatedAtUtc DATETIME2(0) NULL,
        FirstSeenAtUtc DATETIME2(0) NOT NULL,
        RetrievedAtUtc DATETIME2(0) NOT NULL,
        NormalizedText NVARCHAR(MAX) NULL,
        ExtractionStatus NVARCHAR(40) NOT NULL,
        HttpStatusCode INT NULL,
        ErrorMessage NVARCHAR(2000) NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballNewsDocument_Created DEFAULT SYSUTCDATETIME(),
        RowUpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballNewsDocument_Updated DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.FootballNewsFact', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FootballNewsFact
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FootballNewsFact PRIMARY KEY,
        NewsDocumentId BIGINT NOT NULL,
        FactHash CHAR(64) NOT NULL,
        FixtureId BIGINT NOT NULL,
        TeamId INT NULL,
        PlayerId INT NULL,
        TeamNameExtracted NVARCHAR(200) NOT NULL,
        PlayerNameExtracted NVARCHAR(200) NULL,
        PositionCode NVARCHAR(20) NULL,
        EventType NVARCHAR(50) NOT NULL,
        AvailabilityStatus NVARCHAR(50) NOT NULL,
        Certainty NVARCHAR(30) NOT NULL,
        ProbabilityAvailable DECIMAL(9,6) NULL,
        ExpectedMinutesDelta DECIMAL(9,4) NULL,
        Reason NVARCHAR(1000) NULL,
        EvidenceSnippet NVARCHAR(1000) NOT NULL,
        EventEffectiveAtUtc DATETIME2(0) NULL,
        ExpectedReturnAtUtc DATETIME2(0) NULL,
        FixtureRelevance DECIMAL(9,6) NOT NULL,
        ExtractionConfidence DECIMAL(9,6) NOT NULL,
        SourceConfidence DECIMAL(9,6) NOT NULL,
        EffectiveConfidence DECIMAL(9,6) NOT NULL,
        ResolutionStatus NVARCHAR(40) NOT NULL,
        IsCurrent BIT NOT NULL CONSTRAINT DF_FootballNewsFact_IsCurrent DEFAULT 1,
        SupersededByFactId BIGINT NULL,
        ExtractionModel NVARCHAR(100) NOT NULL,
        PromptVersion NVARCHAR(100) NOT NULL,
        IsCurrentExtraction BIT NOT NULL CONSTRAINT DF_FootballNewsFact_CurrentExtraction DEFAULT 1,
        FirstSeenAtUtc DATETIME2(0) NOT NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballNewsFact_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_FootballNewsFact_Document FOREIGN KEY (NewsDocumentId)
            REFERENCES dbo.FootballNewsDocument(Id),
        CONSTRAINT FK_FootballNewsFact_Superseded FOREIGN KEY (SupersededByFactId)
            REFERENCES dbo.FootballNewsFact(Id),
        CONSTRAINT CK_FootballNewsFact_Probability CHECK
            (ProbabilityAvailable IS NULL OR ProbabilityAvailable BETWEEN 0 AND 1),
        CONSTRAINT CK_FootballNewsFact_Confidence CHECK
            (FixtureRelevance BETWEEN 0 AND 1 AND ExtractionConfidence BETWEEN 0 AND 1
             AND SourceConfidence BETWEEN 0 AND 1 AND EffectiveConfidence BETWEEN 0 AND 1)
    );
END;

IF OBJECT_ID(N'dbo.FootballNewsFactResolution', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FootballNewsFactResolution
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FootballNewsFactResolution PRIMARY KEY,
        NewsFactId BIGINT NOT NULL,
        ResolutionStatus NVARCHAR(40) NOT NULL,
        ResolvedTeamId INT NULL,
        ResolvedPlayerId INT NULL,
        MatchedName NVARCHAR(200) NULL,
        Confidence DECIMAL(9,6) NOT NULL,
        ResolverVersion NVARCHAR(100) NOT NULL,
        IsCurrent BIT NOT NULL CONSTRAINT DF_FootballNewsFactResolution_Current DEFAULT 1,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballNewsFactResolution_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_FootballNewsFactResolution_Fact FOREIGN KEY (NewsFactId)
            REFERENCES dbo.FootballNewsFact(Id),
        CONSTRAINT CK_FootballNewsFactResolution_Confidence CHECK (Confidence BETWEEN 0 AND 1)
    );
END;

IF OBJECT_ID(N'dbo.FootballSourceConfiguration', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FootballSourceConfiguration
    (
        Domain NVARCHAR(255) NOT NULL CONSTRAINT PK_FootballSourceConfiguration PRIMARY KEY,
        SourceTier NVARCHAR(40) NOT NULL,
        ConfidenceWeight DECIMAL(9,6) NOT NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_FootballSourceConfiguration_Enabled DEFAULT 1,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballSourceConfiguration_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballSourceConfiguration_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_FootballSourceConfiguration_Weight CHECK (ConfidenceWeight BETWEEN 0 AND 1)
    );
END;

IF OBJECT_ID(N'dbo.FootballTeamAlias', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FootballTeamAlias
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FootballTeamAlias PRIMARY KEY,
        TeamId INT NOT NULL,
        Alias NVARCHAR(200) NOT NULL,
        NormalizedAlias NVARCHAR(200) NOT NULL,
        LanguageCode NVARCHAR(20) NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_FootballTeamAlias_Enabled DEFAULT 1,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballTeamAlias_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballTeamAlias_Updated DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.FootballPlayerAlias', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FootballPlayerAlias
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FootballPlayerAlias PRIMARY KEY,
        PlayerId INT NOT NULL,
        TeamId INT NULL,
        Alias NVARCHAR(200) NOT NULL,
        NormalizedAlias NVARCHAR(200) NOT NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_FootballPlayerAlias_Enabled DEFAULT 1,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballPlayerAlias_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballPlayerAlias_Updated DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.MatchTeamIntelligenceSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MatchTeamIntelligenceSnapshot
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MatchTeamIntelligenceSnapshot PRIMARY KEY,
        FixtureId BIGINT NOT NULL,
        TeamId INT NOT NULL,
        IsHomeTeam BIT NOT NULL,
        CutoffAtUtc DATETIME2(0) NOT NULL,
        KickoffAtUtc DATETIME2(0) NOT NULL,
        DocumentCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Documents DEFAULT 0,
        IndependentSourceCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Sources DEFAULT 0,
        ActionableFactCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Facts DEFAULT 0,
        StructuredEvidenceCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Structured DEFAULT 0,
        ConfirmedOutCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Out DEFAULT 0,
        DoubtfulCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Doubtful DEFAULT 0,
        SuspendedCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Suspended DEFAULT 0,
        MissingStarterMinutesPct DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_MissingStarter DEFAULT 0,
        MissingAttackMinutesPct DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_MissingAttack DEFAULT 0,
        MissingMidfieldMinutesPct DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_MissingMidfield DEFAULT 0,
        MissingDefenceMinutesPct DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_MissingDefence DEFAULT 0,
        AttackAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Attack DEFAULT 0,
        MidfieldAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Midfield DEFAULT 0,
        DefenceAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Defence DEFAULT 0,
        GoalkeeperAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Gk DEFAULT 0,
        WidthAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Width DEFAULT 0,
        SetPieceAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_SetPiece DEFAULT 0,
        RotationRisk DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Rotation DEFAULT 0,
        FatigueRisk DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Fatigue DEFAULT 0,
        MoraleSignal DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Morale DEFAULT 0,
        CoachChangeDays INT NULL,
        ExpectedFormation NVARCHAR(30) NULL,
        FormationChangeExpected BIT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_FormationChange DEFAULT 0,
        OfficialLineupAvailable BIT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Lineup DEFAULT 0,
        ExpectedXiChanges INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_XiChanges DEFAULT 0,
        OverallNewsConfidence DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Confidence DEFAULT 0,
        ConflictCount INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Conflicts DEFAULT 0,
        SnapshotAgeMinutes INT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Age DEFAULT 0,
        MissingWingerMinutesPct DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Winger DEFAULT 0,
        MissingFullBackMinutesPct DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_FullBack DEFAULT 0,
        MissingCornerTakerShare DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_CornerTaker DEFAULT 0,
        MissingCrossShare DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Cross DEFAULT 0,
        CornerCreationImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_CornerImpact DEFAULT 0,
        MissingShotShare DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_ShotShare DEFAULT 0,
        MissingCreatorShare DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Creator DEFAULT 0,
        ShotGenerationImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_ShotImpact DEFAULT 0,
        MissingSotShare DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Sot DEFAULT 0,
        FinishingAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Finishing DEFAULT 0,
        MissingGoalShare DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_GoalShare DEFAULT 0,
        PenaltyTakerMissing BIT NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Penalty DEFAULT 0,
        GoalScoringAvailabilityImpact DECIMAL(9,6) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_GoalImpact DEFAULT 0,
        RiskFlagsJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Risks DEFAULT N'[]',
        DetailJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Detail DEFAULT N'{}',
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_IntelligenceSnapshot_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_MatchTeamIntelligenceSnapshot UNIQUE (FixtureId, TeamId, CutoffAtUtc),
        CONSTRAINT CK_IntelligenceSnapshot_Counts CHECK
            (DocumentCount >= 0 AND IndependentSourceCount >= 0 AND ActionableFactCount >= 0
             AND StructuredEvidenceCount >= 0 AND ConflictCount >= 0),
        CONSTRAINT CK_IntelligenceSnapshot_Confidence CHECK (OverallNewsConfidence BETWEEN 0 AND 1),
        CONSTRAINT CK_IntelligenceSnapshot_Json CHECK (ISJSON(RiskFlagsJson) = 1 AND ISJSON(DetailJson) = 1)
    );
END;

IF OBJECT_ID(N'dbo.MatchIntelligenceRun', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MatchIntelligenceRun
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MatchIntelligenceRun PRIMARY KEY,
        FixtureId BIGINT NOT NULL,
        CutoffAtUtc DATETIME2(0) NOT NULL,
        StartedAtUtc DATETIME2(0) NOT NULL,
        FinishedAtUtc DATETIME2(0) NULL,
        Status NVARCHAR(50) NOT NULL,
        QueriesGenerated INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Queries DEFAULT 0,
        SearchResults INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Search DEFAULT 0,
        DocumentsDownloaded INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Downloaded DEFAULT 0,
        DocumentsProcessed INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Processed DEFAULT 0,
        FactsExtracted INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Facts DEFAULT 0,
        ResolvedFacts INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Resolved DEFAULT 0,
        UnresolvedFacts INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Unresolved DEFAULT 0,
        ConflictCount INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Conflicts DEFAULT 0,
        ApiCost DECIMAL(12,6) NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Cost DEFAULT 0,
        LlmTokensInput INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Input DEFAULT 0,
        LlmTokensOutput INT NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Output DEFAULT 0,
        ErrorMessage NVARCHAR(2000) NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_MatchIntelligenceRun_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_MatchIntelligenceRun UNIQUE (FixtureId, CutoffAtUtc)
    );
END;

IF OBJECT_ID(N'dbo.PlayerMarketImportance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlayerMarketImportance
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlayerMarketImportance PRIMARY KEY,
        PlayerId INT NOT NULL,
        TeamId INT NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        CalculatedAtUtc DATETIME2(0) NOT NULL,
        CutoffAtUtc DATETIME2(0) NOT NULL,
        StartRate DECIMAL(9,6) NOT NULL,
        MinutesShare DECIMAL(9,6) NOT NULL,
        RecentMinutesShare DECIMAL(9,6) NOT NULL,
        MarketContribution DECIMAL(9,6) NOT NULL,
        SetPieceShare DECIMAL(9,6) NOT NULL,
        Importance DECIMAL(9,6) NOT NULL,
        SampleSize INT NOT NULL,
        DetailJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PlayerMarketImportance_Detail DEFAULT N'{}',
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_PlayerMarketImportance_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_PlayerMarketImportance UNIQUE (PlayerId, TeamId, MarketType, CutoffAtUtc),
        CONSTRAINT CK_PlayerMarketImportance_Detail CHECK (ISJSON(DetailJson) = 1)
    );
END;

IF OBJECT_ID(N'dbo.PlayerAvailabilityImpactHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlayerAvailabilityImpactHistory
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlayerAvailabilityImpactHistory PRIMARY KEY,
        PlayerId INT NOT NULL,
        TeamId INT NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        CutoffAtUtc DATETIME2(0) NOT NULL,
        StartingSampleSize INT NOT NULL,
        AbsentSampleSize INT NOT NULL,
        RawDelta DECIMAL(12,6) NOT NULL,
        AdjustedDelta DECIMAL(12,6) NOT NULL,
        ShrinkageStrength DECIMAL(12,6) NOT NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_PlayerAvailabilityImpactHistory_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_PlayerAvailabilityImpactHistory UNIQUE (PlayerId, TeamId, MarketType, CutoffAtUtc)
    );
END;

IF OBJECT_ID(N'dbo.FootballSourcePerformance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FootballSourcePerformance
    (
        Domain NVARCHAR(255) NOT NULL CONSTRAINT PK_FootballSourcePerformance PRIMARY KEY,
        FactsEvaluated BIGINT NOT NULL CONSTRAINT DF_FootballSourcePerformance_Evaluated DEFAULT 0,
        CorrectFacts BIGINT NOT NULL CONSTRAINT DF_FootballSourcePerformance_Correct DEFAULT 0,
        IncorrectFacts BIGINT NOT NULL CONSTRAINT DF_FootballSourcePerformance_Incorrect DEFAULT 0,
        AvailabilityAccuracy DECIMAL(9,6) NULL,
        AverageLeadTimeMinutes DECIMAL(12,2) NULL,
        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_FootballSourcePerformance_Updated DEFAULT SYSUTCDATETIME()
    );
END;
