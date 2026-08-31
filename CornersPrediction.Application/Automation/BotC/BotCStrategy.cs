using System.Text.Json;
using CornersPrediction.Application.Automation.BotD;
using CornersPrediction.Application.Automation.BotE;
using CornersPrediction.Application.FootballIntelligence;

namespace CornersPrediction.Application.Automation.BotC;

public static class BotCDecisionCodes
{
    public const string ApprovedProbability = "APPROVED_CALIBRATED_PROBABILITY";
    public const string ApprovedMetaProbability = "APPROVED_META_PROBABILITY";
    public const string ApprovedEdge = "APPROVED_FINAL_EDGE";
    public const string ApprovedExpectedValue = "APPROVED_FINAL_EV";
    public const string ApprovedContext = "APPROVED_CONTEXT_AGREEMENT";
    public const string ApprovedExactLine = "APPROVED_EXACT_LINE_SUPPORT";
    public const string RejectedProbability = "REJECTED_CALIBRATED_PROBABILITY_LOW";
    public const string RejectedMetaProbability = "REJECTED_META_PROBABILITY_LOW";
    public const string RejectedEdge = "REJECTED_FINAL_EDGE_LOW";
    public const string RejectedExpectedValue = "REJECTED_FINAL_EV_LOW";
    public const string RejectedDataQuality = "REJECTED_DATA_QUALITY_LOW";
    public const string RejectedHistory = "REJECTED_INSUFFICIENT_HISTORY";
    public const string RejectedContext = "REJECTED_MODEL_CONTEXT_DISAGREEMENT";
    public const string RejectedOdds = "REJECTED_ODDS_OUT_OF_RANGE";
    public const string RejectedRuleScore = "REJECTED_RULE_BASED_SCORE_LOW";
    public const string PendingHistory = "PENDING_MISSING_TEAM_HISTORY";
    public const string PendingOdds = "PENDING_MISSING_ODDS";
    public const string InvalidInput = "REJECTED_INVALID_INPUT";
    public const string RejectedModelUnavailable = "REJECTED_MODEL_NOT_AVAILABLE";
    public const string RejectedModelSchemaMismatch = "REJECTED_MODEL_SCHEMA_MISMATCH";
    public const string RejectedMetaModelTemporalLeakage = "REJECTED_META_MODEL_TEMPORAL_LEAKAGE";
    public const string RejectedBaseModelTemporalLeakage = "REJECTED_BASE_MODEL_TEMPORAL_LEAKAGE";
    public const string ApprovedTeamStrength = "APPROVED_TEAM_STRENGTH_SUPPORT";
    public const string RejectedTeamStrength = "REJECTED_TEAM_STRENGTH_QUALITY_LOW";
    public const string ApprovedEmpiricalCalibration = "APPROVED_EMPIRICAL_MARKET_CALIBRATION";
    public const string ApprovedFootballIntelligence = "APPROVED_FOOTBALL_INTELLIGENCE_ADJUSTMENT";
    public const string NeutralFootballIntelligence = "NEUTRAL_FOOTBALL_INTELLIGENCE_NO_USABLE_EVIDENCE";
    public const string RejectedCalibrationUnavailable = "REJECTED_CALIBRATION_SAMPLE_LOW";
    public const string RejectedCalibrationReliability = "REJECTED_CALIBRATION_RELIABILITY_LOW";
}

public static class BotCRiskFlags
{
    public const string InsufficientOverallHistory = "InsufficientOverallHistory";
    public const string InsufficientVenueHistory = "InsufficientVenueHistory";
    public const string StaleHistoricalData = "StaleHistoricalData";
    public const string MissingOppositeOdds = "MissingOppositeOdds";
    public const string MissingCrossMarketPrediction = "MissingCrossMarketPrediction";
    public const string InvalidOdds = "InvalidOdds";
    public const string InvalidLine = "InvalidLine";
    public const string MissingBaseProbability = "MissingBaseProbability";
    public const string LeagueBaselineUnavailable = "LeagueBaselineUnavailable";
    public const string RuleBasedFallback = "RuleBasedFallbackActive";
    public const string MetaModelUnavailable = "MetaModelUnavailable";
    public const string MetaModelSchemaMismatch = "MetaModelSchemaMismatch";
    public const string MetaModelTemporalLeakage = "MetaModelTemporalLeakage";
    public const string BaseModelTemporalLeakage = "BaseModelTemporalLeakage";
    public const string TeamStrengthUnavailable = "TeamStrengthUnavailable";
    public const string EmpiricalCalibrationUnavailable = "EmpiricalCalibrationUnavailable";
    public const string FootballIntelligenceUnavailable = "FootballIntelligenceUnavailable";
    public const string FootballIntelligenceApplied = "FootballIntelligenceApplied";
}

public sealed record BotCStrategyConfiguration
{
    public string ConfigurationVersion { get; init; } = "bot-c-selector-1.0.0";
    public string FeatureSchemaVersion { get; init; } = "bot-c-features-1.0.0";
    public string BasePredictionSource { get; init; } = "MODELS_2026";
    public string? BaseModelVersionOverride { get; init; }
    public DateTime? BaseModelTrainedThroughUtc { get; init; }
    public bool SelectorEnabled { get; init; } = true;
    public bool AllowRuleBasedFallback { get; init; } = true;
    public double DecayFactor { get; init; } = 0.85d;
    public double ShrinkageStrength { get; init; } = 10d;
    public int RequiredVenueMatches { get; init; } = 8;
    public double MaximumVenueWeight { get; init; } = 0.65d;
    public double MinimumStandardDeviation { get; init; } = 0.50d;
    public int MinimumHistoricalMatches { get; init; } = 6;
    public double MinimumCalibratedProbability { get; init; } = 0.55d;
    public double MinimumFinalEdge { get; init; } = 0.035d;
    public double MinimumFinalExpectedValue { get; init; } = 0.03d;
    public double MinimumDataQualityScore { get; init; } = 0.60d;
    public double MinimumContextAgreementScore { get; init; } = 0.60d;
    public double MinimumRuleBasedConfidenceScore { get; init; } = 0.58d;
    public double MaximumBaseContextDistanceSigma { get; init; } = 1.75d;
    public double MinimumOdds { get; init; } = 1.60d;
    public double MaximumOdds { get; init; } = 2.30d;
    public double CalibrationIntercept { get; init; } = 0d;
    public double CalibrationSlope { get; init; } = 1d;
    public IReadOnlyDictionary<string, BotCCalibrationProfile> CalibrationProfiles { get; init; } =
        new Dictionary<string, BotCCalibrationProfile>(StringComparer.OrdinalIgnoreCase);
    public double WeightCalibratedProbability { get; init; } = 0.25d;
    public double WeightEdge { get; init; } = 0.20d;
    public double WeightExpectedValue { get; init; } = 0.15d;
    public double WeightExactLineHitRate { get; init; } = 0.15d;
    public double WeightContextLineDistance { get; init; } = 0.10d;
    public double WeightContextAgreement { get; init; } = 0.10d;
    public double WeightDataQuality { get; init; } = 0.05d;
    public double QualityOverallSampleWeight { get; init; } = 0.35d;
    public double QualityVenueSampleWeight { get; init; } = 0.20d;
    public double QualityFreshnessWeight { get; init; } = 0.15d;
    public double QualityFeatureCompletenessWeight { get; init; } = 0.15d;
    public double QualityMarketDataWeight { get; init; } = 0.10d;
    public double QualityConsistencyWeight { get; init; } = 0.05d;
    public IReadOnlyDictionary<string, BotCMarketThresholdConfiguration> MarketThresholds { get; init; } =
        new Dictionary<string, BotCMarketThresholdConfiguration>(StringComparer.OrdinalIgnoreCase);
    public BotDTeamStrengthConfiguration TeamStrength { get; init; } = new();
    public BotEEmpiricalCalibrationConfiguration EmpiricalCalibration { get; init; } = new();
    public FootballIntelligenceAdjustmentConfiguration FootballIntelligence { get; init; } = new();

    public BotCCalibrationProfile ResolveCalibration(string marketType, string selection)
    {
        var candidates = new[] { $"{marketType}:{selection}", marketType, $"*:{selection}", "*" };
        return candidates
            .Select(key => CalibrationProfiles.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value)
            .FirstOrDefault(value => value is not null)
            ?? new BotCCalibrationProfile
            {
                ModelName = "Identity",
                ModelVersion = ConfigurationVersion,
                Intercept = CalibrationIntercept,
                Slope = CalibrationSlope,
                TrainingSampleCount = 0
            };
    }

    public BotCResolvedThresholds ResolveThresholds(string marketType, string selection)
    {
        var candidates = new[] { $"{marketType}:{selection}", marketType, $"*:{selection}", "*" };
        var thresholds = candidates
            .Select(key => MarketThresholds.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value)
            .Where(value => value is not null)
            .ToArray();
        T? First<T>(Func<BotCMarketThresholdConfiguration, T?> selector) where T : struct =>
            thresholds.Select(selector).FirstOrDefault(value => value.HasValue);
        return new BotCResolvedThresholds(
            First(value => value.Enabled) ?? SelectorEnabled,
            First(value => value.MinimumFinalProbability) ?? MinimumCalibratedProbability,
            First(value => value.MinimumFinalEdge) ?? MinimumFinalEdge,
            First(value => value.MinimumFinalExpectedValue) ?? MinimumFinalExpectedValue,
            First(value => value.MinimumDataQualityScore) ?? MinimumDataQualityScore,
            First(value => value.MinimumContextAgreementScore) ?? MinimumContextAgreementScore,
            First(value => value.MinimumHistoricalMatches) ?? MinimumHistoricalMatches,
            First(value => value.MinimumOdds) ?? MinimumOdds,
            First(value => value.MaximumOdds) ?? MaximumOdds);
    }

    public static BotCStrategyConfiguration FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BotCStrategyConfiguration();
        }

        try
        {
            var configuration = JsonSerializer.Deserialize<BotCStrategyConfiguration>(json, JsonOptions);
            return configuration is null ? new BotCStrategyConfiguration() : Validate(configuration);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Bot C strategy configuration is not valid JSON.", exception);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(Validate(this), JsonOptions);

    public static BotCStrategyConfiguration Validate(BotCStrategyConfiguration value)
    {
        if (value.BasePredictionSource is not ("MODELS_2026" or "LEGACY"))
        {
            throw new ArgumentException("BasePredictionSource must be MODELS_2026 or LEGACY.");
        }
        if (value.BasePredictionSource == "LEGACY"
            && (string.IsNullOrWhiteSpace(value.BaseModelVersionOverride)
                || value.BaseModelTrainedThroughUtc is null))
        {
            throw new ArgumentException("Legacy selector strategies require BaseModelVersionOverride and BaseModelTrainedThroughUtc.");
        }
        RequireRange(value.DecayFactor, 0.50d, 0.999d, nameof(DecayFactor));
        RequireRange(value.ShrinkageStrength, 0d, 100d, nameof(ShrinkageStrength));
        RequireRange(value.MaximumVenueWeight, 0d, 1d, nameof(MaximumVenueWeight));
        RequireRange(value.MinimumCalibratedProbability, 0d, 1d, nameof(MinimumCalibratedProbability));
        RequireRange(value.MinimumFinalEdge, -1d, 1d, nameof(MinimumFinalEdge));
        RequireRange(value.MinimumFinalExpectedValue, -1d, 10d, nameof(MinimumFinalExpectedValue));
        RequireRange(value.MinimumDataQualityScore, 0d, 1d, nameof(MinimumDataQualityScore));
        RequireRange(value.MinimumContextAgreementScore, 0d, 1d, nameof(MinimumContextAgreementScore));
        RequireRange(value.MinimumRuleBasedConfidenceScore, 0d, 1d, nameof(MinimumRuleBasedConfidenceScore));
        if (value.RequiredVenueMatches is < 1 or > 100 || value.MinimumHistoricalMatches is < 1 or > 100)
        {
            throw new ArgumentException("Bot C history thresholds must be between 1 and 100 matches.");
        }
        if (value.MinimumStandardDeviation <= 0 || value.MinimumOdds <= 1 || value.MaximumOdds <= value.MinimumOdds)
        {
            throw new ArgumentException("Bot C standard deviation and odds bounds are invalid.");
        }

        foreach (var (key, profile) in value.CalibrationProfiles)
        {
            if (string.IsNullOrWhiteSpace(key) || profile is null
                || string.IsNullOrWhiteSpace(profile.ModelName)
                || string.IsNullOrWhiteSpace(profile.ModelVersion)
                || !double.IsFinite(profile.Intercept)
                || !double.IsFinite(profile.Slope)
                || profile.Slope <= 0
                || profile.TrainingSampleCount < 0)
            {
                throw new ArgumentException($"Bot C calibration profile '{key}' is invalid.");
            }
        }

        foreach (var (key, threshold) in value.MarketThresholds)
        {
            if (string.IsNullOrWhiteSpace(key) || threshold is null)
            {
                throw new ArgumentException("Bot C market-threshold keys and values are required.");
            }
            RequireNullableRange(threshold.MinimumFinalProbability, 0d, 1d, $"{key}.MinimumFinalProbability");
            RequireNullableRange(threshold.MinimumFinalEdge, -1d, 1d, $"{key}.MinimumFinalEdge");
            RequireNullableRange(threshold.MinimumFinalExpectedValue, -1d, 10d, $"{key}.MinimumFinalExpectedValue");
            RequireNullableRange(threshold.MinimumDataQualityScore, 0d, 1d, $"{key}.MinimumDataQualityScore");
            RequireNullableRange(threshold.MinimumContextAgreementScore, 0d, 1d, $"{key}.MinimumContextAgreementScore");
            if (threshold.MinimumHistoricalMatches is < 1 or > 100)
                throw new ArgumentException($"{key}.MinimumHistoricalMatches must be between 1 and 100.");
            if (threshold.MinimumOdds is <= 1 || threshold.MaximumOdds is <= 1
                || threshold.MinimumOdds.HasValue && threshold.MaximumOdds.HasValue
                && threshold.MaximumOdds <= threshold.MinimumOdds)
                throw new ArgumentException($"{key} odds bounds are invalid.");
        }

        var decisionWeight = value.WeightCalibratedProbability + value.WeightEdge + value.WeightExpectedValue
            + value.WeightExactLineHitRate + value.WeightContextLineDistance + value.WeightContextAgreement
            + value.WeightDataQuality;
        if (Math.Abs(decisionWeight - 1d) > 0.0001d)
        {
            throw new ArgumentException("Bot C decision weights must add up to 1.0.");
        }

        var qualityWeight = value.QualityOverallSampleWeight + value.QualityVenueSampleWeight
            + value.QualityFreshnessWeight + value.QualityFeatureCompletenessWeight
            + value.QualityMarketDataWeight + value.QualityConsistencyWeight;
        if (Math.Abs(qualityWeight - 1d) > 0.0001d)
        {
            throw new ArgumentException("Bot C data-quality weights must add up to 1.0.");
        }

        BotDTeamStrengthCalculator.Validate(value.TeamStrength);
        BotEEmpiricalCalibrationCalculator.Validate(value.EmpiricalCalibration);
        FootballIntelligenceAdjustmentCalculator.Validate(value.FootballIntelligence);
        if (value.TeamStrength.Enabled && value.EmpiricalCalibration.Enabled)
        {
            throw new ArgumentException(
                "Bot D team-strength and Bot E empirical calibration cannot be enabled together in version 1.");
        }

        return value;
    }

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
        }
    }

    private static void RequireNullableRange(double? value, double minimum, double maximum, string name)
    {
        if (value.HasValue)
        {
            RequireRange(value.Value, minimum, maximum, name);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}

public sealed record BotCMarketThresholdConfiguration
{
    public bool? Enabled { get; init; }
    public double? MinimumFinalProbability { get; init; }
    public double? MinimumFinalEdge { get; init; }
    public double? MinimumFinalExpectedValue { get; init; }
    public double? MinimumDataQualityScore { get; init; }
    public double? MinimumContextAgreementScore { get; init; }
    public int? MinimumHistoricalMatches { get; init; }
    public double? MinimumOdds { get; init; }
    public double? MaximumOdds { get; init; }
}

public sealed record BotCCalibrationProfile
{
    public string ModelName { get; init; } = "Platt";
    public string ModelVersion { get; init; } = string.Empty;
    public double Intercept { get; init; }
    public double Slope { get; init; } = 1d;
    public int TrainingSampleCount { get; init; }
    public DateTime? TrainedThroughUtc { get; init; }
}

public sealed record BotCResolvedThresholds(
    bool Enabled,
    double MinimumFinalProbability,
    double MinimumFinalEdge,
    double MinimumFinalExpectedValue,
    double MinimumDataQualityScore,
    double MinimumContextAgreementScore,
    int MinimumHistoricalMatches,
    double MinimumOdds,
    double MaximumOdds);

public sealed record BotCStrategyManifest(
    string StrategyName,
    string DecisionEngineType,
    string ProbabilityPolicy,
    BotCStrategyConfiguration Configuration,
    IReadOnlyList<string> SupportedMarkets,
    IReadOnlyList<string> Pipeline,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FeatureGroups,
    IReadOnlyList<string> ApprovalRules,
    IReadOnlyList<string> DataLeakageGuards,
    IReadOnlyList<string> PersistenceAndSettlement);

public static class BotCStrategyCatalog
{
    public static BotCStrategyManifest Build(string? configurationJson)
    {
        var config = BotCStrategyConfiguration.FromJson(configurationJson);
        var strengthEnabled = config.TeamStrength.Enabled;
        var empiricalCalibrationEnabled = config.EmpiricalCalibration.Enabled;
        var footballIntelligenceEnabled = config.FootballIntelligence.Enabled;
        var pipeline = new List<string>
        {
            "Valida línea, cuota, mercado y timestamp.",
            "Filtra el historial con MatchDateUtc < AsOfDateUtc.",
            "Construye ventanas 5, 10 y 20, overall y local/visita.",
            "Calcula estadística descriptiva, media ponderada, shrinkage, hit rate y tendencia.",
            "Construye una predicción contextual independiente del ML."
        };
        if (strengthEnabled)
        {
            pipeline.Add("Calcula brecha temporal de nivel con Elo, enfrentamientos directos y rivales comunes; reduce la señal según cobertura.");
            pipeline.Add("Ajusta contexto y probabilidad por brecha de nivel, con mayor peso en mercados del equipo local.");
        }
        if (empiricalCalibrationEnabled)
        {
            pipeline.Add($"Carga resultados etiquetados de todas las evaluaciones de {config.EmpiricalCalibration.SourceBotKey} y vuelve a filtrar por disponibilidad estrictamente anterior al candidato.");
            pipeline.Add("Selecciona evidencia jerárquica por mercado+lado, familia+lado o lado global; deduplica y reduce el peso de múltiples líneas del mismo fixture.");
            pipeline.Add("Estima una tasa empírica local por similitud de probabilidad, recencia y calidad; aplica prior hacia no-vig y un límite conservador por incertidumbre.");
        }
        if (footballIntelligenceEnabled)
        {
            pipeline.Add("Carga el último snapshot pre-partido cuya evidencia fue conocida antes del cutoff del candidato.");
            pipeline.Add("Ajusta de forma acotada la probabilidad según bajas, dudas y alineaciones; si no existe evidencia utilizable, el ajuste es exactamente cero.");
        }
        pipeline.AddRange(
        [
            "Calcula calidad, acuerdo, no-vig, edge y EV.",
            "Ejecuta LogisticRegression versionada o activa el fallback explicable permitido.",
            "Recalcula edge y EV con la probabilidad final y aplica todos los thresholds.",
            "Registra cada evaluación; publica solo la mejor aprobada por partido."
        ]);

        var featureGroups = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Historial"] = ["muestra", "promedio", "media ponderada", "mediana", "desviación", "varianza", "mínimo/máximo", "P25/P75", "IQR", "MAD"],
            ["Ventanas"] = ["últimos 5", "últimos 10", "últimos 20", "overall", "local como local", "visita como visita"],
            ["Contexto"] = ["a favor", "en contra", "total", "venue weight", "shrinkage", "predicción contextual local/visita/total"],
            ["Línea"] = ["margen ML", "margen contexto", "distancia sigma", "hit rate exacto", "líneas -1/-0.5/+0.5/+1"],
            ["Consistencia"] = ["respaldo ML", "contexto", "mediana", "tendencia", "hit rate", "acuerdo total"],
            ["Mercado"] = ["probabilidad implícita", "probabilidad no-vig", "overround", "edge final", "EV final"],
            ["Calidad"] = ["muestra overall", "muestra venue", "frescura", "completitud", "cuotas de ambos lados", "consistencia"]
        };
        if (strengthEnabled)
        {
            featureGroups["Brecha de nivel · Bot D"] =
            [
                "Elo temporal con ventaja local configurable",
                "resultado y margen ponderados por recencia",
                "enfrentamientos directos",
                "rendimiento ante rivales comunes",
                "propagación transitiva mediante rivales conectados",
                "confianza por muestra y conexiones",
                "gap bruto y gap ajustado [-1, 1]",
                "ajuste de probabilidad y contexto por alcance de mercado"
            ];
        }
        if (empiricalCalibrationEnabled)
        {
            featureGroups["Calibración empírica walk-forward"] =
            [
                $"evaluaciones Approved y Rejected de {config.EmpiricalCalibration.SourceBotKey}",
                "resultado asiático completo/medio/push",
                "jerarquía mercado+lado / familia+lado / lado global",
                "similitud con la probabilidad candidata",
                "ponderación por recencia, calidad y fixture",
                "muestra efectiva de Kish",
                "prior bayesiano hacia probabilidad no-vig",
                "probabilidad posterior y error estándar",
                "límite inferior conservador",
                "reliability y Brier base/mercado"
            ];
        }
        if (footballIntelligenceEnabled)
        {
            featureGroups["Inteligencia pre-partido"] =
            [
                "bajas y suspensiones estructuradas de API-Football",
                "noticias públicas con fecha y primera observación auditables",
                "alineación oficial cuando está disponible",
                "impacto ofensivo, defensivo, amplitud y balón parado",
                "confianza, número de fuentes y conflictos",
                "ajuste de probabilidad limitado por mercado",
                "neutralidad exacta cuando no existe evidencia suficiente"
            ];
        }

        var approvalRules = new List<string>
        {
            $"Probabilidad calibrada >= {config.MinimumCalibratedProbability:0.###}",
            $"Edge final >= {config.MinimumFinalEdge:0.###}",
            $"EV final >= {config.MinimumFinalExpectedValue:0.###}",
            $"Calidad >= {config.MinimumDataQualityScore:0.###}",
            $"Acuerdo contextual >= {config.MinimumContextAgreementScore:0.###}",
            $"Score fallback >= {config.MinimumRuleBasedConfidenceScore:0.###}",
            $"Historial por equipo >= {config.MinimumHistoricalMatches}",
            $"Cuota entre {config.MinimumOdds:0.00} y {config.MaximumOdds:0.00}",
            "MarketThresholds permite sobrescribir los mínimos por MarketType, MarketType:Selection, *:Selection o * (en ese orden)."
        };
        if (strengthEnabled)
        {
            approvalRules.Add($"Confianza de brecha de nivel >= {config.TeamStrength.MinimumConfidenceScore:0.###}.");
        }
        if (empiricalCalibrationEnabled)
        {
            approvalRules.Add($"Calibrador empírico disponible con reliability >= {config.EmpiricalCalibration.MinimumReliability:0.###}.");
            approvalRules.Add($"Muestra efectiva >= {config.EmpiricalCalibration.MinimumEffectiveObservations} fixtures independientes.");
            approvalRules.Add($"Solo resultados disponibles al menos {config.EmpiricalCalibration.OutcomeAvailabilityLagHours} h antes del candidato.");
        }
        if (footballIntelligenceEnabled)
        {
            approvalRules.Add($"La inteligencia solo influye con confianza >= {config.FootballIntelligence.MinimumTeamConfidence:0.###}, al menos {config.FootballIntelligence.MinimumActionableFacts} hecho accionable y {config.FootballIntelligence.MinimumIndependentSources} fuente independiente.");
            approvalRules.Add("Ausencia, baja calidad, antigüedad o evidencia posterior al cutoff producen ajuste 0; nunca rechazan ni aprueban por sí solas.");
        }

        return new BotCStrategyManifest(
            empiricalCalibrationEnabled
                ? "Pick Selector 2026 · Empirical Market Calibration"
                : strengthEnabled ? "Pick Selector 2026 · Team Strength Gap" : "Pick Selector 2026",
            empiricalCalibrationEnabled
                ? "EmpiricalMarketCalibrator jerárquico con shrinkage no-vig y límite conservador"
                : "MetaModel LogisticRegression cuando hay artefacto compatible; RuleBasedFallback en su ausencia",
            empiricalCalibrationEnabled
                ? "FinalProbability es el límite conservador de una probabilidad empírica posterior, calibrada con resultados estrictamente anteriores y contraída hacia no-vig según evidencia."
                : strengthEnabled
                ? "En fallback, FinalProbability parte de la probabilidad base calibrada y aplica un ajuste acotado por brecha de nivel. El gap y RuleBasedConfidenceScore nunca se presentan como probabilidad."
                : "FinalProbability usa MetaProbability cuando existe un artefacto compatible; en fallback usa la probabilidad base calibrada. RuleBasedConfidenceScore nunca se presenta como probabilidad.",
            config,
            ["TotalCorners", "HomeTeamCorners", "AwayTeamCorners", "TotalGoals", "HomeTeamGoals", "AwayTeamGoals", "TotalShots", "HomeTeamShots", "AwayTeamShots", "TotalShotsOnGoal", "HomeTeamShotsOnGoal", "AwayTeamShotsOnGoal"],
            pipeline,
            featureGroups,
            approvalRules,
            [
                "El motor vuelve a filtrar toda observación por AsOfDateUtc.",
                "El repositorio consulta MatchHistory con fecha estrictamente anterior.",
                strengthEnabled ? "La red de resultados de Bot D también descarta todo partido con fecha igual o posterior a AsOfDateUtc." : "Las features históricas solo usan observaciones estrictamente anteriores.",
                empiricalCalibrationEnabled ? $"El calibrador solo acepta outcomes cuyo kickoff + {config.EmpiricalCalibration.OutcomeAvailabilityLagHours} h sea estrictamente anterior a AsOfDateUtc." : "La calibración estática conserva su TrainedThrough cuando existe.",
                footballIntelligenceEnabled ? "Cada documento y hecho debe tener PublishedAtUtc/FirstSeenAtUtc menor o igual al cutoff; snapshots posteriores se ignoran." : "La inteligencia de noticias está deshabilitada para esta estrategia.",
                "El snapshot guarda AsOfDateUtc, schema y configuración.",
                "No usa resultado ni estadísticas del partido candidato.",
                "Rechaza candidatos cuya fecha no es posterior al TrainedThrough del modelo base.",
                "Rechaza un artefacto cuyo FeatureSchemaVersion no coincide con el runtime."
            ],
            [
                "Idempotencia por bot + cuota origen + mercado + línea + versión.",
                "Candidatos aprobados, rechazados y pendientes conservan snapshot y razones.",
                "Solo Approved se publica en Bot Picks.",
                "La liquidación existente soporta Win/HalfWin/Push/HalfLoss/Loss y consulta MatchHistory."
            ]);
    }
}
