IF OBJECT_ID(N'dbo.AutomatedCornerBetSelections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedCornerBetSelections
    (
        AutomatedCornerBetSelectionId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AutomatedCornerBetSelections PRIMARY KEY,
        RunId UNIQUEIDENTIFIER NOT NULL,
        BotKey NVARCHAR(50) NOT NULL,
        LogicalPickKey VARBINARY(32) NOT NULL,
        AutomationVersion NVARCHAR(50) NOT NULL,
        Source NVARCHAR(50) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_Source DEFAULT N'Betano',
        SourceMatchId NVARCHAR(100) NULL,
        ApiFootballFixtureId BIGINT NULL,
        MatchHistoryId BIGINT NULL,
        SourceUrl NVARCHAR(500) NULL,
        MatchDate DATETIME2(0) NOT NULL,
        League NVARCHAR(200) NOT NULL,
        StandardizedLeague NVARCHAR(200) NULL,
        HomeTeam NVARCHAR(150) NOT NULL,
        AwayTeam NVARCHAR(150) NOT NULL,
        StandardizedHomeTeam NVARCHAR(150) NULL,
        StandardizedAwayTeam NVARCHAR(150) NULL,
        HomeTeamGender CHAR(1) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_HomeTeamGender DEFAULT 'M',
        AwayTeamGender CHAR(1) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_AwayTeamGender DEFAULT 'M',
        SourceMarketType NVARCHAR(50) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_SourceMarketType DEFAULT N'CornersTotal',
        MarketType NVARCHAR(50) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_MarketType DEFAULT N'TotalCorners',
        LineValue DECIMAL(6,2) NOT NULL,
        SelectedSide NVARCHAR(10) NOT NULL,
        Odds DECIMAL(10,2) NOT NULL,
        Stake DECIMAL(10,2) NOT NULL,
        FlatStake DECIMAL(10,2) NULL,
        ImpliedProbability DECIMAL(9,6) NULL,
        ModelProbability DECIMAL(9,6) NULL,
        ProbabilityEdge DECIMAL(9,6) NULL,
        ExpectedValue DECIMAL(9,6) NULL,
        KellyFraction DECIMAL(9,6) NULL,
        SelectionScore DECIMAL(9,6) NULL,
        PredictedTotalCorners DECIMAL(9,4) NULL,
        PredTotalDirect DECIMAL(9,4) NULL,
        PredHomeCorners DECIMAL(9,4) NULL,
        PredAwayCorners DECIMAL(9,4) NULL,
        PredTotalCombined DECIMAL(9,4) NULL,
        DistanceToLine DECIMAL(9,4) NULL,
        ConfidenceLevel NVARCHAR(20) NULL,
        OverUnderConfidenceLevel NVARCHAR(20) NULL,
        ModelConsensus NVARCHAR(20) NULL,
        ContextTotalCorners DECIMAL(9,4) NULL,
        ContextDifference DECIMAL(9,4) NULL,
        RecommendedSide NVARCHAR(10) NULL,
        Status NVARCHAR(20) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_Status DEFAULT N'Pending',
        ActualHomeCorners INT NULL,
        ActualAwayCorners INT NULL,
        ActualTotalCorners INT NULL,
        SettlementActualValue INT NULL,
        SettlementFactor DECIMAL(6,3) NULL,
        SettlementReason NVARCHAR(500) NULL,
        SettlementSource NVARCHAR(50) NULL,
        SettlementMatchStatus NVARCHAR(20) NULL,
        SettlementSnapshotJson NVARCHAR(MAX) NULL,
        LastSettlementCheckReason NVARCHAR(500) NULL,
        LastSettlementCheckAtUtc DATETIME2(0) NULL,
        ProfitLoss DECIMAL(10,2) NULL,
        YieldPct DECIMAL(9,4) NULL,
        DecisionReason NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        SettledAtUtc DATETIME2(0) NULL,
        CONSTRAINT CK_AutomatedCornerBetSelections_Side CHECK (SelectedSide IN (N'Over', N'Under')),
        CONSTRAINT CK_AutomatedCornerBetSelections_Status CHECK (Status IN (N'Pending', N'Won', N'Lost', N'Push', N'Void'))
    );
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedBotDefinitions
    (
        BotKey NVARCHAR(50) NOT NULL
            CONSTRAINT PK_AutomatedBotDefinitions PRIMARY KEY,
        DisplayName NVARCHAR(120) NOT NULL,
        Description NVARCHAR(1000) NOT NULL,
        BaseStrategy NVARCHAR(30) NOT NULL,
        IsEnabled BIT NOT NULL,
        PublishEnabled BIT NOT NULL
            CONSTRAINT DF_AutomatedBotDefinitions_PublishEnabled DEFAULT (1),
        IsBuiltIn BIT NOT NULL,
        MarketFamilies NVARCHAR(200) NOT NULL,
        MinEdge FLOAT NULL,
        MinExpectedValue FLOAT NULL,
        MinDistanceToLine FLOAT NULL,
        MaxContextDifference FLOAT NULL,
        AllowModelDisagreement BIT NULL,
        MinOddsExclusive FLOAT NULL,
        MinProbabilityLiftOverImplied FLOAT NULL,
        StakeMultiplier DECIMAL(9,4) NULL,
        StrategyConfigurationJson NVARCHAR(MAX) NULL,
        LeagueFilterJson NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedBotDefinitions_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedBotDefinitions_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_AutomatedBotDefinitions_BaseStrategy CHECK
            (BaseStrategy IN (N'LEGACY_A', N'LEGACY_B', N'LEGACY_EMPIRICAL', N'MODELS_2026', N'GOALS_MARKET_ANCHORED')),
        CONSTRAINT CK_AutomatedBotDefinitions_StakeMultiplier CHECK
            (StakeMultiplier IS NULL OR (StakeMultiplier > 0 AND StakeMultiplier <= 10))
    );
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotDefinitions')
         AND name = N'CK_AutomatedBotDefinitions_BaseStrategy'
         AND definition NOT LIKE N'%GOALS_MARKET_ANCHORED%'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions DROP CONSTRAINT CK_AutomatedBotDefinitions_BaseStrategy;
END;

IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotDefinitions')
         AND name = N'CK_AutomatedBotDefinitions_BaseStrategy'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions WITH CHECK ADD CONSTRAINT CK_AutomatedBotDefinitions_BaseStrategy
        CHECK (BaseStrategy IN (N'LEGACY_A', N'LEGACY_B', N'LEGACY_EMPIRICAL', N'MODELS_2026', N'GOALS_MARKET_ANCHORED'));
END;

GO

IF COL_LENGTH(N'dbo.AutomatedBotDefinitions', N'StrategyConfigurationJson') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions
        ADD StrategyConfigurationJson NVARCHAR(MAX) NULL;
END;

GO

IF COL_LENGTH(N'dbo.AutomatedBotDefinitions', N'PublishEnabled') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions
        ADD PublishEnabled BIT NOT NULL
            CONSTRAINT DF_AutomatedBotDefinitions_PublishEnabled DEFAULT (1) WITH VALUES;
END;

GO

IF COL_LENGTH(N'dbo.AutomatedBotDefinitions', N'LeagueFilterJson') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions
        ADD LeagueFilterJson NVARCHAR(MAX) NULL;
END;

GO

MERGE dbo.AutomatedBotDefinitions AS target
USING
(
    VALUES
        (N'A', N'Bot A Actual', N'Modelo histórico y contexto con los umbrales productivos actuales.', N'LEGACY_A',
            0.035, 0.030, 0.350, 1.750, CONVERT(BIT, 0), NULL, 0.000, CONVERT(DECIMAL(9,4), 1.0000)),
        (N'B', N'Bot B Conservador', N'Variante histórica con mayor exigencia, cuota mínima y medio stake.', N'LEGACY_B',
            0.0385, 0.033, 0.385, 1.575, CONVERT(BIT, 0), 1.600, 0.100, CONVERT(DECIMAL(9,4), 0.5000)),
        (N'C2026', N'Bot C · Pick Selector 2026', N'Selector auditable: combina los doce modelos ML con historial temporal, contexto, línea, cuota, edge, EV, calidad y acuerdo.', N'MODELS_2026',
            0.035, 0.030, 0.350, 1.750, CONVERT(BIT, 0), NULL, 0.000, CONVERT(DECIMAL(9,4), 1.0000)),
        (N'D2026', N'Bot D · Team Strength Gap', N'Experimento sobre Bot C que incorpora Elo temporal, duelos directos y rivales comunes para medir la brecha de nivel entre equipos.', N'MODELS_2026',
            0.035, 0.030, 0.350, 1.750, CONVERT(BIT, 0), NULL, 0.000, CONVERT(DECIMAL(9,4), 1.0000)),
        (N'E2026', N'Bot E · Calibración empírica', N'Experimento walk-forward sobre Bot C: calibra la probabilidad con resultados asiáticos anteriores, recencia, calidad, no-vig e incertidumbre.', N'MODELS_2026',
            0.025, 0.020, 0.350, 1.750, CONVERT(BIT, 0), NULL, 0.000, CONVERT(DECIMAL(9,4), 1.0000)),
        (N'F2026', N'Bot F · Legacy ML calibrado', N'Experimento walk-forward: usa los modelos ML legacy como base y aplica el mismo calibrador empírico jerárquico de Bot E.', N'LEGACY_EMPIRICAL',
            0.025, 0.020, 0.350, 1.750, CONVERT(BIT, 0), NULL, 0.000, CONVERT(DECIMAL(9,4), 1.0000))
) AS source(
    BotKey,
    DisplayName,
    Description,
    BaseStrategy,
    MinEdge,
    MinExpectedValue,
    MinDistanceToLine,
    MaxContextDifference,
    AllowModelDisagreement,
    MinOddsExclusive,
    MinProbabilityLiftOverImplied,
    StakeMultiplier)
ON target.BotKey = source.BotKey
WHEN NOT MATCHED THEN
    INSERT
    (
        BotKey,
        DisplayName,
        Description,
        BaseStrategy,
        IsEnabled,
        IsBuiltIn,
        MarketFamilies,
        MinEdge,
        MinExpectedValue,
        MinDistanceToLine,
        MaxContextDifference,
        AllowModelDisagreement,
        MinOddsExclusive,
        MinProbabilityLiftOverImplied,
        StakeMultiplier
    )
    VALUES
    (
        source.BotKey,
        source.DisplayName,
        source.Description,
        source.BaseStrategy,
        1,
        1,
        N'CORNERS,GOALS,SHOTS,SOG',
        source.MinEdge,
        source.MinExpectedValue,
        source.MinDistanceToLine,
        source.MaxContextDifference,
        source.AllowModelDisagreement,
        source.MinOddsExclusive,
        source.MinProbabilityLiftOverImplied,
        source.StakeMultiplier
    );

GO

MERGE dbo.AutomatedBotDefinitions WITH (HOLDLOCK) AS target
USING
(
    SELECT
        N'G2026' AS BotKey,
        N'Bot G Goals Specialist' AS DisplayName,
        N'Especialista en goles anclado al mercado, con calibración, incertidumbre, OOD y abstención auditable.' AS Description,
        N'GOALS_MARKET_ANCHORED' AS BaseStrategy,
        CONVERT(BIT, 1) AS IsEnabled,
        CONVERT(BIT, 0) AS PublishEnabled,
        N'GOALS' AS MarketFamilies,
        CONVERT(DECIMAL(9,4), 1.0000) AS StakeMultiplier,
            N'{"botKey":"G2026","name":"Bot G Goals Specialist","baseStrategy":"GOALS_MARKET_ANCHORED","configurationVersion":"bot-g-goals-market-1.0.0","featureSchemaVersion":"bot-g-goals-features-1.0.0","legacyModelVersion":"goals_v1","model2026Version":"goals_deep_tuned_v2","enabled":true,"publishEnabled":false,"shadowMode":true,"stake":1.0,"supportedMarkets":["totalGoals","homeTeamGoals","awayTeamGoals"],"features":{"windows":[5,10,20],"decayFactor":0.85,"requiredVenueMatches":8,"minimumHistoricalMatches":8,"minimumStandardDeviation":0.25,"lineHistoryPriorStrength":20.0,"lineHitRatePriorMean":0.5,"pushRatePriorMean":0.08},"metaModel":{"required":true,"modelVersion":"bot-g-market-meta-1.0.0","featureSchemaVersion":"bot-g-goals-features-1.0.0","maximumAbsoluteResidualLogit":4.0},"calibration":{"version":"bot-g-calibration-1.0.0","method":"BetaCalibration","minimumEffectiveSampleSize":20,"outcomeAvailabilityLagHours":8,"globalPriorStrength":80.0,"marketPriorStrength":60.0,"selectionPriorStrength":40.0,"bookmakerPriorStrength":40.0},"uncertainty":{"version":"bot-g-uncertainty-1.0.0","confidenceZScore":1.645,"conservativeLambda":1.0,"minimumUncertainty":0.005,"maximumUncertainty":0.25,"useLowerBound":true},"outOfDistribution":{"version":"bot-g-ood-1.0.0","minimumReferenceSampleSize":30,"robustZScoreThreshold":3.5,"severeRobustZScore":8.0},"thresholds":{"minimumOdds":1.6,"maximumOdds":2.2,"minimumFinalProbability":0.54,"minimumConservativeEdge":0.02,"minimumConservativeExpectedValue":0.015,"minimumDataQuality":0.65,"minimumCalibrationReliability":0.3,"maximumProbabilityUncertainty":0.08,"maximumOodScore":0.7,"maximumModelDisagreement":1.5,"minimumHistoricalMatches":8,"minimumSettlementEffectiveSampleSize":40,"maximumOddsAgeMinutes":120},"ranking":{"conservativeExpectedValueWeight":0.35,"conservativeEdgeWeight":0.25,"calibrationReliabilityWeight":0.15,"dataQualityWeight":0.1,"inverseUncertaintyWeight":0.1,"contextAgreementWeight":0.05}}' AS StrategyConfigurationJson
) AS source ON target.BotKey = source.BotKey
WHEN NOT MATCHED THEN INSERT
(
    BotKey, DisplayName, Description, BaseStrategy, IsEnabled,
    PublishEnabled, IsBuiltIn, MarketFamilies, StakeMultiplier,
    StrategyConfigurationJson
)
VALUES
(
    source.BotKey, source.DisplayName, source.Description,
    source.BaseStrategy, source.IsEnabled, source.PublishEnabled, 1,
    source.MarketFamilies, source.StakeMultiplier,
    source.StrategyConfigurationJson
);

GO

-- H is a corners-only calibration challenger. It reuses the 2026 base signals,
-- writes every decision to the audit table and is structurally unable to publish.
-- It is deliberately separate from G so goals and corners can earn promotion independently.
MERGE dbo.AutomatedBotDefinitions WITH (HOLDLOCK) AS target
USING
(
    SELECT
        N'H2026' AS BotKey,
        N'Bot H · Challenger calibración córners' AS DisplayName,
        N'Experimento shadow de córners: calibración walk-forward anclada a no-vig, umbrales conservadores y cero publicación.' AS Description,
        N'MODELS_2026' AS BaseStrategy,
        CONVERT(BIT, 1) AS IsEnabled,
        CONVERT(BIT, 0) AS PublishEnabled,
        N'CORNERS' AS MarketFamilies,
        CONVERT(FLOAT, 0.04) AS MinEdge,
        CONVERT(FLOAT, 0.03) AS MinExpectedValue,
        CONVERT(DECIMAL(9,4), 0.5000) AS StakeMultiplier,
        N'{"configurationVersion":"bot-h-corners-calibration-shadow-1.0.0","featureSchemaVersion":"bot-c-features-1.0.0","basePredictionSource":"MODELS_2026","selectorEnabled":true,"allowRuleBasedFallback":true,"minimumCalibratedProbability":0.56,"minimumFinalEdge":0.04,"minimumFinalExpectedValue":0.03,"minimumDataQualityScore":0.70,"minimumContextAgreementScore":0.70,"minimumOdds":1.60,"maximumOdds":2.20,"marketThresholds":{"TotalCorners":{"enabled":false},"HomeTeamCorners":{"enabled":true,"minimumFinalProbability":0.56,"minimumFinalEdge":0.04,"minimumFinalExpectedValue":0.03,"minimumDataQualityScore":0.70,"minimumContextAgreementScore":0.70,"minimumHistoricalMatches":8,"minimumOdds":1.60,"maximumOdds":2.20},"AwayTeamCorners":{"enabled":true,"minimumFinalProbability":0.56,"minimumFinalEdge":0.04,"minimumFinalExpectedValue":0.03,"minimumDataQualityScore":0.70,"minimumContextAgreementScore":0.70,"minimumHistoricalMatches":8,"minimumOdds":1.60,"maximumOdds":2.20}},"teamStrength":{"enabled":false},"footballIntelligence":{"enabled":false},"empiricalCalibration":{"enabled":true,"version":"bot-h-corners-calibration-shadow-1.0.0","sourceBotKey":"C2026","minimumObservations":50,"minimumExactMarketObservations":20,"minimumEffectiveObservations":20,"targetEffectiveObservations":100,"outcomeAvailabilityLagHours":8,"probabilityBandwidth":0.08,"globalPriorStrength":80,"familyPriorStrength":120,"exactMarketPriorStrength":60,"recencyHalfLifeDays":30,"qualityWeightFloor":0.60,"minimumReliability":0.30,"confidenceZScore":1.645,"requireSameBaseModelVersion":false,"requireNoVigProbability":true}}' AS StrategyConfigurationJson
) AS source ON target.BotKey = source.BotKey
WHEN NOT MATCHED THEN INSERT
(
    BotKey, DisplayName, Description, BaseStrategy, IsEnabled, PublishEnabled,
    IsBuiltIn, MarketFamilies, MinEdge, MinExpectedValue, StakeMultiplier,
    StrategyConfigurationJson
)
VALUES
(
    source.BotKey, source.DisplayName, source.Description, source.BaseStrategy,
    source.IsEnabled, source.PublishEnabled, 1, source.MarketFamilies,
    source.MinEdge, source.MinExpectedValue, source.StakeMultiplier,
    source.StrategyConfigurationJson
)
WHEN MATCHED AND target.IsBuiltIn = 1 THEN UPDATE SET
    DisplayName = source.DisplayName,
    Description = source.Description,
    BaseStrategy = source.BaseStrategy,
    PublishEnabled = 0,
    MarketFamilies = source.MarketFamilies,
    MinEdge = source.MinEdge,
    MinExpectedValue = source.MinExpectedValue,
    StakeMultiplier = source.StakeMultiplier,
    UpdatedAtUtc = SYSUTCDATETIME();

GO

-- Bot B is retired. Its historical picks remain intact for audit and comparison,
-- but schema initialization must never reactivate it as an executable bot.
UPDATE dbo.AutomatedBotDefinitions
SET IsEnabled = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'B'
  AND IsEnabled <> 0;

GO

-- Pause every Chilean competition for CORNERS only. Merge the exclusion into
-- every built-in A-F definition without replacing filters maintained for
-- Brazil or any other league/market family. Re-running this block leaves one
-- canonical Chile exclusion and never appends a duplicate.
DECLARE @ChileFilterBotKey NVARCHAR(50);
DECLARE @ChileFilterJson NVARCHAR(MAX);
DECLARE @ChileMergedFilterJson NVARCHAR(MAX);
DECLARE @FirstCornersFilterIndex INT;
DECLARE @CornersFilterIndex INT;
DECLARE @ExistingCornerExclusions NVARCHAR(MAX);
DECLARE @MergedCornerExclusions NVARCHAR(MAX);
DECLARE @CornerFilterPath NVARCHAR(200);
DECLARE @ChileCornerCursor CURSOR;

DECLARE ChileFilterBotCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT BotKey
    FROM dbo.AutomatedBotDefinitions
    WHERE BotKey IN (N'A', N'B', N'C2026', N'D2026', N'E2026', N'F2026');

OPEN ChileFilterBotCursor;
FETCH NEXT FROM ChileFilterBotCursor INTO @ChileFilterBotKey;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @ChileFilterJson = LeagueFilterJson
    FROM dbo.AutomatedBotDefinitions
    WHERE BotKey = @ChileFilterBotKey;

    -- Invalid legacy JSON cannot be merged safely. Replace only that unusable
    -- value; every valid array is preserved and modified in place.
    SET @ChileMergedFilterJson = CASE
        WHEN ISJSON(@ChileFilterJson) = 1
         AND LEFT(LTRIM(@ChileFilterJson), 1) = N'['
            THEN @ChileFilterJson
        ELSE N'[]'
    END;

    SELECT @FirstCornersFilterIndex = MIN(CONVERT(INT, filterEntry.[key]))
    FROM OPENJSON(@ChileMergedFilterJson) AS filterEntry
    WHERE UPPER(LTRIM(RTRIM(JSON_VALUE(filterEntry.[value], N'$.marketFamily')))) = N'CORNERS';

    IF @FirstCornersFilterIndex IS NULL
    BEGIN
        SET @ChileMergedFilterJson = JSON_MODIFY(
            @ChileMergedFilterJson,
            N'append $',
            JSON_QUERY(N'{"marketFamily":"CORNERS","includedLeagues":[],"excludedLeagues":["Chile - *"]}'));
    END;
    ELSE
    BEGIN
        SET @ChileCornerCursor = CURSOR LOCAL FAST_FORWARD FOR
            SELECT CONVERT(INT, filterEntry.[key])
            FROM OPENJSON(@ChileMergedFilterJson) AS filterEntry
            WHERE UPPER(LTRIM(RTRIM(JSON_VALUE(filterEntry.[value], N'$.marketFamily')))) = N'CORNERS'
            ORDER BY CONVERT(INT, filterEntry.[key]);

        OPEN @ChileCornerCursor;
        FETCH NEXT FROM @ChileCornerCursor INTO @CornersFilterIndex;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @CornerFilterPath = CONCAT(N'$[', @CornersFilterIndex, N'].excludedLeagues');
            SET @ExistingCornerExclusions = JSON_QUERY(@ChileMergedFilterJson, @CornerFilterPath);
            IF @ExistingCornerExclusions IS NULL
               OR LEFT(LTRIM(@ExistingCornerExclusions), 1) <> N'['
                SET @ExistingCornerExclusions = N'[]';

            ;WITH ExistingExclusions AS
            (
                SELECT
                    Exclusion = MIN(LTRIM(RTRIM(CONVERT(NVARCHAR(200), exclusionEntry.[value])))),
                    FirstOrdinal = MIN(CONVERT(INT, exclusionEntry.[key]))
                FROM OPENJSON(@ExistingCornerExclusions) AS exclusionEntry
                WHERE exclusionEntry.[type] = 1
                  AND NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(200), exclusionEntry.[value]))), N'') IS NOT NULL
                  AND LTRIM(RTRIM(CONVERT(NVARCHAR(200), exclusionEntry.[value])))
                        COLLATE Latin1_General_100_CI_AI <> N'Chile - *'
                GROUP BY LTRIM(RTRIM(CONVERT(NVARCHAR(200), exclusionEntry.[value])))
                    COLLATE Latin1_General_100_CI_AI
            ), CanonicalExclusions AS
            (
                SELECT Exclusion, FirstOrdinal
                FROM ExistingExclusions
                UNION ALL
                SELECT N'Chile - *', 2147483647
                WHERE @CornersFilterIndex = @FirstCornersFilterIndex
            )
            SELECT @MergedCornerExclusions = COALESCE(
                N'[' + STRING_AGG(
                    CONVERT(NVARCHAR(MAX), N'"' + STRING_ESCAPE(Exclusion, N'json') + N'"'),
                    N',') WITHIN GROUP (ORDER BY FirstOrdinal) + N']',
                N'[]')
            FROM CanonicalExclusions;

            SET @ChileMergedFilterJson = JSON_MODIFY(
                @ChileMergedFilterJson,
                @CornerFilterPath,
                JSON_QUERY(@MergedCornerExclusions));

            FETCH NEXT FROM @ChileCornerCursor INTO @CornersFilterIndex;
        END;
        CLOSE @ChileCornerCursor;
        DEALLOCATE @ChileCornerCursor;
    END;

    IF ISNULL(@ChileFilterJson, N'') <> @ChileMergedFilterJson
    BEGIN
        UPDATE dbo.AutomatedBotDefinitions
        SET LeagueFilterJson = @ChileMergedFilterJson,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE BotKey = @ChileFilterBotKey;
    END;

    FETCH NEXT FROM ChileFilterBotCursor INTO @ChileFilterBotKey;
END;
CLOSE ChileFilterBotCursor;
DEALLOCATE ChileFilterBotCursor;

GO

UPDATE dbo.AutomatedBotDefinitions
SET StrategyConfigurationJson = N'{"configurationVersion":"bot-d-strength-gap-1.0.0","featureSchemaVersion":"bot-d-features-1.0.0","teamStrength":{"enabled":true,"version":"bot-d-team-strength-1.0.0","resultDecayFactor":0.9,"eloKFactor":24,"homeAdvantageElo":50,"eloWeight":0.5,"directMatchWeight":0.2,"commonOpponentWeight":0.3,"minimumMatchesPerTeam":4,"minimumCommonOpponents":1,"minimumConfidenceScore":0.45,"maximumProbabilityAdjustment":0.08,"contextExpectedValueSigmaWeight":0.35,"homeTeamMarketWeight":1.0,"awayTeamMarketWeight":0.8,"totalMarketWeight":0.15}}',
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'D2026'
  AND StrategyConfigurationJson IS NULL;

GO

UPDATE dbo.AutomatedBotDefinitions
SET StrategyConfigurationJson = CASE
        WHEN ISNULL(ISJSON(StrategyConfigurationJson), 0) <> 1
            THEN N'{"configurationVersion":"bot-e-empirical-calibration-1.0.2","featureSchemaVersion":"bot-c-features-1.0.0","minimumCalibratedProbability":0.54,"minimumFinalEdge":0.025,"minimumFinalExpectedValue":0.02,"minimumDataQualityScore":0.65,"minimumContextAgreementScore":0.65,"minimumOdds":1.60,"maximumOdds":2.20,"teamStrength":{"enabled":false},"empiricalCalibration":{"enabled":true,"version":"bot-e-empirical-calibration-1.0.2","sourceBotKey":"C2026","minimumObservations":20,"minimumExactMarketObservations":12,"minimumEffectiveObservations":8,"targetEffectiveObservations":80,"outcomeAvailabilityLagHours":8,"probabilityBandwidth":0.10,"globalPriorStrength":40,"familyPriorStrength":80,"exactMarketPriorStrength":40,"recencyHalfLifeDays":45,"qualityWeightFloor":0.50,"minimumReliability":0.15,"confidenceZScore":0.50,"requireSameBaseModelVersion":false,"requireNoVigProbability":true}}'
        ELSE JSON_MODIFY(
            JSON_MODIFY(
                JSON_MODIFY(
                    StrategyConfigurationJson,
                    '$.configurationVersion',
                    N'bot-e-empirical-calibration-1.0.2'),
                '$.empiricalCalibration.version',
                N'bot-e-empirical-calibration-1.0.2'),
            '$.empiricalCalibration.minimumEffectiveObservations',
            8)
    END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'E2026'
  AND
  (
      StrategyConfigurationJson IS NULL OR ISJSON(StrategyConfigurationJson) <> 1
      OR JSON_VALUE(
          CASE WHEN ISJSON(StrategyConfigurationJson) = 1
              THEN StrategyConfigurationJson ELSE N'{}' END,
          '$.configurationVersion') IN
          (N'bot-e-empirical-calibration-1.0.0', N'bot-e-empirical-calibration-1.0.1')
  );

UPDATE dbo.AutomatedBotDefinitions
SET DisplayName = N'Bot E · Calibración empírica',
    Description = N'Experimento walk-forward sobre Bot C: calibra la probabilidad con resultados asiáticos anteriores, recencia, calidad, no-vig e incertidumbre.',
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'E2026'
  AND IsBuiltIn = 1
  AND (DisplayName IS NULL OR DisplayName = N'Bot E')
  AND (Description IS NULL OR Description = N'');

GO

UPDATE dbo.AutomatedBotDefinitions
SET StrategyConfigurationJson = N'{"configurationVersion":"bot-f-legacy-empirical-1.0.0","featureSchemaVersion":"bot-f-legacy-features-1.0.0","basePredictionSource":"LEGACY","baseModelVersionOverride":"legacy-corners-v1+goals-v1+shots-v3+sog-v1","baseModelTrainedThroughUtc":"2026-06-11T16:36:16Z","minimumCalibratedProbability":0.54,"minimumFinalEdge":0.025,"minimumFinalExpectedValue":0.02,"minimumDataQualityScore":0.65,"minimumContextAgreementScore":0.65,"minimumOdds":1.60,"maximumOdds":2.20,"teamStrength":{"enabled":false},"empiricalCalibration":{"enabled":true,"version":"bot-f-legacy-empirical-1.0.0","sourceBotKey":"F2026","minimumObservations":20,"minimumExactMarketObservations":12,"minimumEffectiveObservations":8,"targetEffectiveObservations":80,"outcomeAvailabilityLagHours":8,"probabilityBandwidth":0.10,"globalPriorStrength":40,"familyPriorStrength":80,"exactMarketPriorStrength":40,"recencyHalfLifeDays":45,"qualityWeightFloor":0.50,"minimumReliability":0.15,"confidenceZScore":0.50,"requireSameBaseModelVersion":false,"requireNoVigProbability":true}}',
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'F2026'
  AND StrategyConfigurationJson IS NULL;

GO

DECLARE @FootballIntelligenceConfiguration NVARCHAR(MAX) =
    N'{"enabled":true,"version":"football-intelligence-adjustment-1.0.0","weight":0.35,"maximumProbabilityAdjustment":0.04,"minimumTeamConfidence":0.60,"maximumSnapshotAgeMinutes":4320,"minimumActionableFacts":1,"minimumIndependentSources":1,"attackWeight":0.35,"defenceWeight":0.25,"widthWeight":0.20,"setPieceWeight":0.20}';

UPDATE dbo.AutomatedBotDefinitions
SET StrategyConfigurationJson = JSON_MODIFY(
        JSON_MODIFY(
            StrategyConfigurationJson,
            '$.configurationVersion',
            N'bot-e-intelligence-1.1.0'),
        '$.footballIntelligence',
        JSON_QUERY(@FootballIntelligenceConfiguration)),
    Description = CASE
        WHEN Description = N'Experimento walk-forward sobre Bot C: calibra la probabilidad con resultados asiáticos anteriores, recencia, calidad, no-vig e incertidumbre.'
            THEN N'Modelos 2026 con calibración empírica e inteligencia pre-partido; sin evidencia utilizable, el ajuste contextual es exactamente cero.'
        ELSE Description
    END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'E2026'
  AND ISJSON(StrategyConfigurationJson) = 1
  AND
  (
      JSON_VALUE(StrategyConfigurationJson, '$.configurationVersion') IN
          (N'bot-e-empirical-calibration-1.0.0', N'bot-e-empirical-calibration-1.0.1', N'bot-e-empirical-calibration-1.0.2')
  );

UPDATE dbo.AutomatedBotDefinitions
SET StrategyConfigurationJson = JSON_MODIFY(
        JSON_MODIFY(
            StrategyConfigurationJson,
            '$.configurationVersion',
            N'bot-f-legacy-intelligence-1.1.0'),
        '$.footballIntelligence',
        JSON_QUERY(@FootballIntelligenceConfiguration)),
    Description = CASE
        WHEN Description = N'Experimento walk-forward: usa los modelos ML legacy como base y aplica el mismo calibrador empírico jerárquico de Bot E.'
            THEN N'Modelos ML legacy con la misma calibración e inteligencia pre-partido de Bot E; sin evidencia utilizable, el ajuste contextual es exactamente cero.'
        ELSE Description
    END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'F2026'
  AND ISJSON(StrategyConfigurationJson) = 1
  AND JSON_VALUE(StrategyConfigurationJson, '$.configurationVersion') IN
      (N'bot-f-legacy-empirical-1.0.0');

-- Intelligence is a shared pre-match evidence layer, not an E/F-only feature.
-- Enable it for every active built-in decision engine. Bot B remains retired and
-- historical selections are never rewritten by this configuration migration.
UPDATE dbo.AutomatedBotDefinitions
SET StrategyConfigurationJson = JSON_MODIFY(
        JSON_MODIFY(
            CASE
                WHEN ISJSON(StrategyConfigurationJson) = 1
                 AND LEFT(LTRIM(StrategyConfigurationJson), 1) = N'{'
                    THEN StrategyConfigurationJson
                ELSE N'{}'
            END,
            '$.configurationVersion',
            CASE BotKey
                WHEN N'A' THEN N'bot-a-intelligence-1.1.0'
                WHEN N'C2026' THEN N'bot-c-intelligence-1.1.0'
                WHEN N'D2026' THEN N'bot-d-strength-intelligence-1.1.0'
                WHEN N'E2026' THEN N'bot-e-intelligence-1.1.0'
                WHEN N'F2026' THEN N'bot-f-legacy-intelligence-1.1.0'
                WHEN N'G2026' THEN N'bot-g-goals-market-intelligence-1.1.0'
                WHEN N'H2026' THEN N'bot-h-corners-calibration-shadow-1.1.0-intelligence'
            END),
        '$.footballIntelligence',
        JSON_QUERY(@FootballIntelligenceConfiguration)),
    Description = CASE BotKey
        WHEN N'A' THEN N'Modelo histórico y contexto con inteligencia pre-partido; sin evidencia utilizable, el ajuste es exactamente cero.'
        WHEN N'C2026' THEN N'Selector 2026 auditable con inteligencia pre-partido aplicada antes de edge, EV y aprobación.'
        WHEN N'D2026' THEN N'Brecha de nivel y fuerza de equipos con inteligencia pre-partido aplicada antes de edge, EV y aprobación.'
        WHEN N'E2026' THEN N'Modelos 2026 con calibración empírica e inteligencia pre-partido; sin evidencia utilizable, el ajuste es exactamente cero.'
        WHEN N'F2026' THEN N'Modelos ML legacy calibrados con inteligencia pre-partido; sin evidencia utilizable, el ajuste es exactamente cero.'
        WHEN N'G2026' THEN N'Especialista de goles anclado al mercado con inteligencia pre-partido antes de incertidumbre, EV y abstención; permanece shadow.'
        WHEN N'H2026' THEN N'Challenger shadow de córners con calibración conservadora e inteligencia pre-partido; publicación estructuralmente bloqueada.'
        ELSE Description
    END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE IsBuiltIn = 1
  AND BotKey IN (N'A', N'C2026', N'D2026', N'E2026', N'F2026', N'G2026', N'H2026');

GO

UPDATE dbo.AutomatedBotDefinitions
SET DisplayName = N'Bot C · Pick Selector 2026',
    Description = N'Selector auditable: combina los doce modelos ML con historial temporal, contexto, línea, cuota, edge, EV, calidad y acuerdo.',
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'C2026'
  AND IsBuiltIn = 1
  AND DisplayName = N'Bot C · Modelos 2026'
  AND Description = N'Doce modelos ML para córners, goles, tiros y tiros al arco.';

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAutomatedBotDefinitions
    @BotKeys NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        BotKey,
        DisplayName,
        Description,
        BaseStrategy,
        IsEnabled,
        PublishEnabled,
        IsBuiltIn,
        MarketFamilies,
        MinEdge,
        MinExpectedValue,
        MinDistanceToLine,
        MaxContextDifference,
        AllowModelDisagreement,
        MinOddsExclusive,
        MinProbabilityLiftOverImplied,
        StakeMultiplier,
        StrategyConfigurationJson,
        LeagueFilterJson,
        CreatedAtUtc,
        UpdatedAtUtc
    FROM dbo.AutomatedBotDefinitions
    WHERE @BotKeys IS NULL
       OR BotKey IN
          (
              SELECT UPPER(LTRIM(RTRIM(value)))
              FROM STRING_SPLIT(@BotKeys, N',')
          )
    ORDER BY IsBuiltIn DESC, BotKey;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertAutomatedBotDefinition
    @BotKey NVARCHAR(50),
    @DisplayName NVARCHAR(120),
    @Description NVARCHAR(1000),
    @BaseStrategy NVARCHAR(30),
    @IsEnabled BIT,
    @MarketFamilies NVARCHAR(200),
    @MinEdge FLOAT = NULL,
    @MinExpectedValue FLOAT = NULL,
    @MinDistanceToLine FLOAT = NULL,
    @MaxContextDifference FLOAT = NULL,
    @AllowModelDisagreement BIT = NULL,
    @MinOddsExclusive FLOAT = NULL,
    @MinProbabilityLiftOverImplied FLOAT = NULL,
    @StakeMultiplier DECIMAL(9,4) = NULL,
    @StrategyConfigurationJson NVARCHAR(MAX) = NULL,
    @PublishEnabled BIT = NULL,
    @LeagueFilterJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @LeagueFilterJson IS NOT NULL AND ISJSON(@LeagueFilterJson) <> 1
        THROW 50026, 'LeagueFilterJson must be valid JSON.', 1;

    MERGE dbo.AutomatedBotDefinitions AS target
    USING
    (
        SELECT
            @BotKey AS BotKey,
            @DisplayName AS DisplayName,
            @Description AS Description,
            @BaseStrategy AS BaseStrategy,
            @IsEnabled AS IsEnabled,
            @PublishEnabled AS PublishEnabled,
            @MarketFamilies AS MarketFamilies,
            @MinEdge AS MinEdge,
            @MinExpectedValue AS MinExpectedValue,
            @MinDistanceToLine AS MinDistanceToLine,
            @MaxContextDifference AS MaxContextDifference,
            @AllowModelDisagreement AS AllowModelDisagreement,
            @MinOddsExclusive AS MinOddsExclusive,
            @MinProbabilityLiftOverImplied AS MinProbabilityLiftOverImplied,
            @StakeMultiplier AS StakeMultiplier,
            @StrategyConfigurationJson AS StrategyConfigurationJson,
            @LeagueFilterJson AS LeagueFilterJson
    ) AS source
    ON target.BotKey = source.BotKey
    WHEN MATCHED THEN
        UPDATE SET
            DisplayName = source.DisplayName,
            Description = source.Description,
            BaseStrategy = source.BaseStrategy,
            IsEnabled = source.IsEnabled,
            PublishEnabled = COALESCE(source.PublishEnabled, target.PublishEnabled),
            MarketFamilies = source.MarketFamilies,
            MinEdge = source.MinEdge,
            MinExpectedValue = source.MinExpectedValue,
            MinDistanceToLine = source.MinDistanceToLine,
            MaxContextDifference = source.MaxContextDifference,
            AllowModelDisagreement = source.AllowModelDisagreement,
            MinOddsExclusive = source.MinOddsExclusive,
            MinProbabilityLiftOverImplied = source.MinProbabilityLiftOverImplied,
            StakeMultiplier = source.StakeMultiplier,
            StrategyConfigurationJson = source.StrategyConfigurationJson,
            LeagueFilterJson = COALESCE(source.LeagueFilterJson, target.LeagueFilterJson),
            UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            BotKey,
            DisplayName,
            Description,
            BaseStrategy,
            IsEnabled,
            PublishEnabled,
            IsBuiltIn,
            MarketFamilies,
            MinEdge,
            MinExpectedValue,
            MinDistanceToLine,
            MaxContextDifference,
            AllowModelDisagreement,
            MinOddsExclusive,
            MinProbabilityLiftOverImplied,
            StakeMultiplier,
            StrategyConfigurationJson,
            LeagueFilterJson
        )
        VALUES
        (
            source.BotKey,
            source.DisplayName,
            source.Description,
            source.BaseStrategy,
            source.IsEnabled,
            COALESCE(source.PublishEnabled, CONVERT(BIT, 1)),
            0,
            source.MarketFamilies,
            source.MinEdge,
            source.MinExpectedValue,
            source.MinDistanceToLine,
            source.MaxContextDifference,
            source.AllowModelDisagreement,
            source.MinOddsExclusive,
            source.MinProbabilityLiftOverImplied,
            source.StakeMultiplier,
            source.StrategyConfigurationJson,
            source.LeagueFilterJson
        );

    EXEC dbo.sp_GetAutomatedBotDefinitions @BotKeys = @BotKey;
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedBotPickEvaluations
    (
        AutomatedBotPickEvaluationId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AutomatedBotPickEvaluations PRIMARY KEY,
        IdempotencyKey CHAR(64) NOT NULL,
        RunId UNIQUEIDENTIFIER NOT NULL,
        BotKey NVARCHAR(50) NOT NULL,
        AutomationVersion NVARCHAR(50) NOT NULL,
        PartidoProximoCuotaId BIGINT NOT NULL,
        ApiFootballFixtureId BIGINT NULL,
        MatchDate DATETIME2(0) NOT NULL,
        League NVARCHAR(200) NOT NULL,
        HomeTeam NVARCHAR(150) NOT NULL,
        AwayTeam NVARCHAR(150) NOT NULL,
        Source NVARCHAR(50) NOT NULL,
        SourceMarketType NVARCHAR(50) NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        LineValue DECIMAL(6,2) NOT NULL,
        SelectedSide NVARCHAR(10) NULL,
        SelectedOdds DECIMAL(10,2) NULL,
        DecisionEngineType NVARCHAR(40) NOT NULL,
        Decision NVARCHAR(20) NOT NULL,
        BaseModelName NVARCHAR(120) NULL,
        BaseModelVersion NVARCHAR(120) NULL,
        FeatureSchemaVersion NVARCHAR(80) NOT NULL,
        ConfigurationVersion NVARCHAR(80) NOT NULL,
        BaseRawProbability DECIMAL(9,6) NULL,
        BaseCalibratedProbability DECIMAL(9,6) NULL,
        RawImpliedProbability DECIMAL(9,6) NULL,
        MarketNoVigProbability DECIMAL(9,6) NULL,
        FinalProbability DECIMAL(9,6) NULL,
        FinalEdge DECIMAL(9,6) NULL,
        FinalExpectedValue DECIMAL(9,6) NULL,
        RuleBasedConfidenceScore DECIMAL(9,6) NULL,
        ContextExpectedValue DECIMAL(12,4) NULL,
        ContextAgreementScore DECIMAL(9,6) NULL,
        DataQualityScore DECIMAL(9,6) NULL,
        DecisionReasonsJson NVARCHAR(MAX) NOT NULL,
        RiskFlagsJson NVARCHAR(MAX) NOT NULL,
        Explanation NVARCHAR(1000) NOT NULL,
        FeatureSnapshotJson NVARCHAR(MAX) NOT NULL,
        PublishedSelectionId BIGINT NULL,
        EvaluatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedBotPickEvaluations_Evaluated DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedBotPickEvaluations_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_AutomatedBotPickEvaluations_Idempotency UNIQUE (IdempotencyKey),
        CONSTRAINT CK_AutomatedBotPickEvaluations_Decision CHECK
            (Decision IN (N'Approved', N'Rejected', N'Abstain', N'PendingData', N'Invalid'))
    );
END;

GO

-- These publication-audit fields are also part of the full G migration.  They are
-- bootstrapped here because the productive upsert below must be able to update the
-- G audit row in the same transaction that creates the published selection.
IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'Published') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD Published BIT NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'PublicationStatus') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD PublicationStatus NVARCHAR(20) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'PublicationLine') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD PublicationLine DECIMAL(6,2) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'PublicationOdds') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD PublicationOdds DECIMAL(10,4) NULL;

-- The productive upsert is defined in this bootstrap script, before the full G
-- migration is executed. Bootstrap every G audit column referenced by that
-- procedure so an existing database can compile the procedure on the first pass.
IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'FixtureIdentity') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD FixtureIdentity BIGINT NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'ConservativeProbability') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD ConservativeProbability DECIMAL(9,6) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'ConservativeEdge') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD ConservativeEdge DECIMAL(9,6) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'ConservativeExpectedValue') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD ConservativeExpectedValue DECIMAL(9,6) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'GSelectionScore') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD GSelectionScore DECIMAL(9,6) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'StakeUnits') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD StakeUnits DECIMAL(9,4) NULL;

GO

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'BaseModelTrainedThroughUtc') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations
        ADD BaseModelTrainedThroughUtc DATETIME2(0) NULL;
END;

-- The twelve currently deployed model manifests all declare 2026-08-07.
-- This one-time guarded backfill gives legacy C evaluations the same auditable
-- cutoff that new evaluations persist directly; unknown model versions remain NULL.
UPDATE dbo.AutomatedBotPickEvaluations
SET BaseModelTrainedThroughUtc = CONVERT(DATETIME2(0), N'2026-08-07T00:00:00'),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'C2026'
  AND BaseModelTrainedThroughUtc IS NULL
  AND BaseModelVersion IN
  (
      N'home-corners-2026-08-09-trial-1840',
      N'targetawaycorners-2026-08-09-trial-53',
      N'targetawaygoals-2026-08-09-trial-48',
      N'targetawayshots-2026-08-09-trial-59',
      N'targetawayshotsongoal-2026-08-09-trial-33',
      N'targethomegoals-2026-08-09-trial-15',
      N'targethomeshots-2026-08-09-trial-56',
      N'targethomeshotsongoal-2026-08-09-trial-18',
      N'targettotalcorners-2026-08-09-trial-56',
      N'targettotalgoals-2026-08-09-trial-53',
      N'targettotalshots-2026-08-09-trial-56',
      N'targettotalshotsongoal-2026-08-09-trial-54'
  );

GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_BotDecisionDate'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_BotDecisionDate
        ON dbo.AutomatedBotPickEvaluations(BotKey, Decision, MatchDate DESC)
        INCLUDE (MarketType, SelectedSide, SelectedOdds, FinalEdge, FinalExpectedValue, DataQualityScore);
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertAutomatedBotPickEvaluation
    @IdempotencyKey CHAR(64),
    @RunId UNIQUEIDENTIFIER,
    @BotKey NVARCHAR(50),
    @AutomationVersion NVARCHAR(50),
    @PartidoProximoCuotaId BIGINT,
    @ApiFootballFixtureId BIGINT = NULL,
    @MatchDate DATETIME2(0),
    @League NVARCHAR(200),
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @Source NVARCHAR(50),
    @SourceMarketType NVARCHAR(50),
    @MarketType NVARCHAR(50),
    @LineValue DECIMAL(6,2),
    @SelectedSide NVARCHAR(10) = NULL,
    @SelectedOdds DECIMAL(10,2) = NULL,
    @DecisionEngineType NVARCHAR(40),
    @Decision NVARCHAR(20),
    @BaseModelName NVARCHAR(120) = NULL,
    @BaseModelVersion NVARCHAR(120) = NULL,
    @BaseModelTrainedThroughUtc DATETIME2(0) = NULL,
    @FeatureSchemaVersion NVARCHAR(80),
    @ConfigurationVersion NVARCHAR(80),
    @BaseRawProbability DECIMAL(9,6) = NULL,
    @BaseCalibratedProbability DECIMAL(9,6) = NULL,
    @RawImpliedProbability DECIMAL(9,6) = NULL,
    @MarketNoVigProbability DECIMAL(9,6) = NULL,
    @FinalProbability DECIMAL(9,6) = NULL,
    @FinalEdge DECIMAL(9,6) = NULL,
    @FinalExpectedValue DECIMAL(9,6) = NULL,
    @RuleBasedConfidenceScore DECIMAL(9,6) = NULL,
    @ContextExpectedValue DECIMAL(12,4) = NULL,
    @ContextAgreementScore DECIMAL(9,6) = NULL,
    @DataQualityScore DECIMAL(9,6) = NULL,
    @DecisionReasonsJson NVARCHAR(MAX),
    @RiskFlagsJson NVARCHAR(MAX),
    @Explanation NVARCHAR(1000),
    @FeatureSnapshotJson NVARCHAR(MAX),
    @PublishedSelectionId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    MERGE dbo.AutomatedBotPickEvaluations WITH (HOLDLOCK) AS target
    USING (SELECT @IdempotencyKey AS IdempotencyKey) AS source
       ON target.IdempotencyKey = source.IdempotencyKey
    WHEN MATCHED THEN UPDATE SET
        RunId = @RunId,
        Decision = @Decision,
        SelectedSide = @SelectedSide,
        SelectedOdds = @SelectedOdds,
        BaseModelName = @BaseModelName,
        BaseModelVersion = @BaseModelVersion,
        BaseModelTrainedThroughUtc = @BaseModelTrainedThroughUtc,
        BaseRawProbability = @BaseRawProbability,
        BaseCalibratedProbability = @BaseCalibratedProbability,
        RawImpliedProbability = @RawImpliedProbability,
        MarketNoVigProbability = @MarketNoVigProbability,
        FinalProbability = @FinalProbability,
        FinalEdge = @FinalEdge,
        FinalExpectedValue = @FinalExpectedValue,
        RuleBasedConfidenceScore = @RuleBasedConfidenceScore,
        ContextExpectedValue = @ContextExpectedValue,
        ContextAgreementScore = @ContextAgreementScore,
        DataQualityScore = @DataQualityScore,
        DecisionReasonsJson = @DecisionReasonsJson,
        RiskFlagsJson = @RiskFlagsJson,
        Explanation = @Explanation,
        FeatureSnapshotJson = @FeatureSnapshotJson,
        PublishedSelectionId = @PublishedSelectionId,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT
    (
        IdempotencyKey, RunId, BotKey, AutomationVersion, PartidoProximoCuotaId,
        ApiFootballFixtureId, MatchDate, League, HomeTeam, AwayTeam, Source,
        SourceMarketType, MarketType, LineValue, SelectedSide, SelectedOdds,
        DecisionEngineType, Decision, BaseModelName, BaseModelVersion,
        BaseModelTrainedThroughUtc,
        FeatureSchemaVersion, ConfigurationVersion, BaseRawProbability,
        BaseCalibratedProbability, RawImpliedProbability, MarketNoVigProbability,
        FinalProbability, FinalEdge, FinalExpectedValue, RuleBasedConfidenceScore,
        ContextExpectedValue, ContextAgreementScore, DataQualityScore,
        DecisionReasonsJson, RiskFlagsJson, Explanation, FeatureSnapshotJson,
        PublishedSelectionId
    )
    VALUES
    (
        @IdempotencyKey, @RunId, @BotKey, @AutomationVersion, @PartidoProximoCuotaId,
        @ApiFootballFixtureId, @MatchDate, @League, @HomeTeam, @AwayTeam, @Source,
        @SourceMarketType, @MarketType, @LineValue, @SelectedSide, @SelectedOdds,
        @DecisionEngineType, @Decision, @BaseModelName, @BaseModelVersion,
        @BaseModelTrainedThroughUtc,
        @FeatureSchemaVersion, @ConfigurationVersion, @BaseRawProbability,
        @BaseCalibratedProbability, @RawImpliedProbability, @MarketNoVigProbability,
        @FinalProbability, @FinalEdge, @FinalExpectedValue, @RuleBasedConfidenceScore,
        @ContextExpectedValue, @ContextAgreementScore, @DataQualityScore,
        @DecisionReasonsJson, @RiskFlagsJson, @Explanation, @FeatureSnapshotJson,
        @PublishedSelectionId
    );
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_DeleteAutomatedBotDefinition
    @BotKey NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.AutomatedBotDefinitions
    WHERE BotKey = @BotKey
      AND IsBuiltIn = 0;

    SELECT @@ROWCOUNT;
END;

GO

IF OBJECT_ID(N'dbo.AutomatedRecommendationJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedRecommendationJobs
    (
        RecommendationJobId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_AutomatedRecommendationJobs PRIMARY KEY,
        Name NVARCHAR(150) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        Mode NVARCHAR(30) NOT NULL,
        DateFrom DATE NOT NULL,
        DateTo DATE NOT NULL,
        BotKeys NVARCHAR(500) NOT NULL,
        MarketFamilies NVARCHAR(200) NOT NULL,
        BatchSize INT NOT NULL,
        NextBatchNumber INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_NextBatch DEFAULT 1,
        TotalBatches INT NULL,
        ProcessedBatches INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Processed DEFAULT 0,
        SelectedMatches INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Selected DEFAULT 0,
        InsertedRows INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Inserted DEFAULT 0,
        UpdatedRows INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Updated DEFAULT 0,
        SkippedMatches INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Skipped DEFAULT 0,
        ErrorMatches INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Errors DEFAULT 0,
        AttemptCount INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Attempts DEFAULT 0,
        MaxAttempts INT NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_MaxAttempts DEFAULT 3,
        RequestHash CHAR(64) NOT NULL,
        LastRunId UNIQUEIDENTIFIER NULL,
        LastError NVARCHAR(2000) NULL,
        LeaseOwner NVARCHAR(150) NULL,
        LeaseExpiresAtUtc DATETIME2(0) NULL,
        NextAttemptAtUtc DATETIME2(0) NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_Created DEFAULT SYSUTCDATETIME(),
        StartedAtUtc DATETIME2(0) NULL,
        UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedRecommendationJobs_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc DATETIME2(0) NULL,
        CONSTRAINT CK_AutomatedRecommendationJobs_Status CHECK
            (Status IN (N'Queued', N'Running', N'Completed', N'Failed', N'Cancelled')),
        CONSTRAINT CK_AutomatedRecommendationJobs_Mode CHECK
            (Mode IN (N'HistoricalBackfill', N'Live')),
        CONSTRAINT CK_AutomatedRecommendationJobs_Dates CHECK (DateTo >= DateFrom),
        CONSTRAINT CK_AutomatedRecommendationJobs_BatchSize CHECK (BatchSize BETWEEN 1 AND 100),
        CONSTRAINT CK_AutomatedRecommendationJobs_MaxAttempts CHECK (MaxAttempts BETWEEN 1 AND 10)
    );
END;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedRecommendationJobs')
      AND name = N'IX_AutomatedRecommendationJobs_Queue'
)
BEGIN
    CREATE INDEX IX_AutomatedRecommendationJobs_Queue
        ON dbo.AutomatedRecommendationJobs(Status, NextAttemptAtUtc, LeaseExpiresAtUtc, CreatedAtUtc)
        INCLUDE (RequestHash, NextBatchNumber, TotalBatches);
END;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedRecommendationJobs')
      AND name = N'IX_AutomatedRecommendationJobs_RequestHash'
)
BEGIN
    CREATE INDEX IX_AutomatedRecommendationJobs_RequestHash
        ON dbo.AutomatedRecommendationJobs(RequestHash, CreatedAtUtc DESC)
        INCLUDE (Status);
END;

GO

CREATE OR ALTER VIEW dbo.vw_AutomatedRecommendationJobs
AS
    SELECT
        RecommendationJobId,
        Name,
        Status,
        Mode,
        DateFrom,
        DateTo,
        BotKeys,
        MarketFamilies,
        BatchSize,
        NextBatchNumber,
        TotalBatches,
        ProcessedBatches,
        SelectedMatches,
        InsertedRows,
        UpdatedRows,
        SkippedMatches,
        ErrorMatches,
        AttemptCount,
        MaxAttempts,
        LastRunId,
        LastError,
        CreatedAtUtc,
        StartedAtUtc,
        UpdatedAtUtc,
        CompletedAtUtc
    FROM dbo.AutomatedRecommendationJobs;

GO

CREATE OR ALTER PROCEDURE dbo.sp_EnqueueAutomatedRecommendationJob
    @RecommendationJobId UNIQUEIDENTIFIER,
    @Name NVARCHAR(150),
    @Mode NVARCHAR(30),
    @DateFrom DATE,
    @DateTo DATE,
    @BotKeys NVARCHAR(500),
    @MarketFamilies NVARCHAR(200),
    @BatchSize INT,
    @MaxAttempts INT,
    @RequestHash CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @ExistingJobId UNIQUEIDENTIFIER;
    SELECT TOP (1) @ExistingJobId = RecommendationJobId
    FROM dbo.AutomatedRecommendationJobs WITH (UPDLOCK, HOLDLOCK)
    WHERE RequestHash = @RequestHash
      AND Status IN (N'Queued', N'Running')
    ORDER BY CreatedAtUtc DESC;

    IF @ExistingJobId IS NULL
    BEGIN
        INSERT dbo.AutomatedRecommendationJobs
        (
            RecommendationJobId,
            Name,
            Status,
            Mode,
            DateFrom,
            DateTo,
            BotKeys,
            MarketFamilies,
            BatchSize,
            NextBatchNumber,
            MaxAttempts,
            RequestHash
        )
        VALUES
        (
            @RecommendationJobId,
            @Name,
            N'Queued',
            @Mode,
            @DateFrom,
            @DateTo,
            @BotKeys,
            @MarketFamilies,
            @BatchSize,
            1,
            @MaxAttempts,
            @RequestHash
        );
    END
    ELSE
    BEGIN
        SET @RecommendationJobId = @ExistingJobId;
    END;

    COMMIT TRANSACTION;

    SELECT *
    FROM dbo.vw_AutomatedRecommendationJobs
    WHERE RecommendationJobId = @RecommendationJobId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAutomatedRecommendationJob
    @RecommendationJobId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM dbo.vw_AutomatedRecommendationJobs
    WHERE RecommendationJobId = @RecommendationJobId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_ListAutomatedRecommendationJobs
    @Take INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (CASE WHEN @Take BETWEEN 1 AND 200 THEN @Take ELSE 50 END) *
    FROM dbo.vw_AutomatedRecommendationJobs
    ORDER BY CreatedAtUtc DESC;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_ClaimNextAutomatedRecommendationJob
    @WorkerId NVARCHAR(150),
    @LeaseSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @RecommendationJobId UNIQUEIDENTIFIER;
    SELECT TOP (1) @RecommendationJobId = RecommendationJobId
    FROM dbo.AutomatedRecommendationJobs WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE
        (
            Status = N'Queued'
            OR
            (Status = N'Running' AND LeaseExpiresAtUtc < SYSUTCDATETIME())
        )
      AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= SYSUTCDATETIME())
    -- Fair queue: once a batch finishes its UpdatedAtUtc moves to the end,
    -- allowing a manual job queued meanwhile to run before the next batch.
    ORDER BY UpdatedAtUtc, CreatedAtUtc, RecommendationJobId;

    IF @RecommendationJobId IS NOT NULL
    BEGIN
        UPDATE dbo.AutomatedRecommendationJobs
        SET
            Status = N'Running',
            LeaseOwner = @WorkerId,
            LeaseExpiresAtUtc = DATEADD(SECOND, @LeaseSeconds, SYSUTCDATETIME()),
            StartedAtUtc = COALESCE(StartedAtUtc, SYSUTCDATETIME()),
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE RecommendationJobId = @RecommendationJobId;
    END;

    COMMIT TRANSACTION;

    SELECT *
    FROM dbo.vw_AutomatedRecommendationJobs
    WHERE RecommendationJobId = @RecommendationJobId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_HeartbeatAutomatedRecommendationJob
    @RecommendationJobId UNIQUEIDENTIFIER,
    @WorkerId NVARCHAR(150),
    @LeaseSeconds INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AutomatedRecommendationJobs
    SET
        LeaseExpiresAtUtc = DATEADD(SECOND, @LeaseSeconds, SYSUTCDATETIME()),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE RecommendationJobId = @RecommendationJobId
      AND Status = N'Running'
      AND LeaseOwner = @WorkerId;

    SELECT @@ROWCOUNT;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_CompleteAutomatedRecommendationJobBatch
    @RecommendationJobId UNIQUEIDENTIFIER,
    @WorkerId NVARCHAR(150),
    @CompletedBatchNumber INT,
    @TotalBatches INT,
    @RunId UNIQUEIDENTIFIER,
    @SelectedMatches INT,
    @InsertedRows INT,
    @UpdatedRows INT,
    @SkippedMatches INT,
    @ErrorMatches INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsComplete BIT = CASE WHEN @TotalBatches <= @CompletedBatchNumber THEN 1 ELSE 0 END;

    UPDATE dbo.AutomatedRecommendationJobs
    SET
        Status = CASE WHEN @IsComplete = 1 THEN N'Completed' ELSE N'Queued' END,
        NextBatchNumber = @CompletedBatchNumber + 1,
        TotalBatches = @TotalBatches,
        ProcessedBatches = ProcessedBatches + 1,
        SelectedMatches = SelectedMatches + @SelectedMatches,
        InsertedRows = InsertedRows + @InsertedRows,
        UpdatedRows = UpdatedRows + @UpdatedRows,
        SkippedMatches = SkippedMatches + @SkippedMatches,
        ErrorMatches = ErrorMatches + @ErrorMatches,
        AttemptCount = 0,
        LastRunId = @RunId,
        LastError = NULL,
        LeaseOwner = NULL,
        LeaseExpiresAtUtc = NULL,
        NextAttemptAtUtc = NULL,
        UpdatedAtUtc = SYSUTCDATETIME(),
        CompletedAtUtc = CASE WHEN @IsComplete = 1 THEN SYSUTCDATETIME() ELSE NULL END
    WHERE RecommendationJobId = @RecommendationJobId
      AND Status = N'Running'
      AND LeaseOwner = @WorkerId;

    SELECT *
    FROM dbo.vw_AutomatedRecommendationJobs
    WHERE RecommendationJobId = @RecommendationJobId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_FailAutomatedRecommendationJobBatch
    @RecommendationJobId UNIQUEIDENTIFIER,
    @WorkerId NVARCHAR(150),
    @ErrorMessage NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AutomatedRecommendationJobs
    SET
        Status = CASE WHEN AttemptCount + 1 >= MaxAttempts THEN N'Failed' ELSE N'Queued' END,
        AttemptCount = AttemptCount + 1,
        LastError = @ErrorMessage,
        LeaseOwner = NULL,
        LeaseExpiresAtUtc = NULL,
        NextAttemptAtUtc = CASE
            WHEN AttemptCount + 1 >= MaxAttempts THEN NULL
            ELSE DATEADD(SECOND,
                CASE AttemptCount + 1
                    WHEN 1 THEN 30
                    WHEN 2 THEN 60
                    WHEN 3 THEN 120
                    ELSE 300
                END,
                SYSUTCDATETIME())
        END,
        UpdatedAtUtc = SYSUTCDATETIME(),
        CompletedAtUtc = CASE WHEN AttemptCount + 1 >= MaxAttempts THEN SYSUTCDATETIME() ELSE NULL END
    WHERE RecommendationJobId = @RecommendationJobId
      AND Status = N'Running'
      AND LeaseOwner = @WorkerId;

    SELECT *
    FROM dbo.vw_AutomatedRecommendationJobs
    WHERE RecommendationJobId = @RecommendationJobId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_CancelAutomatedRecommendationJob
    @RecommendationJobId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AutomatedRecommendationJobs
    SET
        Status = N'Cancelled',
        LeaseOwner = NULL,
        LeaseExpiresAtUtc = NULL,
        NextAttemptAtUtc = NULL,
        UpdatedAtUtc = SYSUTCDATETIME(),
        CompletedAtUtc = SYSUTCDATETIME()
    WHERE RecommendationJobId = @RecommendationJobId
      AND Status IN (N'Queued', N'Running');

    SELECT @@ROWCOUNT;
END;

GO

IF OBJECT_ID(N'dbo.CornerOddsSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CornerOddsSnapshots
    (
        CornerOddsSnapshotId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_CornerOddsSnapshots PRIMARY KEY,
        CapturedAtUtc DATETIME2(3) NOT NULL,
        Source NVARCHAR(50) NOT NULL,
        SourceMatchId NVARCHAR(100) NULL,
        SourceUrl NVARCHAR(1000) NULL,
        MatchDate DATETIME2(0) NOT NULL,
        League NVARCHAR(300) NOT NULL,
        StandardizedLeague NVARCHAR(300) NULL,
        HomeTeam NVARCHAR(300) NOT NULL,
        AwayTeam NVARCHAR(300) NOT NULL,
        StandardizedHomeTeam NVARCHAR(300) NULL,
        StandardizedAwayTeam NVARCHAR(300) NULL,
        HomeTeamGender NVARCHAR(1) NOT NULL,
        AwayTeamGender NVARCHAR(1) NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        LineValue DECIMAL(10,2) NOT NULL,
        OverOdds DECIMAL(18,6) NULL,
        UnderOdds DECIMAL(18,6) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_CornerOddsSnapshots_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CornerOddsSnapshots_MatchMarketCapture'
      AND object_id = OBJECT_ID(N'dbo.CornerOddsSnapshots')
)
BEGIN
    CREATE INDEX IX_CornerOddsSnapshots_MatchMarketCapture
        ON dbo.CornerOddsSnapshots(Source, MatchDate, MarketType, LineValue, CapturedAtUtc)
        INCLUDE
        (
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            OverOdds,
            UnderOdds
        );
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_VoidAutomatedCornerBetSelection
    @AutomatedCornerBetSelectionId BIGINT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NowUtc DATETIME2(0) = SYSUTCDATETIME();
    DECLARE @Reason NVARCHAR(500) = N'Pick anulado manualmente.';

    UPDATE dbo.AutomatedCornerBetSelections
    SET
        Status = N'Void',
        ActualHomeCorners = NULL,
        ActualAwayCorners = NULL,
        ActualTotalCorners = NULL,
        SettlementActualValue = NULL,
        SettlementFactor = CONVERT(DECIMAL(6,3), 0),
        SettlementReason = @Reason,
        SettlementSource = N'Manual',
        SettlementSnapshotJson = N'{"source":"Manual","action":"Void"}',
        LastSettlementCheckReason = @Reason,
        LastSettlementCheckAtUtc = @NowUtc,
        ProfitLoss = 0,
        YieldPct = 0,
        SettledAtUtc = @NowUtc,
        UpdatedAtUtc = @NowUtc
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
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

    IF @Status NOT IN (N'Pending', N'Won', N'Lost', N'Push', N'Void')
    BEGIN
        THROW 50000, 'Invalid status. Allowed: Pending, Won, Lost, Push, Void.', 1;
    END;

    DECLARE @NowUtc DATETIME2(0) = SYSUTCDATETIME();
    DECLARE @Reason NVARCHAR(500) = CASE
        WHEN @Status = N'Pending' THEN N'Pick reabierto manualmente.'
        ELSE CONCAT(N'Estado establecido manualmente: ', @Status, N'.')
    END;

    UPDATE dbo.AutomatedCornerBetSelections
    SET
        Status = @Status,
        ActualHomeCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualHomeCorners, ActualHomeCorners) END,
        ActualAwayCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualAwayCorners, ActualAwayCorners) END,
        ActualTotalCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualTotalCorners, ActualTotalCorners) END,
        SettlementActualValue = CASE WHEN @Status = N'Pending' THEN NULL ELSE SettlementActualValue END,
        SettlementFactor = CASE
            WHEN @Status = N'Pending' THEN NULL
            WHEN @Status = N'Won' THEN CONVERT(DECIMAL(6,3), 1)
            WHEN @Status = N'Lost' THEN CONVERT(DECIMAL(6,3), -1)
            ELSE CONVERT(DECIMAL(6,3), 0)
        END,
        SettlementReason = CASE WHEN @Status = N'Pending' THEN NULL ELSE @Reason END,
        SettlementSource = CASE WHEN @Status = N'Pending' THEN NULL ELSE N'Manual' END,
        SettlementSnapshotJson = CASE
            WHEN @Status = N'Pending' THEN NULL
            ELSE CONCAT(N'{"source":"Manual","action":"Status","status":"', @Status, N'"}')
        END,
        LastSettlementCheckReason = @Reason,
        LastSettlementCheckAtUtc = @NowUtc,
        ProfitLoss = CASE
            WHEN @Status = N'Won' THEN ROUND(Stake * (Odds - 1), 2)
            WHEN @Status = N'Lost' THEN ROUND(-Stake, 2)
            WHEN @Status IN (N'Push', N'Void') THEN 0
            ELSE NULL
        END,
        YieldPct = CASE
            WHEN @Status = N'Won' THEN ROUND(Odds - 1, 4)
            WHEN @Status = N'Lost' THEN -1
            WHEN @Status IN (N'Push', N'Void') THEN 0
            ELSE NULL
        END,
        SettledAtUtc = CASE WHEN @Status = N'Pending' THEN NULL ELSE @NowUtc END,
        UpdatedAtUtc = @NowUtc
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_ResolveAutomatedCornerBetSelection
    @AutomatedCornerBetSelectionId BIGINT,
    @ActualValue INT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ActualValue < 0
    BEGIN
        THROW 50000, 'Actual result must be zero or greater.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.AutomatedCornerBetSelections
        WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId
    )
    BEGIN
        SET @RowsAffected = 0;
        RETURN;
    END;

    DECLARE @SettlementFactor DECIMAL(6,3);
    DECLARE @SelectedSide NVARCHAR(10);
    DECLARE @LineValue DECIMAL(6,2);
    DECLARE @NowUtc DATETIME2(0) = SYSUTCDATETIME();

    SELECT
        @SelectedSide = SelectedSide,
        @LineValue = LineValue,
        @SettlementFactor =
            CAST(
                CASE
                    -- Asian quarter line x.25 = half on x.0 and half on x.5.
                    WHEN LineValue - FLOOR(LineValue) = 0.25
                     AND @ActualValue = FLOOR(LineValue)
                     AND SelectedSide = N'Over' THEN -0.5
                    WHEN LineValue - FLOOR(LineValue) = 0.25
                     AND @ActualValue = FLOOR(LineValue)
                     AND SelectedSide = N'Under' THEN 0.5
                    -- Asian quarter line x.75 = half on x.5 and half on the next integer.
                    WHEN LineValue - FLOOR(LineValue) = 0.75
                     AND @ActualValue = CEILING(LineValue)
                     AND SelectedSide = N'Over' THEN 0.5
                    WHEN LineValue - FLOOR(LineValue) = 0.75
                     AND @ActualValue = CEILING(LineValue)
                     AND SelectedSide = N'Under' THEN -0.5
                    WHEN @ActualValue = LineValue THEN 0
                    WHEN SelectedSide = N'Over' AND @ActualValue > LineValue THEN 1
                    WHEN SelectedSide = N'Under' AND @ActualValue < LineValue THEN 1
                    ELSE -1
                END
                AS DECIMAL(4,2))
    FROM dbo.AutomatedCornerBetSelections
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    DECLARE @SettlementReason NVARCHAR(500) = CONCAT(
        N'Liquidación manual: ',
        @SelectedSide,
        N' ',
        CONVERT(NVARCHAR(30), @LineValue),
        N', resultado ',
        CONVERT(NVARCHAR(20), @ActualValue),
        N', factor ',
        CONVERT(NVARCHAR(20), @SettlementFactor),
        N'.');

    UPDATE dbo.AutomatedCornerBetSelections
    SET
        ActualHomeCorners = CASE
            WHEN MarketType IN (N'HomeTeamCorners', N'HomeTeamGoals', N'HomeTeamShots', N'HomeTeamShotsOnGoal')
                THEN @ActualValue
            ELSE ActualHomeCorners
        END,
        ActualAwayCorners = CASE
            WHEN MarketType IN (N'AwayTeamCorners', N'AwayTeamGoals', N'AwayTeamShots', N'AwayTeamShotsOnGoal')
                THEN @ActualValue
            ELSE ActualAwayCorners
        END,
        ActualTotalCorners = CASE
            WHEN MarketType IN (N'TotalCorners', N'TotalGoals', N'TotalShots', N'TotalShotsOnGoal')
                THEN @ActualValue
            ELSE ActualTotalCorners
        END,
        SettlementActualValue = @ActualValue,
        SettlementFactor = @SettlementFactor,
        SettlementReason = @SettlementReason,
        SettlementSource = N'Manual',
        SettlementSnapshotJson = CONCAT(
            N'{"source":"Manual","actualValue":',
            CONVERT(NVARCHAR(20), @ActualValue),
            N',"factor":',
            CONVERT(NVARCHAR(20), @SettlementFactor),
            N'}'),
        LastSettlementCheckReason = @SettlementReason,
        LastSettlementCheckAtUtc = @NowUtc,
        Status = CASE
            WHEN @SettlementFactor > 0 THEN N'Won'
            WHEN @SettlementFactor < 0 THEN N'Lost'
            ELSE N'Push'
        END,
        ProfitLoss = CASE
            WHEN @SettlementFactor > 0 THEN ROUND(Stake * (Odds - 1) * @SettlementFactor, 2)
            WHEN @SettlementFactor < 0 THEN ROUND(Stake * @SettlementFactor, 2)
            ELSE 0
        END,
        YieldPct = CASE
            WHEN Stake = 0 THEN NULL
            WHEN @SettlementFactor > 0 THEN ROUND((Odds - 1) * @SettlementFactor, 4)
            ELSE @SettlementFactor
        END,
        SettledAtUtc = @NowUtc,
        UpdatedAtUtc = @NowUtc
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_DeleteAutomatedCornerBetSelection
    @AutomatedCornerBetSelectionId BIGINT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.AutomatedCornerBetSelections
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;

GO

IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'FlatStake') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedCornerBetSelections
        ADD FlatStake DECIMAL(10,2) NULL;
END;

IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'KellyFraction') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedCornerBetSelections
        ADD KellyFraction DECIMAL(9,6) NULL;
END;

IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'ApiFootballFixtureId') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD ApiFootballFixtureId BIGINT NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'MatchHistoryId') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD MatchHistoryId BIGINT NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementActualValue') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementActualValue INT NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementFactor') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementFactor DECIMAL(6,3) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementReason') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementReason NVARCHAR(500) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementSource') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementSource NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementMatchStatus') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementMatchStatus NVARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementSnapshotJson') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementSnapshotJson NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'LastSettlementCheckReason') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD LastSettlementCheckReason NVARCHAR(500) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'LastSettlementCheckAtUtc') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD LastSettlementCheckAtUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'BotKey') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD BotKey NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'LogicalPickKey') IS NULL
    ALTER TABLE dbo.AutomatedCornerBetSelections ADD LogicalPickKey VARBINARY(32) NULL;

GO

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
      AND name = N'CK_AutomatedCornerBetSelections_Status'
)
BEGIN
    ALTER TABLE dbo.AutomatedCornerBetSelections
        DROP CONSTRAINT CK_AutomatedCornerBetSelections_Status;
END;

ALTER TABLE dbo.AutomatedCornerBetSelections WITH CHECK
    ADD CONSTRAINT CK_AutomatedCornerBetSelections_Status
    CHECK (Status IN (N'Pending', N'Won', N'Lost', N'Push', N'Void'));

GO

-- Bot E 1.0.0/1.0.1 admitted calibration observations without a persisted
-- base-model trained-through cutoff. Preserve those rows for auditability, but
-- exclude their result from performance because the experiment is superseded
-- by 1.0.2, which enforces the cutoff for every historical observation.
UPDATE dbo.AutomatedCornerBetSelections
SET Status = N'Void',
    SettlementFactor = CONVERT(DECIMAL(6,3), 0),
    SettlementReason = N'Experimento reemplazado: Bot E anterior a 1.0.2 no garantizaba el corte TrainedThrough del modelo base.',
    SettlementSource = N'SystemExperimentSuperseded',
    LastSettlementCheckReason = N'Anulado de forma auditable; volver a evaluar con Bot E 1.0.2.',
    LastSettlementCheckAtUtc = SYSUTCDATETIME(),
    ProfitLoss = CONVERT(DECIMAL(10,2), 0),
    YieldPct = CONVERT(DECIMAL(9,4), 0),
    SettledAtUtc = COALESCE(SettledAtUtc, SYSUTCDATETIME()),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE ISJSON(DecisionReason) = 1
  AND JSON_VALUE(DecisionReason, '$.botProfile') = N'E2026'
  AND COALESCE(
          JSON_VALUE(DecisionReason, '$.ConfigurationVersion'),
          JSON_VALUE(DecisionReason, '$.configurationVersion')) IN
      (N'bot-e-empirical-calibration-1.0.0', N'bot-e-empirical-calibration-1.0.1')
  AND
  (
      Status <> N'Void'
      OR ISNULL(SettlementSource, N'') <> N'SystemExperimentSuperseded'
  );

GO

UPDATE dbo.AutomatedCornerBetSelections
SET FlatStake = Stake
WHERE FlatStake IS NULL;

GO

-- A productive pick is unique per bot, fixture, market, side and line. Source,
-- quote and run are evidence attached to that pick, not separate recommendations.
UPDATE selection
SET BotKey = COALESCE
(
    NULLIF(UPPER(LTRIM(RTRIM(JSON_VALUE(
        CASE WHEN ISJSON(selection.DecisionReason) = 1
             THEN selection.DecisionReason ELSE N'{}' END,
        '$.botProfile')))), N''),
    CASE
        WHEN RIGHT(UPPER(LTRIM(RTRIM(selection.AutomationVersion))), 6) = N'-C2026' THEN N'C2026'
        WHEN RIGHT(UPPER(LTRIM(RTRIM(selection.AutomationVersion))), 6) = N'-D2026' THEN N'D2026'
        WHEN RIGHT(UPPER(LTRIM(RTRIM(selection.AutomationVersion))), 6) = N'-E2026' THEN N'E2026'
        WHEN RIGHT(UPPER(LTRIM(RTRIM(selection.AutomationVersion))), 6) = N'-F2026' THEN N'F2026'
        WHEN RIGHT(UPPER(LTRIM(RTRIM(selection.AutomationVersion))), 6) = N'-G2026' THEN N'G2026'
        WHEN RIGHT(UPPER(LTRIM(RTRIM(selection.AutomationVersion))), 2) = N'-A' THEN N'A'
        WHEN RIGHT(UPPER(LTRIM(RTRIM(selection.AutomationVersion))), 2) = N'-B' THEN N'B'
        ELSE UPPER(LTRIM(RTRIM(selection.AutomationVersion)))
    END
)
FROM dbo.AutomatedCornerBetSelections AS selection
WHERE selection.BotKey IS NULL OR LTRIM(RTRIM(selection.BotKey)) = N'';

-- Reconcile rows that were originally published without an official fixture id
-- when the same canonical fixture has since acquired one unambiguous id.
;WITH OfficialFixture AS
(
    SELECT
        MatchDay = CAST(MatchDate AS DATE),
        HomeKey = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)))),
        AwayKey = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam)))),
        ApiFootballFixtureId = MIN(ApiFootballFixtureId)
    FROM dbo.AutomatedCornerBetSelections
    WHERE ApiFootballFixtureId IS NOT NULL
    GROUP BY
        CAST(MatchDate AS DATE),
        UPPER(LTRIM(RTRIM(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)))),
        UPPER(LTRIM(RTRIM(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam))))
    HAVING MIN(ApiFootballFixtureId) = MAX(ApiFootballFixtureId)
)
UPDATE selection
SET ApiFootballFixtureId = fixture.ApiFootballFixtureId
FROM dbo.AutomatedCornerBetSelections AS selection
INNER JOIN OfficialFixture AS fixture
    ON fixture.MatchDay = CAST(selection.MatchDate AS DATE)
   AND fixture.HomeKey = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(selection.StandardizedHomeTeam, N''), selection.HomeTeam))))
   AND fixture.AwayKey = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(selection.StandardizedAwayTeam, N''), selection.AwayTeam))))
WHERE selection.ApiFootballFixtureId IS NULL;

UPDATE selection
SET LogicalPickKey = HASHBYTES
(
    'SHA2_256',
    CONCAT
    (
        UPPER(LTRIM(RTRIM(selection.BotKey))), N'|',
        CASE
            WHEN selection.ApiFootballFixtureId IS NOT NULL
                THEN CONCAT(N'ID|', CONVERT(NVARCHAR(30), selection.ApiFootballFixtureId))
            ELSE CONCAT
            (
                N'FALLBACK|', CONVERT(NVARCHAR(10), CAST(selection.MatchDate AS DATE), 23), N'|',
                UPPER(LTRIM(RTRIM(COALESCE(NULLIF(selection.StandardizedHomeTeam, N''), selection.HomeTeam)))), N'|',
                UPPER(LTRIM(RTRIM(COALESCE(NULLIF(selection.StandardizedAwayTeam, N''), selection.AwayTeam))))
            )
        END, N'|',
        UPPER(LTRIM(RTRIM(selection.MarketType))), N'|',
        UPPER(LTRIM(RTRIM(selection.SelectedSide))), N'|',
        CONVERT(NVARCHAR(30), CONVERT(DECIMAL(6,2), selection.LineValue))
    )
)
FROM dbo.AutomatedCornerBetSelections AS selection
WHERE selection.LogicalPickKey IS NULL;

IF EXISTS
(
    SELECT 1
    FROM dbo.AutomatedCornerBetSelections
    WHERE BotKey IS NULL OR LogicalPickKey IS NULL
)
    THROW 51050, 'Could not derive the logical identity of every automated pick.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
      AND name = N'BotKey'
      AND is_nullable = 1
)
    ALTER TABLE dbo.AutomatedCornerBetSelections ALTER COLUMN BotKey NVARCHAR(50) NOT NULL;

IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
      AND name = N'LogicalPickKey'
      AND is_nullable = 1
)
    ALTER TABLE dbo.AutomatedCornerBetSelections ALTER COLUMN LogicalPickKey VARBINARY(32) NOT NULL;

IF OBJECT_ID(N'dbo.AutomatedCornerBetSelectionDeduplicationAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedCornerBetSelectionDeduplicationAudit
    (
        AutomatedCornerBetSelectionDeduplicationAuditId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AutomatedCornerBetSelectionDeduplicationAudit PRIMARY KEY,
        RemovedSelectionId BIGINT NOT NULL,
        SurvivorSelectionId BIGINT NOT NULL,
        BotKey NVARCHAR(50) NOT NULL,
        LogicalPickKey VARBINARY(32) NOT NULL,
        RemovedStatus NVARCHAR(20) NOT NULL,
        RemovedSnapshotJson NVARCHAR(MAX) NOT NULL,
        RemovedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelectionDeduplicationAudit_RemovedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_AutomatedCornerBetSelectionDeduplicationAudit_RemovedSelectionId
            UNIQUE (RemovedSelectionId)
    );
END;

DECLARE @DuplicateSelections TABLE
(
    RemovedSelectionId BIGINT NOT NULL PRIMARY KEY,
    SurvivorSelectionId BIGINT NOT NULL,
    BotKey NVARCHAR(50) NOT NULL,
    LogicalPickKey VARBINARY(32) NOT NULL
);

;WITH SettlementConsistency AS
(
    SELECT
        BotKey,
        LogicalPickKey,
        MinimumSignature = MIN(CONCAT(
            Status, N'|', COALESCE(CONVERT(NVARCHAR(20), SettlementActualValue), N'NULL'), N'|',
            COALESCE(CONVERT(NVARCHAR(30), SettlementFactor), N'NULL'))),
        MaximumSignature = MAX(CONCAT(
            Status, N'|', COALESCE(CONVERT(NVARCHAR(20), SettlementActualValue), N'NULL'), N'|',
            COALESCE(CONVERT(NVARCHAR(30), SettlementFactor), N'NULL')))
    FROM dbo.AutomatedCornerBetSelections
    GROUP BY BotKey, LogicalPickKey
    HAVING COUNT_BIG(*) > 1
),
RankedDuplicateSelections AS
(
    SELECT
        selection.AutomatedCornerBetSelectionId,
        selection.BotKey,
        selection.LogicalPickKey,
        rn = ROW_NUMBER() OVER
        (
            PARTITION BY
                selection.BotKey,
                selection.LogicalPickKey
            ORDER BY
                CASE
                    WHEN selection.Status IN (N'Won', N'Lost', N'Push') THEN 0
                    WHEN selection.Status = N'Pending' THEN 1
                    ELSE 2
                END,
                CASE WHEN selection.MatchHistoryId IS NOT NULL THEN 0 ELSE 1 END,
                CASE WHEN consistency.MinimumSignature <> consistency.MaximumSignature
                     THEN selection.SettledAtUtc END DESC,
                selection.Odds DESC,
                selection.SettledAtUtc DESC,
                selection.UpdatedAtUtc DESC,
                selection.AutomatedCornerBetSelectionId DESC
        )
    FROM dbo.AutomatedCornerBetSelections AS selection
    INNER JOIN SettlementConsistency AS consistency
        ON consistency.BotKey = selection.BotKey
       AND consistency.LogicalPickKey = selection.LogicalPickKey
)
INSERT INTO @DuplicateSelections
(
    RemovedSelectionId,
    SurvivorSelectionId,
    BotKey,
    LogicalPickKey
)
SELECT
    duplicate.AutomatedCornerBetSelectionId,
    survivor.AutomatedCornerBetSelectionId,
    duplicate.BotKey,
    duplicate.LogicalPickKey
FROM RankedDuplicateSelections AS duplicate
INNER JOIN RankedDuplicateSelections AS survivor
    ON survivor.BotKey = duplicate.BotKey
   AND survivor.LogicalPickKey = duplicate.LogicalPickKey
   AND survivor.rn = 1
WHERE duplicate.rn > 1;

INSERT INTO dbo.AutomatedCornerBetSelectionDeduplicationAudit
(
    RemovedSelectionId,
    SurvivorSelectionId,
    BotKey,
    LogicalPickKey,
    RemovedStatus,
    RemovedSnapshotJson
)
SELECT
    mapping.RemovedSelectionId,
    mapping.SurvivorSelectionId,
    mapping.BotKey,
    mapping.LogicalPickKey,
    removed.Status,
    (
        SELECT removed.*
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    )
FROM @DuplicateSelections AS mapping
INNER JOIN dbo.AutomatedCornerBetSelections AS removed
    ON removed.AutomatedCornerBetSelectionId = mapping.RemovedSelectionId
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.AutomatedCornerBetSelectionDeduplicationAudit AS audit
    WHERE audit.RemovedSelectionId = mapping.RemovedSelectionId
);

UPDATE evaluation
SET PublishedSelectionId = mapping.SurvivorSelectionId,
    UpdatedAtUtc = SYSUTCDATETIME()
FROM dbo.AutomatedBotPickEvaluations AS evaluation
INNER JOIN @DuplicateSelections AS mapping
    ON mapping.RemovedSelectionId = evaluation.PublishedSelectionId;

DELETE removed
FROM dbo.AutomatedCornerBetSelections AS removed
INNER JOIN @DuplicateSelections AS mapping
    ON mapping.RemovedSelectionId = removed.AutomatedCornerBetSelectionId;

GO

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_AutomatedCornerBetSelections_Match'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
)
BEGIN
    DROP INDEX UX_AutomatedCornerBetSelections_Match
        ON dbo.AutomatedCornerBetSelections;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_AutomatedCornerBetSelections_LogicalPick'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
)
BEGIN
    CREATE UNIQUE INDEX UX_AutomatedCornerBetSelections_LogicalPick
        ON dbo.AutomatedCornerBetSelections(BotKey, LogicalPickKey);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AutomatedCornerBetSelections_StatusDate'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
)
BEGIN
    CREATE INDEX IX_AutomatedCornerBetSelections_StatusDate
        ON dbo.AutomatedCornerBetSelections(Status, MatchDate)
        INCLUDE (League, StandardizedLeague, HomeTeam, AwayTeam, LineValue, Odds, Stake, ProfitLoss);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AutomatedCornerBetSelections_ApiFootballFixtureId'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
)
BEGIN
    CREATE INDEX IX_AutomatedCornerBetSelections_ApiFootballFixtureId
        ON dbo.AutomatedCornerBetSelections(ApiFootballFixtureId, Status)
        INCLUDE (MatchHistoryId, MarketType, SelectedSide, LineValue, Odds, Stake, MatchDate)
        WHERE ApiFootballFixtureId IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AutomatedCornerBetSelections_PendingSettlement'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
)
BEGIN
    CREATE INDEX IX_AutomatedCornerBetSelections_PendingSettlement
        ON dbo.AutomatedCornerBetSelections(Status, MatchDate, AutomatedCornerBetSelectionId)
        INCLUDE (MatchHistoryId, ApiFootballFixtureId, MarketType, SelectedSide, LineValue, Odds, Stake);
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertAutomatedCornerBetSelection
    @RunId UNIQUEIDENTIFIER,
    @AutomationVersion NVARCHAR(50),
    @Source NVARCHAR(50),
    @SourceMatchId NVARCHAR(100) = NULL,
    @ApiFootballFixtureId BIGINT = NULL,
    @SourceUrl NVARCHAR(500) = NULL,
    @MatchDate DATETIME2(0),
    @League NVARCHAR(200),
    @StandardizedLeague NVARCHAR(200) = NULL,
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @StandardizedHomeTeam NVARCHAR(150) = NULL,
    @StandardizedAwayTeam NVARCHAR(150) = NULL,
    @HomeTeamGender CHAR(1) = 'M',
    @AwayTeamGender CHAR(1) = 'M',
    @SourceMarketType NVARCHAR(50) = N'CornersTotal',
    @MarketType NVARCHAR(50) = N'TotalCorners',
    @LineValue DECIMAL(6,2),
    @SelectedSide NVARCHAR(10),
    @Odds DECIMAL(10,2),
    @Stake DECIMAL(10,2),
    @FlatStake DECIMAL(10,2) = NULL,
    @ImpliedProbability DECIMAL(9,6) = NULL,
    @ModelProbability DECIMAL(9,6) = NULL,
    @ProbabilityEdge DECIMAL(9,6) = NULL,
    @ExpectedValue DECIMAL(9,6) = NULL,
    @KellyFraction DECIMAL(9,6) = NULL,
    @SelectionScore DECIMAL(9,6) = NULL,
    @PredictedTotalCorners DECIMAL(9,4) = NULL,
    @PredTotalDirect DECIMAL(9,4) = NULL,
    @PredHomeCorners DECIMAL(9,4) = NULL,
    @PredAwayCorners DECIMAL(9,4) = NULL,
    @PredTotalCombined DECIMAL(9,4) = NULL,
    @DistanceToLine DECIMAL(9,4) = NULL,
    @ConfidenceLevel NVARCHAR(20) = NULL,
    @OverUnderConfidenceLevel NVARCHAR(20) = NULL,
    @ModelConsensus NVARCHAR(20) = NULL,
    @ContextTotalCorners DECIMAL(9,4) = NULL,
    @ContextDifference DECIMAL(9,4) = NULL,
    @RecommendedSide NVARCHAR(10) = NULL,
    @DecisionReason NVARCHAR(MAX) = NULL,
    @AutomatedCornerBetSelectionId BIGINT OUTPUT,
    @MergeAction NVARCHAR(10) OUTPUT,
    @BotKey NVARCHAR(50) = NULL,
    @BotGCandidateId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @EffectiveBotKey NVARCHAR(50) = COALESCE
    (
        NULLIF(UPPER(LTRIM(RTRIM(@BotKey))), N''),
        NULLIF(UPPER(LTRIM(RTRIM(JSON_VALUE(
            CASE WHEN ISJSON(@DecisionReason) = 1
                 THEN @DecisionReason ELSE N'{}' END,
            '$.botProfile')))), N''),
        CASE
            WHEN RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 6) = N'-C2026' THEN N'C2026'
            WHEN RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 6) = N'-D2026' THEN N'D2026'
            WHEN RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 6) = N'-E2026' THEN N'E2026'
            WHEN RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 6) = N'-F2026' THEN N'F2026'
            WHEN RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 6) = N'-G2026' THEN N'G2026'
            WHEN RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 2) = N'-A' THEN N'A'
            WHEN RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 2) = N'-B' THEN N'B'
            ELSE UPPER(LTRIM(RTRIM(@AutomationVersion)))
        END
    );

    IF NULLIF(@EffectiveBotKey, N'') IS NULL
        THROW 51051, 'A stable bot key is required to publish an automated pick.', 1;

    DECLARE @LogicalPickKey VARBINARY(32) = HASHBYTES
    (
        'SHA2_256',
        CONCAT
        (
            @EffectiveBotKey, N'|',
            CASE
                WHEN @ApiFootballFixtureId IS NOT NULL
                    THEN CONCAT(N'ID|', CONVERT(NVARCHAR(30), @ApiFootballFixtureId))
                ELSE CONCAT
                (
                    N'FALLBACK|', CONVERT(NVARCHAR(10), CAST(@MatchDate AS DATE), 23), N'|',
                    UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedHomeTeam, N''), @HomeTeam)))), N'|',
                    UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedAwayTeam, N''), @AwayTeam))))
                )
            END, N'|',
            UPPER(LTRIM(RTRIM(@MarketType))), N'|',
            UPPER(LTRIM(RTRIM(@SelectedSide))), N'|',
            CONVERT(NVARCHAR(30), CONVERT(DECIMAL(6,2), @LineValue))
        )
    );

    DECLARE @IsBotG BIT = IIF
    (
        @EffectiveBotKey = N'G2026'
        OR UPPER(LTRIM(RTRIM(@AutomationVersion))) = N'G2026'
        OR RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 6) = N'-G2026',
        1,
        0
    );

    -- Preserve the pre-existing A-F transaction semantics. Atomic fail-closed
    -- publication is required only for the isolated G branch.
    IF @IsBotG = 1 SET XACT_ABORT ON;

    DECLARE @StartedBotGTransaction BIT = 0;
    DECLARE @StartedDeduplicationTransaction BIT = 0;
    DECLARE @BotGFixtureIdentity BIGINT = NULL;

    BEGIN TRY
        -- The application selector emits one winner, but publication can be retried
        -- concurrently by separate workers/bookmakers.  Serialize only G for this
        -- canonical fixture so the guard below and the MERGE are one atomic decision.
        IF @IsBotG = 1
        BEGIN
            IF @@TRANCOUNT = 0
            BEGIN
                BEGIN TRANSACTION;
                SET @StartedBotGTransaction = 1;
            END;

            SELECT @BotGFixtureIdentity = auditCandidate.FixtureIdentity
            FROM dbo.AutomatedBotPickEvaluations AS auditCandidate WITH (UPDLOCK, HOLDLOCK)
            WHERE auditCandidate.AutomatedBotPickEvaluationId = @BotGCandidateId
              AND auditCandidate.BotKey = N'G2026';

            IF @BotGFixtureIdentity IS NULL
                THROW 51043, 'Bot G2026 publication requires a persisted candidate with canonical fixture identity.', 1;

            -- Always lock the canonical audit identity. Official-id enrichment can
            -- therefore never create a second concurrency namespace.
            DECLARE @BotGFixtureLockResource NVARCHAR(255) = CONCAT
            (
                N'BotG2026:fixture-identity:',
                CONVERT(NVARCHAR(30), @BotGFixtureIdentity)
            );
            DECLARE @BotGFixtureLockResult INT;

            EXEC @BotGFixtureLockResult = sys.sp_getapplock
                @Resource = @BotGFixtureLockResource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;

            IF @BotGFixtureLockResult < 0
                THROW 51042, 'Bot G2026 could not acquire the fixture publication lock.', 1;
        END;

        IF @IsBotG = 0
        BEGIN
            IF @@TRANCOUNT = 0
            BEGIN
                BEGIN TRANSACTION;
                SET @StartedDeduplicationTransaction = 1;
            END;

            -- Use the canonical date/team identity for the lock even when an
            -- official id exists, so enrichment cannot create a second namespace.
            DECLARE @LogicalPickLockResource NVARCHAR(255) = CONCAT
            (
                N'AutomatedPick:', @EffectiveBotKey, N':',
                CONVERT(NVARCHAR(10), CAST(@MatchDate AS DATE), 23), N':',
                UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedHomeTeam, N''), @HomeTeam)))), N':',
                UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedAwayTeam, N''), @AwayTeam)))), N':',
                UPPER(LTRIM(RTRIM(@MarketType))), N':',
                UPPER(LTRIM(RTRIM(@SelectedSide))), N':',
                CONVERT(NVARCHAR(30), CONVERT(DECIMAL(6,2), @LineValue))
            );
            DECLARE @LogicalPickLockResult INT;

            EXEC @LogicalPickLockResult = sys.sp_getapplock
                @Resource = @LogicalPickLockResource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;

            IF @LogicalPickLockResult < 0
                THROW 51052, 'Could not acquire the logical automated-pick lock.', 1;

            DECLARE @ExistingLogicalSelectionId BIGINT = NULL;
            SELECT TOP (1)
                @ExistingLogicalSelectionId = existing.AutomatedCornerBetSelectionId
            FROM dbo.AutomatedCornerBetSelections AS existing WITH (UPDLOCK, HOLDLOCK)
            WHERE existing.BotKey = @EffectiveBotKey
              AND existing.MarketType = @MarketType
              AND existing.SelectedSide = @SelectedSide
              AND existing.LineValue = @LineValue
              AND
              (
                  existing.LogicalPickKey = @LogicalPickKey
                  OR
                  (
                      @ApiFootballFixtureId IS NOT NULL
                      AND existing.ApiFootballFixtureId = @ApiFootballFixtureId
                  )
                  OR
                  (
                      CAST(existing.MatchDate AS DATE) = CAST(@MatchDate AS DATE)
                      AND UPPER(LTRIM(RTRIM(COALESCE(NULLIF(existing.StandardizedHomeTeam, N''), existing.HomeTeam)))) =
                          UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedHomeTeam, N''), @HomeTeam))))
                      AND UPPER(LTRIM(RTRIM(COALESCE(NULLIF(existing.StandardizedAwayTeam, N''), existing.AwayTeam)))) =
                          UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedAwayTeam, N''), @AwayTeam))))
                  )
              )
            ORDER BY
                CASE
                    WHEN existing.Status IN (N'Won', N'Lost', N'Push') THEN 0
                    WHEN existing.Status = N'Pending' THEN 1
                    ELSE 2
                END,
                CASE WHEN existing.MatchHistoryId IS NOT NULL THEN 0 ELSE 1 END,
                existing.Odds DESC,
                existing.AutomatedCornerBetSelectionId DESC;

            IF @ExistingLogicalSelectionId IS NOT NULL
            BEGIN
                -- Reuse the original productive record. Only enrich immutable
                -- fixture identity; never overwrite its quote, result or lineage.
                UPDATE dbo.AutomatedCornerBetSelections
                SET ApiFootballFixtureId = COALESCE(ApiFootballFixtureId, @ApiFootballFixtureId),
                    LogicalPickKey = CASE
                        WHEN ApiFootballFixtureId IS NULL AND @ApiFootballFixtureId IS NOT NULL
                            THEN @LogicalPickKey
                        ELSE LogicalPickKey
                    END,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE AutomatedCornerBetSelectionId = @ExistingLogicalSelectionId;

                SET @AutomatedCornerBetSelectionId = @ExistingLogicalSelectionId;
                SET @MergeAction = N'UPDATE';

                IF @StartedDeduplicationTransaction = 1 AND XACT_STATE() = 1
                    COMMIT TRANSACTION;
                RETURN;
            END;
        END;

    IF @IsBotG = 1
       AND
       (
           @BotGCandidateId IS NULL
           OR NOT EXISTS
           (
               SELECT 1
               FROM dbo.AutomatedBotPickEvaluations AS auditCandidate
               WHERE auditCandidate.AutomatedBotPickEvaluationId = @BotGCandidateId
                 AND auditCandidate.BotKey = N'G2026'
                 AND auditCandidate.RunId = @RunId
                 AND auditCandidate.FixtureIdentity = @BotGFixtureIdentity
                 AND auditCandidate.Decision = N'Approved'
                 AND auditCandidate.AutomationVersion = @AutomationVersion
                 AND auditCandidate.Source = @Source
                 AND auditCandidate.MarketType = @MarketType
                 AND auditCandidate.LineValue = @LineValue
                 AND auditCandidate.SelectedSide = @SelectedSide
                 AND CONVERT(DECIMAL(10,2), auditCandidate.SelectedOdds) = @Odds
                 AND auditCandidate.MatchDate = @MatchDate
                 AND UPPER(LTRIM(RTRIM(auditCandidate.League))) =
                     UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedLeague, N''), @League))))
                 AND UPPER(LTRIM(RTRIM(auditCandidate.HomeTeam))) =
                     UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedHomeTeam, N''), @HomeTeam))))
                 AND UPPER(LTRIM(RTRIM(auditCandidate.AwayTeam))) =
                     UPPER(LTRIM(RTRIM(COALESCE(NULLIF(@StandardizedAwayTeam, N''), @AwayTeam))))
                 AND
                 (
                     (@ApiFootballFixtureId IS NOT NULL
                      AND auditCandidate.ApiFootballFixtureId = @ApiFootballFixtureId)
                     OR
                     (@ApiFootballFixtureId IS NULL
                     AND auditCandidate.ApiFootballFixtureId IS NULL)
                 )
                 AND auditCandidate.RawImpliedProbability IS NOT NULL
                 AND auditCandidate.ConservativeProbability IS NOT NULL
                 AND auditCandidate.ConservativeEdge IS NOT NULL
                 AND auditCandidate.ConservativeExpectedValue IS NOT NULL
                 AND auditCandidate.GSelectionScore IS NOT NULL
                 AND auditCandidate.StakeUnits > 0
           )
       )
        THROW 51043, 'Bot G2026 publication requires the matching persisted Approved audit candidate.', 1;

    -- For G the persisted audit decision is authoritative. Never allow a caller
    -- to publish different probabilities, economics, ranking or stake.
    IF @IsBotG = 1
    BEGIN
        SELECT
            @ImpliedProbability = auditCandidate.RawImpliedProbability,
            @ModelProbability = auditCandidate.ConservativeProbability,
            @ProbabilityEdge = auditCandidate.ConservativeEdge,
            @ExpectedValue = auditCandidate.ConservativeExpectedValue,
            @SelectionScore = auditCandidate.GSelectionScore,
            @Stake = auditCandidate.StakeUnits,
            @FlatStake = auditCandidate.StakeUnits
        FROM dbo.AutomatedBotPickEvaluations AS auditCandidate WITH (UPDLOCK, HOLDLOCK)
        WHERE auditCandidate.AutomatedBotPickEvaluationId = @BotGCandidateId
          AND auditCandidate.BotKey = N'G2026';
    END;

    IF @IsBotG = 1
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.AutomatedBotDefinitions
           WHERE BotKey = N'G2026'
             AND IsEnabled = 1
             AND PublishEnabled = 1
       )
    BEGIN
        THROW 51040, 'Bot G2026 publication is disabled. Shadow candidates cannot create productive picks.', 1;
    END;

    -- Published G decisions are immutable and limited to one pick per fixture
    -- across all bookmakers. An exact retry of the same version is the only update
    -- accepted; a different market, line, side or version is rejected.
    IF @IsBotG = 1
       AND EXISTS
       (
           SELECT 1
           FROM dbo.AutomatedCornerBetSelections AS existing
           WHERE
             (
                 UPPER(LTRIM(RTRIM(existing.AutomationVersion))) = N'G2026'
                 OR RIGHT(UPPER(LTRIM(RTRIM(existing.AutomationVersion))), 6) = N'-G2026'
             )
             AND
             (
                 (@ApiFootballFixtureId IS NOT NULL AND existing.ApiFootballFixtureId = @ApiFootballFixtureId)
                 OR
                 (
                     existing.MatchDate = @MatchDate
                     AND COALESCE(existing.StandardizedLeague, existing.League) =
                         COALESCE(@StandardizedLeague, @League)
                     AND COALESCE(existing.StandardizedHomeTeam, existing.HomeTeam) =
                         COALESCE(@StandardizedHomeTeam, @HomeTeam)
                     AND COALESCE(existing.StandardizedAwayTeam, existing.AwayTeam) =
                         COALESCE(@StandardizedAwayTeam, @AwayTeam)
                 )
             )
             AND
             (
                 (@ApiFootballFixtureId IS NOT NULL
                  AND existing.ApiFootballFixtureId IS NOT NULL
                  AND existing.ApiFootballFixtureId <> @ApiFootballFixtureId)
                 OR existing.AutomationVersion <> @AutomationVersion
                 OR existing.Source <> @Source
                 OR existing.MarketType <> @MarketType
                 OR existing.LineValue <> @LineValue
                 OR existing.SelectedSide <> @SelectedSide
                 OR existing.Odds <> @Odds
             )
       )
    BEGIN
        THROW 51041, 'Bot G2026 already has an immutable published pick for this fixture.', 1;
    END;

    DECLARE @Results TABLE
    (
        AutomatedCornerBetSelectionId BIGINT NOT NULL,
        MergeAction NVARCHAR(10) NOT NULL
    );

    MERGE dbo.AutomatedCornerBetSelections AS Target
    USING
    (
        SELECT
            RunId = @RunId,
            BotKey = @EffectiveBotKey,
            LogicalPickKey = @LogicalPickKey,
            AutomationVersion = @AutomationVersion,
            Source = @Source,
            SourceMatchId = @SourceMatchId,
            ApiFootballFixtureId = @ApiFootballFixtureId,
            SourceUrl = @SourceUrl,
            MatchDate = @MatchDate,
            League = @League,
            StandardizedLeague = @StandardizedLeague,
            HomeTeam = @HomeTeam,
            AwayTeam = @AwayTeam,
            StandardizedHomeTeam = @StandardizedHomeTeam,
            StandardizedAwayTeam = @StandardizedAwayTeam,
            HomeTeamGender = @HomeTeamGender,
            AwayTeamGender = @AwayTeamGender,
            SourceMarketType = @SourceMarketType,
            MarketType = @MarketType,
            LineValue = @LineValue,
            SelectedSide = @SelectedSide,
            Odds = @Odds,
            Stake = @Stake,
            FlatStake = COALESCE(@FlatStake, @Stake),
            ImpliedProbability = @ImpliedProbability,
            ModelProbability = @ModelProbability,
            ProbabilityEdge = @ProbabilityEdge,
            ExpectedValue = @ExpectedValue,
            KellyFraction = @KellyFraction,
            SelectionScore = @SelectionScore,
            PredictedTotalCorners = @PredictedTotalCorners,
            PredTotalDirect = @PredTotalDirect,
            PredHomeCorners = @PredHomeCorners,
            PredAwayCorners = @PredAwayCorners,
            PredTotalCombined = @PredTotalCombined,
            DistanceToLine = @DistanceToLine,
            ConfidenceLevel = @ConfidenceLevel,
            OverUnderConfidenceLevel = @OverUnderConfidenceLevel,
            ModelConsensus = @ModelConsensus,
            ContextTotalCorners = @ContextTotalCorners,
            ContextDifference = @ContextDifference,
            RecommendedSide = @RecommendedSide,
            DecisionReason = @DecisionReason
    ) AS Source
        ON
        (
            @IsBotG = 1
            AND Target.AutomationVersion = Source.AutomationVersion
            AND Target.Source = Source.Source
            AND Target.MarketType = Source.MarketType
            AND
            (
                Target.ApiFootballFixtureId = Source.ApiFootballFixtureId
                OR
                (
                    (Target.ApiFootballFixtureId IS NULL OR Source.ApiFootballFixtureId IS NULL)
                    AND Target.MatchDate = Source.MatchDate
                    AND COALESCE(Target.StandardizedLeague, Target.League) = COALESCE(Source.StandardizedLeague, Source.League)
                    AND COALESCE(Target.StandardizedHomeTeam, Target.HomeTeam) = COALESCE(Source.StandardizedHomeTeam, Source.HomeTeam)
                    AND COALESCE(Target.StandardizedAwayTeam, Target.AwayTeam) = COALESCE(Source.StandardizedAwayTeam, Source.AwayTeam)
                )
            )
        )
        OR
        (
            @IsBotG = 0
            AND Target.AutomationVersion = Source.AutomationVersion
            AND Target.Source = Source.Source
            AND Target.MarketType = Source.MarketType
            AND
            (
                (
                    NULLIF(LTRIM(RTRIM(Source.SourceMatchId)), N'') IS NOT NULL
                    AND Target.SourceMatchId = Source.SourceMatchId
                )
                OR
                (
                    NULLIF(LTRIM(RTRIM(Source.SourceMatchId)), N'') IS NULL
                    AND NULLIF(LTRIM(RTRIM(Source.SourceUrl)), N'') IS NOT NULL
                    AND Target.SourceUrl = Source.SourceUrl
                )
                OR
                (
                    NULLIF(LTRIM(RTRIM(Source.SourceMatchId)), N'') IS NULL
                    AND NULLIF(LTRIM(RTRIM(Source.SourceUrl)), N'') IS NULL
                    AND Target.MatchDate = Source.MatchDate
                    AND COALESCE(Target.StandardizedLeague, Target.League) = COALESCE(Source.StandardizedLeague, Source.League)
                    AND COALESCE(Target.StandardizedHomeTeam, Target.HomeTeam) = COALESCE(Source.StandardizedHomeTeam, Source.HomeTeam)
                    AND COALESCE(Target.StandardizedAwayTeam, Target.AwayTeam) = COALESCE(Source.StandardizedAwayTeam, Source.AwayTeam)
                )
            )
        )
    WHEN MATCHED THEN
        UPDATE SET
            Target.RunId = Source.RunId,
            Target.BotKey = Source.BotKey,
            Target.LogicalPickKey = Source.LogicalPickKey,
            Target.AutomationVersion = Source.AutomationVersion,
            Target.SourceMatchId = Source.SourceMatchId,
            Target.ApiFootballFixtureId = COALESCE(Target.ApiFootballFixtureId, Source.ApiFootballFixtureId),
            Target.SourceUrl = Source.SourceUrl,
            Target.League = Source.League,
            Target.StandardizedLeague = Source.StandardizedLeague,
            Target.HomeTeam = Source.HomeTeam,
            Target.AwayTeam = Source.AwayTeam,
            Target.StandardizedHomeTeam = Source.StandardizedHomeTeam,
            Target.StandardizedAwayTeam = Source.StandardizedAwayTeam,
            Target.HomeTeamGender = Source.HomeTeamGender,
            Target.AwayTeamGender = Source.AwayTeamGender,
            Target.SourceMarketType = Source.SourceMarketType,
            Target.LineValue = Source.LineValue,
            Target.SelectedSide = Source.SelectedSide,
            Target.Odds = Source.Odds,
            Target.Stake = Source.Stake,
            Target.FlatStake = Source.FlatStake,
            Target.ImpliedProbability = Source.ImpliedProbability,
            Target.ModelProbability = Source.ModelProbability,
            Target.ProbabilityEdge = Source.ProbabilityEdge,
            Target.ExpectedValue = Source.ExpectedValue,
            Target.KellyFraction = Source.KellyFraction,
            Target.SelectionScore = Source.SelectionScore,
            Target.PredictedTotalCorners = Source.PredictedTotalCorners,
            Target.PredTotalDirect = Source.PredTotalDirect,
            Target.PredHomeCorners = Source.PredHomeCorners,
            Target.PredAwayCorners = Source.PredAwayCorners,
            Target.PredTotalCombined = Source.PredTotalCombined,
            Target.DistanceToLine = Source.DistanceToLine,
            Target.ConfidenceLevel = Source.ConfidenceLevel,
            Target.OverUnderConfidenceLevel = Source.OverUnderConfidenceLevel,
            Target.ModelConsensus = Source.ModelConsensus,
            Target.ContextTotalCorners = Source.ContextTotalCorners,
            Target.ContextDifference = Source.ContextDifference,
            Target.RecommendedSide = Source.RecommendedSide,
            Target.DecisionReason = Source.DecisionReason,
            Target.Status = CASE WHEN Target.Status IN (N'Won', N'Lost', N'Push', N'Void') THEN Target.Status ELSE N'Pending' END,
            Target.UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            RunId,
            BotKey,
            LogicalPickKey,
            AutomationVersion,
            Source,
            SourceMatchId,
            ApiFootballFixtureId,
            SourceUrl,
            MatchDate,
            League,
            StandardizedLeague,
            HomeTeam,
            AwayTeam,
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            HomeTeamGender,
            AwayTeamGender,
            SourceMarketType,
            MarketType,
            LineValue,
            SelectedSide,
            Odds,
            Stake,
            FlatStake,
            ImpliedProbability,
            ModelProbability,
            ProbabilityEdge,
            ExpectedValue,
            KellyFraction,
            SelectionScore,
            PredictedTotalCorners,
            PredTotalDirect,
            PredHomeCorners,
            PredAwayCorners,
            PredTotalCombined,
            DistanceToLine,
            ConfidenceLevel,
            OverUnderConfidenceLevel,
            ModelConsensus,
            ContextTotalCorners,
            ContextDifference,
            RecommendedSide,
            DecisionReason,
            Status,
            CreatedAtUtc,
            UpdatedAtUtc
        )
        VALUES
        (
            Source.RunId,
            Source.BotKey,
            Source.LogicalPickKey,
            Source.AutomationVersion,
            Source.Source,
            Source.SourceMatchId,
            Source.ApiFootballFixtureId,
            Source.SourceUrl,
            Source.MatchDate,
            Source.League,
            Source.StandardizedLeague,
            Source.HomeTeam,
            Source.AwayTeam,
            Source.StandardizedHomeTeam,
            Source.StandardizedAwayTeam,
            Source.HomeTeamGender,
            Source.AwayTeamGender,
            Source.SourceMarketType,
            Source.MarketType,
            Source.LineValue,
            Source.SelectedSide,
            Source.Odds,
            Source.Stake,
            Source.FlatStake,
            Source.ImpliedProbability,
            Source.ModelProbability,
            Source.ProbabilityEdge,
            Source.ExpectedValue,
            Source.KellyFraction,
            Source.SelectionScore,
            Source.PredictedTotalCorners,
            Source.PredTotalDirect,
            Source.PredHomeCorners,
            Source.PredAwayCorners,
            Source.PredTotalCombined,
            Source.DistanceToLine,
            Source.ConfidenceLevel,
            Source.OverUnderConfidenceLevel,
            Source.ModelConsensus,
            Source.ContextTotalCorners,
            Source.ContextDifference,
            Source.RecommendedSide,
            Source.DecisionReason,
            N'Pending',
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        )
    OUTPUT inserted.AutomatedCornerBetSelectionId, $action
        INTO @Results (AutomatedCornerBetSelectionId, MergeAction);

    SELECT TOP (1)
        @AutomatedCornerBetSelectionId = AutomatedCornerBetSelectionId,
        @MergeAction = MergeAction
    FROM @Results;

        IF @IsBotG = 1
        BEGIN
            UPDATE dbo.AutomatedBotPickEvaluations
            SET Published = 1,
                PublicationStatus = N'Published',
                PublishedSelectionId = @AutomatedCornerBetSelectionId,
                PublicationLine = @LineValue,
                PublicationOdds = @Odds,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE AutomatedBotPickEvaluationId = @BotGCandidateId
              AND BotKey = N'G2026'
              AND Decision = N'Approved'
              AND (PublishedSelectionId IS NULL
                   OR PublishedSelectionId = @AutomatedCornerBetSelectionId);

            IF @@ROWCOUNT <> 1
                THROW 51044, 'Bot G2026 audit publication update failed; the productive pick was rolled back.', 1;
        END;

        IF @StartedBotGTransaction = 1 AND XACT_STATE() = 1
            COMMIT TRANSACTION;
        IF @StartedDeduplicationTransaction = 1 AND XACT_STATE() = 1
            COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF (@StartedBotGTransaction = 1 OR @StartedDeduplicationTransaction = 1)
           AND XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAutomatedCornerBetSelections
    @DateFrom DATE = NULL,
    @DateTo DATE = NULL,
    @Status NVARCHAR(20) = NULL,
    @League NVARCHAR(200) = NULL,
    @Source NVARCHAR(50) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @OnlyPending BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        AutomatedCornerBetSelectionId,
        RunId,
        BotKey,
        AutomationVersion,
        Source,
        SourceMatchId,
        ApiFootballFixtureId,
        MatchHistoryId,
        SourceUrl,
        MatchDate,
        League,
        StandardizedLeague,
        HomeTeam,
        AwayTeam,
        StandardizedHomeTeam,
        StandardizedAwayTeam,
        HomeTeamGender,
        AwayTeamGender,
        SourceMarketType,
        MarketType,
        LineValue,
        SelectedSide,
        Odds,
        Stake,
        FlatStake,
        ImpliedProbability,
        ModelProbability,
        ProbabilityEdge,
        ExpectedValue,
        KellyFraction,
        SelectionScore,
        PredictedTotalCorners,
        PredTotalDirect,
        PredHomeCorners,
        PredAwayCorners,
        PredTotalCombined,
        DistanceToLine,
        ConfidenceLevel,
        OverUnderConfidenceLevel,
        ModelConsensus,
        ContextTotalCorners,
        ContextDifference,
        RecommendedSide,
        Status,
        ActualHomeCorners,
        ActualAwayCorners,
        ActualTotalCorners,
        SettlementActualValue,
        SettlementFactor,
        SettlementReason,
        SettlementSource,
        SettlementMatchStatus,
        LastSettlementCheckReason,
        LastSettlementCheckAtUtc,
        ProfitLoss,
        YieldPct,
        DecisionReason,
        CreatedAtUtc,
        UpdatedAtUtc,
        SettledAtUtc
    FROM dbo.AutomatedCornerBetSelections
    WHERE (@DateFrom IS NULL OR CAST(MatchDate AS DATE) >= @DateFrom)
      AND (@DateTo IS NULL OR CAST(MatchDate AS DATE) <= @DateTo)
      AND (@Status IS NULL OR Status = @Status)
      AND (@League IS NULL OR COALESCE(StandardizedLeague, League) = @League)
      AND (@Source IS NULL OR Source = @Source)
      AND (@MarketType IS NULL OR MarketType = @MarketType)
      AND (@OnlyPending = 0 OR Status = N'Pending')
    ORDER BY MatchDate DESC, UpdatedAtUtc DESC, AutomatedCornerBetSelectionId DESC;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_SettleAutomatedCornerBetSelections
    @MatchDateTo DATE = NULL,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Compatibilidad únicamente. La liquidación productiva se ejecuta en
    -- IAutomatedBotPickSettlementUseCase para validar estado final, NULL y concurrencia.
    SET @RowsAffected = 0;
    RETURN;

    ;WITH MatchesToSettle AS
    (
        SELECT
            s.AutomatedCornerBetSelectionId,
            HomeCorners = CASE s.MarketType
                WHEN N'TotalGoals' THEN mh.HomeGoals
                WHEN N'HomeTeamGoals' THEN mh.HomeGoals
                WHEN N'AwayTeamGoals' THEN mh.HomeGoals
                WHEN N'TotalShots' THEN mh.HomeShots
                WHEN N'HomeTeamShots' THEN mh.HomeShots
                WHEN N'AwayTeamShots' THEN mh.HomeShots
                WHEN N'TotalShotsOnGoal' THEN mh.HomeShotsOnGoal
                WHEN N'HomeTeamShotsOnGoal' THEN mh.HomeShotsOnGoal
                WHEN N'AwayTeamShotsOnGoal' THEN mh.HomeShotsOnGoal
                ELSE mh.HomeCorners
            END,
            AwayCorners = CASE s.MarketType
                WHEN N'TotalGoals' THEN mh.AwayGoals
                WHEN N'HomeTeamGoals' THEN mh.AwayGoals
                WHEN N'AwayTeamGoals' THEN mh.AwayGoals
                WHEN N'TotalShots' THEN mh.AwayShots
                WHEN N'HomeTeamShots' THEN mh.AwayShots
                WHEN N'AwayTeamShots' THEN mh.AwayShots
                WHEN N'TotalShotsOnGoal' THEN mh.AwayShotsOnGoal
                WHEN N'HomeTeamShotsOnGoal' THEN mh.AwayShotsOnGoal
                WHEN N'AwayTeamShotsOnGoal' THEN mh.AwayShotsOnGoal
                ELSE mh.AwayCorners
            END,
            ActualTotalCorners = CASE s.MarketType
                WHEN N'TotalGoals' THEN mh.HomeGoals + mh.AwayGoals
                WHEN N'HomeTeamGoals' THEN mh.HomeGoals + mh.AwayGoals
                WHEN N'AwayTeamGoals' THEN mh.HomeGoals + mh.AwayGoals
                WHEN N'TotalShots' THEN mh.HomeShots + mh.AwayShots
                WHEN N'HomeTeamShots' THEN mh.HomeShots + mh.AwayShots
                WHEN N'AwayTeamShots' THEN mh.HomeShots + mh.AwayShots
                WHEN N'TotalShotsOnGoal' THEN mh.HomeShotsOnGoal + mh.AwayShotsOnGoal
                WHEN N'HomeTeamShotsOnGoal' THEN mh.HomeShotsOnGoal + mh.AwayShotsOnGoal
                WHEN N'AwayTeamShotsOnGoal' THEN mh.HomeShotsOnGoal + mh.AwayShotsOnGoal
                ELSE mh.HomeCorners + mh.AwayCorners
            END,
            ActualSelectedCorners =
                CASE s.MarketType
                    WHEN N'HomeTeamCorners' THEN mh.HomeCorners
                    WHEN N'AwayTeamCorners' THEN mh.AwayCorners
                    WHEN N'HomeTeamGoals' THEN mh.HomeGoals
                    WHEN N'AwayTeamGoals' THEN mh.AwayGoals
                    WHEN N'TotalGoals' THEN mh.HomeGoals + mh.AwayGoals
                    WHEN N'HomeTeamShots' THEN mh.HomeShots
                    WHEN N'AwayTeamShots' THEN mh.AwayShots
                    WHEN N'TotalShots' THEN mh.HomeShots + mh.AwayShots
                    WHEN N'HomeTeamShotsOnGoal' THEN mh.HomeShotsOnGoal
                    WHEN N'AwayTeamShotsOnGoal' THEN mh.AwayShotsOnGoal
                    WHEN N'TotalShotsOnGoal' THEN mh.HomeShotsOnGoal + mh.AwayShotsOnGoal
                    ELSE mh.HomeCorners + mh.AwayCorners
                END
        FROM dbo.AutomatedCornerBetSelections s
        INNER JOIN dbo.MatchHistory mh
            ON CAST(s.MatchDate AS DATE) = mh.MatchDate
           AND COALESCE(s.StandardizedLeague, s.League) = COALESCE(mh.StandardizedLeague, mh.League)
           AND COALESCE(s.StandardizedHomeTeam, s.HomeTeam) = COALESCE(mh.StandardizedHomeTeam, mh.HomeTeam)
           AND COALESCE(s.StandardizedAwayTeam, s.AwayTeam) = COALESCE(mh.StandardizedAwayTeam, mh.AwayTeam)
        WHERE s.Status = N'Pending'
          AND (@MatchDateTo IS NULL OR CAST(s.MatchDate AS DATE) <= @MatchDateTo)
          AND (s.MarketType <> N'TotalShots'
               OR (mh.HomeShots IS NOT NULL AND mh.AwayShots IS NOT NULL))
          AND (s.MarketType <> N'HomeTeamShots' OR mh.HomeShots IS NOT NULL)
          AND (s.MarketType <> N'AwayTeamShots' OR mh.AwayShots IS NOT NULL)
          AND (s.MarketType <> N'TotalShotsOnGoal'
               OR (mh.HomeShotsOnGoal IS NOT NULL AND mh.AwayShotsOnGoal IS NOT NULL))
          AND (s.MarketType <> N'HomeTeamShotsOnGoal' OR mh.HomeShotsOnGoal IS NOT NULL)
          AND (s.MarketType <> N'AwayTeamShotsOnGoal' OR mh.AwayShotsOnGoal IS NOT NULL)
    ),
    SettlementOutcomes AS
    (
        SELECT
            m.*,
            SettlementFactor =
                CAST(
                    CASE
                        -- Asian quarter line x.25 = half on x.0 and half on x.5.
                        WHEN s.LineValue - FLOOR(s.LineValue) = 0.25
                         AND m.ActualSelectedCorners = FLOOR(s.LineValue)
                         AND s.SelectedSide = N'Over' THEN -0.5
                        WHEN s.LineValue - FLOOR(s.LineValue) = 0.25
                         AND m.ActualSelectedCorners = FLOOR(s.LineValue)
                         AND s.SelectedSide = N'Under' THEN 0.5
                        -- Asian quarter line x.75 = half on x.5 and half on the next integer.
                        WHEN s.LineValue - FLOOR(s.LineValue) = 0.75
                         AND m.ActualSelectedCorners = CEILING(s.LineValue)
                         AND s.SelectedSide = N'Over' THEN 0.5
                        WHEN s.LineValue - FLOOR(s.LineValue) = 0.75
                         AND m.ActualSelectedCorners = CEILING(s.LineValue)
                         AND s.SelectedSide = N'Under' THEN -0.5
                        WHEN m.ActualSelectedCorners = s.LineValue THEN 0
                        WHEN s.SelectedSide = N'Over' AND m.ActualSelectedCorners > s.LineValue THEN 1
                        WHEN s.SelectedSide = N'Under' AND m.ActualSelectedCorners < s.LineValue THEN 1
                        ELSE -1
                    END
                    AS DECIMAL(4,2))
        FROM MatchesToSettle m
        INNER JOIN dbo.AutomatedCornerBetSelections s
            ON s.AutomatedCornerBetSelectionId = m.AutomatedCornerBetSelectionId
    )
    UPDATE s
    SET
        ActualHomeCorners = m.HomeCorners,
        ActualAwayCorners = m.AwayCorners,
        ActualTotalCorners = m.ActualTotalCorners,
        Status =
            CASE
                WHEN m.SettlementFactor > 0 THEN N'Won'
                WHEN m.SettlementFactor < 0 THEN N'Lost'
                ELSE N'Push'
            END,
        ProfitLoss =
            CASE
                WHEN m.SettlementFactor > 0 THEN ROUND(s.Stake * (s.Odds - 1) * m.SettlementFactor, 2)
                WHEN m.SettlementFactor < 0 THEN ROUND(s.Stake * m.SettlementFactor, 2)
                ELSE 0
            END,
        YieldPct =
            CASE
                WHEN s.Stake = 0 THEN NULL
                WHEN m.SettlementFactor > 0 THEN ROUND((s.Odds - 1) * m.SettlementFactor, 4)
                ELSE m.SettlementFactor
            END,
        SettledAtUtc = SYSUTCDATETIME(),
        UpdatedAtUtc = SYSUTCDATETIME()
    FROM dbo.AutomatedCornerBetSelections s
    INNER JOIN SettlementOutcomes m
        ON m.AutomatedCornerBetSelectionId = s.AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_ApplyAutomatedBotPickSettlements
    @RowsJson NVARCHAR(MAX),
    @AppliedRows INT OUTPUT,
    @SettledRows INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF ISJSON(@RowsJson) <> 1
        THROW 50020, 'RowsJson must be valid JSON.', 1;

    DECLARE @NowUtc DATETIME2(0) = SYSUTCDATETIME();
    DECLARE @Updates TABLE
    (
        SelectionId BIGINT NOT NULL PRIMARY KEY,
        ReconcileExistingSettlement BIT NOT NULL,
        ExpectedSettledAtUtc DATETIME2(0) NULL,
        MatchHistoryId BIGINT NULL,
        ApiFootballFixtureId BIGINT NULL,
        Status NVARCHAR(20) NOT NULL,
        ActualHomeValue INT NULL,
        ActualAwayValue INT NULL,
        ActualTotalValue INT NULL,
        ActualValue INT NULL,
        SettlementFactor DECIMAL(6,3) NULL,
        ProfitLoss DECIMAL(10,2) NULL,
        YieldPct DECIMAL(9,4) NULL,
        Reason NVARCHAR(500) NOT NULL,
        SettlementSource NVARCHAR(50) NOT NULL,
        FixtureStatus NVARCHAR(20) NULL,
        SnapshotJson NVARCHAR(MAX) NULL
    );

    INSERT @Updates
    (
        SelectionId, ReconcileExistingSettlement, ExpectedSettledAtUtc,
        MatchHistoryId, ApiFootballFixtureId, Status,
        ActualHomeValue, ActualAwayValue, ActualTotalValue, ActualValue,
        SettlementFactor, ProfitLoss, YieldPct, Reason, SettlementSource,
        FixtureStatus, SnapshotJson
    )
    SELECT
        SelectionId, ReconcileExistingSettlement, ExpectedSettledAtUtc,
        MatchHistoryId, ApiFootballFixtureId, Status,
        ActualHomeValue, ActualAwayValue, ActualTotalValue, ActualValue,
        SettlementFactor, ProfitLoss, YieldPct, Reason, SettlementSource,
        FixtureStatus, SnapshotJson
    FROM OPENJSON(@RowsJson)
    WITH
    (
        SelectionId BIGINT '$.SelectionId',
        ReconcileExistingSettlement BIT '$.ReconcileExistingSettlement',
        ExpectedSettledAtUtc DATETIME2(0) '$.ExpectedSettledAtUtc',
        MatchHistoryId BIGINT '$.MatchHistoryId',
        ApiFootballFixtureId BIGINT '$.ApiFootballFixtureId',
        Status NVARCHAR(20) '$.Status',
        ActualHomeValue INT '$.ActualHomeValue',
        ActualAwayValue INT '$.ActualAwayValue',
        ActualTotalValue INT '$.ActualTotalValue',
        ActualValue INT '$.ActualValue',
        SettlementFactor DECIMAL(6,3) '$.SettlementFactor',
        ProfitLoss DECIMAL(10,2) '$.ProfitLoss',
        YieldPct DECIMAL(9,4) '$.YieldPct',
        Reason NVARCHAR(500) '$.Reason',
        SettlementSource NVARCHAR(50) '$.SettlementSource',
        FixtureStatus NVARCHAR(20) '$.FixtureStatus',
        SnapshotJson NVARCHAR(MAX) '$.SnapshotJson'
    );

    IF EXISTS (SELECT 1 FROM @Updates WHERE Status NOT IN (N'Pending', N'Won', N'Lost', N'Push'))
        THROW 50021, 'Automated settlement status must be Pending, Won, Lost or Push.', 1;

    DECLARE @Applied TABLE (Status NVARCHAR(20) NOT NULL);

    UPDATE s
    SET
        -- A final canonical/UTC match must replace a preliminary historical link.
        -- Keeping the old id here would prevent future source-update reconciliation.
        MatchHistoryId = COALESCE(u.MatchHistoryId, s.MatchHistoryId),
        ApiFootballFixtureId = COALESCE(u.ApiFootballFixtureId, s.ApiFootballFixtureId),
        Status = u.Status,
        ActualHomeCorners = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.ActualHomeCorners ELSE u.ActualHomeValue END,
        ActualAwayCorners = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.ActualAwayCorners ELSE u.ActualAwayValue END,
        ActualTotalCorners = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.ActualTotalCorners ELSE u.ActualTotalValue END,
        SettlementActualValue = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.SettlementActualValue ELSE u.ActualValue END,
        SettlementFactor = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.SettlementFactor ELSE u.SettlementFactor END,
        SettlementReason = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.SettlementReason ELSE u.Reason END,
        SettlementSource = u.SettlementSource,
        SettlementMatchStatus = u.FixtureStatus,
        SettlementSnapshotJson = u.SnapshotJson,
        LastSettlementCheckReason = u.Reason,
        LastSettlementCheckAtUtc = @NowUtc,
        ProfitLoss = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.ProfitLoss ELSE u.ProfitLoss END,
        YieldPct = CASE WHEN u.Status = N'Pending' AND u.ReconcileExistingSettlement = 0 THEN s.YieldPct ELSE u.YieldPct END,
        SettledAtUtc = CASE
            WHEN u.Status <> N'Pending' THEN @NowUtc
            WHEN u.ReconcileExistingSettlement = 1 THEN NULL
            ELSE s.SettledAtUtc
        END,
        UpdatedAtUtc = CASE
            WHEN u.Status <> N'Pending' OR u.ReconcileExistingSettlement = 1 THEN @NowUtc
            ELSE s.UpdatedAtUtc
        END
    OUTPUT inserted.Status INTO @Applied(Status)
    FROM dbo.AutomatedCornerBetSelections s WITH (UPDLOCK, ROWLOCK)
    INNER JOIN @Updates u
        ON u.SelectionId = s.AutomatedCornerBetSelectionId
    WHERE
        s.Status = N'Pending'
        OR
        (
            u.ReconcileExistingSettlement = 1
            AND s.Status IN (N'Won', N'Lost', N'Push')
            AND s.SettlementSource IN (N'LocalMatchHistory', N'LocalMatchHistoryHistorical')
            AND s.SettledAtUtc = u.ExpectedSettledAtUtc
        );

    SELECT @AppliedRows = COUNT(*) FROM @Applied;
    SELECT @SettledRows = COUNT(*) FROM @Applied WHERE Status <> N'Pending';
END;

GO
