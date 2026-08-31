using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPrediction.Domain.Automation.BotG;

public sealed record BotGConfiguration
{
    public const string DefaultBotKey = "G2026";
    public const string DefaultBaseStrategy = "GOALS_MARKET_ANCHORED";
    public const string LegacyConfigurationVersion = "bot-g-goals-market-1.0.0";
    public const string DefaultConfigurationVersion = "bot-g-goals-market-intelligence-1.1.0";
    public const string DefaultFeatureSchemaVersion = "bot-g-goals-features-1.0.0";
    public const string DefaultTrainingContractVersion = "bot-g-training-export-1.1.0";
    public const string DefaultMetaModelVersion = "bot-g-market-meta-1.1.0";

    public string BotKey { get; init; } = DefaultBotKey;
    public string Name { get; init; } = "Bot G Goals Specialist";
    public string BaseStrategy { get; init; } = DefaultBaseStrategy;
    public string ConfigurationVersion { get; init; } = DefaultConfigurationVersion;
    public string FeatureSchemaVersion { get; init; } = DefaultFeatureSchemaVersion;
    public string LegacyModelVersion { get; init; } = "goals_v1";
    public string Model2026Version { get; init; } = "per-market-model-lineage";
    public bool Enabled { get; init; } = true;
    public bool PublishEnabled { get; init; }
    public bool ShadowMode { get; init; } = true;
    public decimal Stake { get; init; } = 1m;
    public IReadOnlyList<BotGMarketType> SupportedMarkets { get; init; } =
        [BotGMarketType.TotalGoals, BotGMarketType.HomeTeamGoals, BotGMarketType.AwayTeamGoals];
    public BotGModelLineageConfiguration ModelLineages { get; init; } = new();
    public BotGFeatureConfiguration Features { get; init; } = new();
    public BotGMetaModelConfiguration MetaModel { get; init; } = new();
    public BotGCalibrationConfiguration Calibration { get; init; } = new();
    public BotGUncertaintyConfiguration Uncertainty { get; init; } = new();
    public BotGOodConfiguration OutOfDistribution { get; init; } = new();
    public BotGThresholdConfiguration Thresholds { get; init; } = new();
    public BotGRankingConfiguration Ranking { get; init; } = new();
    public BotGFootballIntelligenceConfiguration FootballIntelligence { get; init; } = new();

    public static BotGConfiguration FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Validate(new BotGConfiguration());
        }

        try
        {
            return Validate(JsonSerializer.Deserialize<BotGConfiguration>(json, JsonOptions)
                ?? throw new ArgumentException("Bot G configuration JSON is empty."));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Bot G configuration is not valid JSON.", exception);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(Validate(this), JsonOptions);

    public static BotGConfiguration Validate(BotGConfiguration value)
    {
        RequireText(value.BotKey, nameof(BotKey));
        RequireText(value.Name, nameof(Name));
        RequireText(value.BaseStrategy, nameof(BaseStrategy));
        RequireText(value.ConfigurationVersion, nameof(ConfigurationVersion));
        RequireText(value.FeatureSchemaVersion, nameof(FeatureSchemaVersion));
        RequireText(value.LegacyModelVersion, nameof(LegacyModelVersion));
        RequireText(value.Model2026Version, nameof(Model2026Version));
        if (!value.BotKey.Equals(DefaultBotKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"BotKey must be {DefaultBotKey}.");
        if (!value.BaseStrategy.Equals(DefaultBaseStrategy, StringComparison.Ordinal))
            throw new ArgumentException($"BaseStrategy must be {DefaultBaseStrategy}.");
        if (value.PublishEnabled == value.ShadowMode)
            throw new ArgumentException("Exactly one of PublishEnabled and ShadowMode must be true.");
        if (value.Stake is <= 0m or > 10m)
            throw new ArgumentException("Stake must be greater than zero and at most 10 units.");
        if (value.SupportedMarkets is null
            || value.SupportedMarkets.Count == 0
            || value.SupportedMarkets.Distinct().Count() != value.SupportedMarkets.Count
            || value.SupportedMarkets.Any(market => !Enum.IsDefined(market)))
            throw new ArgumentException("SupportedMarkets must contain unique supported Goals markets.");

        if (value.ModelLineages is null || value.Features is null || value.MetaModel is null || value.Calibration is null
            || value.Uncertainty is null || value.OutOfDistribution is null
            || value.Thresholds is null || value.Ranking is null || value.FootballIntelligence is null)
            throw new ArgumentException("Bot G configuration sections cannot be null.");

        ValidateModelLineages(value.ModelLineages);
        ValidateFeatures(value.Features);
        ValidateMetaModel(value.MetaModel);
        ValidateCalibration(value.Calibration);
        ValidateUncertainty(value.Uncertainty);
        ValidateOod(value.OutOfDistribution);
        ValidateThresholds(value.Thresholds);
        ValidateRanking(value.Ranking);
        ValidateFootballIntelligence(value.FootballIntelligence);
        return value;
    }

    private static void ValidateFeatures(BotGFeatureConfiguration value)
    {
        RequireRange(value.DecayFactor, 0.50d, 0.999d, nameof(value.DecayFactor));
        RequireRange(value.MinimumStandardDeviation, 0.01d, 20d, nameof(value.MinimumStandardDeviation));
        RequireRange(value.LineHistoryPriorStrength, 1d, 10_000d, nameof(value.LineHistoryPriorStrength));
        RequireRange(value.LineHitRatePriorMean, 0d, 1d, nameof(value.LineHitRatePriorMean));
        RequireRange(value.PushRatePriorMean, 0d, 1d, nameof(value.PushRatePriorMean));
        if (value.RequiredVenueMatches is < 1 or > 100 || value.MinimumHistoricalMatches is < 1 or > 100)
            throw new ArgumentException("Bot G history thresholds must be between 1 and 100.");
        if (value.Windows is null || value.Windows.Count != 3 || !value.Windows.SequenceEqual([5, 10, 20]))
            throw new ArgumentException("Bot G v1 requires the temporal windows 5, 10 and 20.");
    }

    private static void ValidateModelLineages(BotGModelLineageConfiguration value)
    {
        ValidateMarketLineage(value.TotalGoals, nameof(value.TotalGoals));
        ValidateMarketLineage(value.HomeTeamGoals, nameof(value.HomeTeamGoals));
        ValidateMarketLineage(value.AwayTeamGoals, nameof(value.AwayTeamGoals));
    }

    private static void ValidateMarketLineage(BotGBaseModelLineageConfiguration value, string name)
    {
        if (value is null)
            throw new ArgumentException($"Bot G model lineage '{name}' is required.");
        ValidateVersionSet(value.LegacyModelVersions, $"{name}.LegacyModelVersions");
        ValidateVersionSet(value.Model2026Versions, $"{name}.Model2026Versions");
    }

    private static void ValidateVersionSet(IReadOnlyList<string> values, string name)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace)
            || values.Select(item => item.Trim()).Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new ArgumentException($"{name} must contain unique, non-empty model versions.");
        }
    }

    private static void ValidateMetaModel(BotGMetaModelConfiguration value)
    {
        if (!value.Required)
            throw new ArgumentException("Bot G v1 requires the market-anchored residual meta-model and has no rule fallback.");
        RequireText(value.ModelVersion, nameof(value.ModelVersion));
        RequireText(value.FeatureSchemaVersion, nameof(value.FeatureSchemaVersion));
        RequireRange(value.MaximumAbsoluteResidualLogit, 0.1d, 20d, nameof(value.MaximumAbsoluteResidualLogit));
    }

    private static void ValidateCalibration(BotGCalibrationConfiguration value)
    {
        RequireText(value.Version, nameof(value.Version));
        RequireText(value.Method, nameof(value.Method));
        RequireRange(value.GlobalPriorStrength, 1d, 10_000d, nameof(value.GlobalPriorStrength));
        RequireRange(value.MarketPriorStrength, 1d, 10_000d, nameof(value.MarketPriorStrength));
        RequireRange(value.SelectionPriorStrength, 1d, 10_000d, nameof(value.SelectionPriorStrength));
        RequireRange(value.BookmakerPriorStrength, 1d, 10_000d, nameof(value.BookmakerPriorStrength));
        if (value.MinimumEffectiveSampleSize is < 1 or > 100_000 || value.OutcomeAvailabilityLagHours is < 0 or > 168)
            throw new ArgumentException("Bot G calibration sample and lag configuration is invalid.");
    }

    private static void ValidateUncertainty(BotGUncertaintyConfiguration value)
    {
        RequireText(value.Version, nameof(value.Version));
        RequireRange(value.ConfidenceZScore, 0d, 5d, nameof(value.ConfidenceZScore));
        RequireRange(value.ConservativeLambda, 0d, 5d, nameof(value.ConservativeLambda));
        RequireRange(value.MinimumUncertainty, 0d, 0.50d, nameof(value.MinimumUncertainty));
        RequireRange(value.MaximumUncertainty, value.MinimumUncertainty, 0.50d, nameof(value.MaximumUncertainty));
    }

    private static void ValidateOod(BotGOodConfiguration value)
    {
        RequireText(value.Version, nameof(value.Version));
        RequireRange(value.RobustZScoreThreshold, 0.5d, 20d, nameof(value.RobustZScoreThreshold));
        RequireRange(value.SevereRobustZScore, value.RobustZScoreThreshold, 100d, nameof(value.SevereRobustZScore));
        if (value.MinimumReferenceSampleSize is < 1 or > 1_000_000)
            throw new ArgumentException("OOD minimum reference sample size is invalid.");
    }

    private static void ValidateThresholds(BotGThresholdConfiguration value)
    {
        if (value.MinimumOdds <= 1d || value.MaximumOdds <= value.MinimumOdds)
            throw new ArgumentException("Bot G odds thresholds are invalid.");
        RequireRange(value.MinimumFinalProbability, 0d, 1d, nameof(value.MinimumFinalProbability));
        RequireRange(value.MinimumConservativeEdge, -1d, 1d, nameof(value.MinimumConservativeEdge));
        RequireRange(value.MinimumConservativeExpectedValue, -1d, 10d, nameof(value.MinimumConservativeExpectedValue));
        RequireRange(value.MinimumDataQuality, 0d, 1d, nameof(value.MinimumDataQuality));
        RequireRange(value.MinimumCalibrationReliability, 0d, 1d, nameof(value.MinimumCalibrationReliability));
        RequireRange(value.MaximumProbabilityUncertainty, 0d, 0.50d, nameof(value.MaximumProbabilityUncertainty));
        RequireRange(value.MaximumOodScore, 0d, 1d, nameof(value.MaximumOodScore));
        RequireRange(value.MaximumModelDisagreement, 0d, 20d, nameof(value.MaximumModelDisagreement));
        if (value.MinimumHistoricalMatches is < 1 or > 100
            || value.MinimumSettlementEffectiveSampleSize is < 1 or > 1_000_000
            || value.MaximumOddsAgeMinutes is < 1 or > 10_080)
            throw new ArgumentException("Bot G history or odds-age thresholds are invalid.");
    }

    private static void ValidateRanking(BotGRankingConfiguration value)
    {
        var weights = new[]
        {
            value.ConservativeExpectedValueWeight,
            value.ConservativeEdgeWeight,
            value.CalibrationReliabilityWeight,
            value.DataQualityWeight,
            value.InverseUncertaintyWeight,
            value.ContextAgreementWeight
        };
        if (weights.Any(weight => !double.IsFinite(weight) || weight < 0d)
            || Math.Abs(weights.Sum() - 1d) > 0.000001d)
            throw new ArgumentException("Bot G ranking weights must be non-negative and add up to 1.0.");
    }

    private static void ValidateFootballIntelligence(BotGFootballIntelligenceConfiguration value)
    {
        RequireText(value.Version, nameof(value.Version));
        RequireRange(value.Weight, 0d, 1d, nameof(value.Weight));
        RequireRange(value.MaximumProbabilityAdjustment, 0d, 0.25d, nameof(value.MaximumProbabilityAdjustment));
        RequireRange(value.MinimumTeamConfidence, 0d, 1d, nameof(value.MinimumTeamConfidence));
        RequireRange(value.AttackWeight, 0d, 1d, nameof(value.AttackWeight));
        RequireRange(value.DefenceWeight, 0d, 1d, nameof(value.DefenceWeight));
        RequireRange(value.WidthWeight, 0d, 1d, nameof(value.WidthWeight));
        RequireRange(value.SetPieceWeight, 0d, 1d, nameof(value.SetPieceWeight));
        if (value.MaximumSnapshotAgeMinutes < 1
            || value.MinimumActionableFacts < 1
            || value.MinimumIndependentSources < 1)
        {
            throw new ArgumentException("Bot G football-intelligence evidence thresholds must be positive.");
        }

        var weights = value.AttackWeight + value.DefenceWeight + value.WidthWeight + value.SetPieceWeight;
        if (Math.Abs(weights - 1d) > 0.0001d)
            throw new ArgumentException("Bot G football-intelligence market weights must add up to 1.0.");
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.");
    }

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed record BotGFootballIntelligenceConfiguration
{
    public bool Enabled { get; init; } = true;
    public string Version { get; init; } = "football-intelligence-adjustment-1.0.0";
    public double Weight { get; init; } = 0.35d;
    public double MaximumProbabilityAdjustment { get; init; } = 0.04d;
    public double MinimumTeamConfidence { get; init; } = 0.60d;
    public int MaximumSnapshotAgeMinutes { get; init; } = 4_320;
    public int MinimumActionableFacts { get; init; } = 1;
    public int MinimumIndependentSources { get; init; } = 1;
    public double AttackWeight { get; init; } = 0.35d;
    public double DefenceWeight { get; init; } = 0.25d;
    public double WidthWeight { get; init; } = 0.20d;
    public double SetPieceWeight { get; init; } = 0.20d;
}

public sealed record BotGBaseModelLineageConfiguration
{
    public IReadOnlyList<string> LegacyModelVersions { get; init; } = ["goals_v1"];
    public IReadOnlyList<string> Model2026Versions { get; init; } = [];

    public bool AllowsLegacy(string lineage) => Allows(lineage, LegacyModelVersions);

    public bool AllowsModel2026(string lineage) => Allows(lineage, Model2026Versions);

    private static bool Allows(string lineage, IReadOnlyList<string> allowedComponents)
    {
        if (string.IsNullOrWhiteSpace(lineage)) return false;
        var components = lineage.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return components.Length > 0
            && components.Distinct(StringComparer.Ordinal).Count() == components.Length
            && components.All(component => allowedComponents.Contains(component, StringComparer.Ordinal));
    }
}

public sealed record BotGModelLineageConfiguration
{
    private const string HomeGoals2026 = "targethomegoals-2026-08-09-trial-15";
    private const string AwayGoals2026 = "targetawaygoals-2026-08-09-trial-48";
    private const string TotalGoals2026 = "targettotalgoals-2026-08-09-trial-53";

    public BotGBaseModelLineageConfiguration TotalGoals { get; init; } = new()
    {
        Model2026Versions = [TotalGoals2026]
    };

    // Neutral-venue evaluation averages the normal and swapped team models, so both
    // component versions are explicitly allowed. The persisted lineage still records
    // the exact ordered, de-duplicated signature that was actually used.
    public BotGBaseModelLineageConfiguration HomeTeamGoals { get; init; } = new()
    {
        Model2026Versions = [HomeGoals2026, AwayGoals2026]
    };

    public BotGBaseModelLineageConfiguration AwayTeamGoals { get; init; } = new()
    {
        Model2026Versions = [AwayGoals2026, HomeGoals2026]
    };

    public BotGBaseModelLineageConfiguration For(BotGMarketType marketType) => marketType switch
    {
        BotGMarketType.TotalGoals => TotalGoals,
        BotGMarketType.HomeTeamGoals => HomeTeamGoals,
        BotGMarketType.AwayTeamGoals => AwayTeamGoals,
        _ => throw new ArgumentOutOfRangeException(nameof(marketType), marketType, "Unsupported Bot G market.")
    };
}

public sealed record BotGFeatureConfiguration
{
    public IReadOnlyList<int> Windows { get; init; } = [5, 10, 20];
    public double DecayFactor { get; init; } = 0.85d;
    public int RequiredVenueMatches { get; init; } = 8;
    public int MinimumHistoricalMatches { get; init; } = 8;
    public double MinimumStandardDeviation { get; init; } = 0.25d;
    public double LineHistoryPriorStrength { get; init; } = 20d;
    public double LineHitRatePriorMean { get; init; } = 0.50d;
    public double PushRatePriorMean { get; init; } = 0.08d;
}

public sealed record BotGMetaModelConfiguration
{
    public bool Required { get; init; } = true;
    public string ModelVersion { get; init; } = BotGConfiguration.DefaultMetaModelVersion;
    public string FeatureSchemaVersion { get; init; } = BotGConfiguration.DefaultFeatureSchemaVersion;
    public double MaximumAbsoluteResidualLogit { get; init; } = 4d;
}

public sealed record BotGCalibrationConfiguration
{
    public string Version { get; init; } = "bot-g-calibration-1.0.0";
    public string Method { get; init; } = "BetaCalibration";
    public int MinimumEffectiveSampleSize { get; init; } = 20;
    public int OutcomeAvailabilityLagHours { get; init; } = 8;
    public double GlobalPriorStrength { get; init; } = 80d;
    public double MarketPriorStrength { get; init; } = 60d;
    public double SelectionPriorStrength { get; init; } = 40d;
    public double BookmakerPriorStrength { get; init; } = 40d;
}

public sealed record BotGUncertaintyConfiguration
{
    public string Version { get; init; } = "bot-g-uncertainty-1.0.0";
    public double ConfidenceZScore { get; init; } = 1.645d;
    public double ConservativeLambda { get; init; } = 1d;
    public double MinimumUncertainty { get; init; } = 0.005d;
    public double MaximumUncertainty { get; init; } = 0.25d;
    public bool UseLowerBound { get; init; } = true;
}

public sealed record BotGOodConfiguration
{
    public string Version { get; init; } = "bot-g-ood-1.0.0";
    public int MinimumReferenceSampleSize { get; init; } = 30;
    public double RobustZScoreThreshold { get; init; } = 3.5d;
    public double SevereRobustZScore { get; init; } = 8d;
}

public sealed record BotGThresholdConfiguration
{
    public double MinimumOdds { get; init; } = 1.60d;
    public double MaximumOdds { get; init; } = 2.20d;
    public double MinimumFinalProbability { get; init; } = 0.54d;
    public double MinimumConservativeEdge { get; init; } = 0.02d;
    public double MinimumConservativeExpectedValue { get; init; } = 0.015d;
    public double MinimumDataQuality { get; init; } = 0.65d;
    public double MinimumCalibrationReliability { get; init; } = 0.30d;
    public double MaximumProbabilityUncertainty { get; init; } = 0.08d;
    public double MaximumOodScore { get; init; } = 0.70d;
    public double MaximumModelDisagreement { get; init; } = 1.50d;
    public int MinimumHistoricalMatches { get; init; } = 8;
    public int MinimumSettlementEffectiveSampleSize { get; init; } = 40;
    public int MaximumOddsAgeMinutes { get; init; } = 120;
}

public sealed record BotGRankingConfiguration
{
    public double ConservativeExpectedValueWeight { get; init; } = 0.35d;
    public double ConservativeEdgeWeight { get; init; } = 0.25d;
    public double CalibrationReliabilityWeight { get; init; } = 0.15d;
    public double DataQualityWeight { get; init; } = 0.10d;
    public double InverseUncertaintyWeight { get; init; } = 0.10d;
    public double ContextAgreementWeight { get; init; } = 0.05d;
}
