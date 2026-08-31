namespace CornersPrediction.Domain.Automation.BotG;

/// <summary>
/// One named, standardized coefficient of the market-logit residual model.
/// Names are matched with ordinal comparison against <see cref="BotGFeatures.ToNumericVector"/>.
/// </summary>
public sealed record BotGMetaFeatureCoefficient(
    string Name,
    double Mean,
    double Scale,
    double Coefficient);

public sealed record BotGLogitResidualLogisticModel
{
    public string Type { get; init; } = "LogitResidualLogistic";
    public double Intercept { get; init; }
    public IReadOnlyList<BotGMetaFeatureCoefficient> Features { get; init; } = [];
}

public sealed record BotGArtifactRuntimeSettings
{
    public double MaximumAbsoluteResidualLogit { get; init; }
    public double MinimumSettlementEffectiveSampleSize { get; init; }
    public int SettlementEvidenceLagHours { get; init; }
}

public sealed record BotGArtifactUncertaintySettings
{
    public required string Version { get; init; }
    public required string Method { get; init; }
    public double ConfidenceZScore { get; init; }
    public double ConservativeLambda { get; init; }
    public bool UseLowerBound { get; init; }
    public double MinimumUncertainty { get; init; }
    public double MaximumUncertainty { get; init; }
}

public sealed record BotGArtifactOodSettings
{
    public required string Version { get; init; }
    public required string Method { get; init; }
    public int MinimumReferenceSampleSize { get; init; }
    public double RobustZScoreThreshold { get; init; }
    public double SevereRobustZScore { get; init; }
}

public sealed record BotGArtifactFootballIntelligenceSettings
{
    public bool Enabled { get; init; }
    public required string Version { get; init; }
    public double Weight { get; init; }
    public double MaximumProbabilityAdjustment { get; init; }
    public double MinimumTeamConfidence { get; init; }
    public int MaximumSnapshotAgeMinutes { get; init; }
    public int MinimumActionableFacts { get; init; }
    public int MinimumIndependentSources { get; init; }
    public double AttackWeight { get; init; }
    public double DefenceWeight { get; init; }
    public double WidthWeight { get; init; }
    public double SetPieceWeight { get; init; }
}

public sealed record BotGArtifactMarketModelLineage
{
    public BotGMarketType MarketType { get; init; }
    public IReadOnlyList<string> LegacyModelLineages { get; init; } = [];
    public IReadOnlyList<string> Model2026Lineages { get; init; } = [];
}

public sealed record BotGArtifactTrainingMetadata
{
    public IReadOnlyList<string> LegacyModelVersions { get; init; } = [];
    public IReadOnlyList<string> Model2026Versions { get; init; } = [];
    public IReadOnlyList<BotGArtifactMarketModelLineage> MarketLineages { get; init; } = [];
}

public sealed record BotGEnsembleMember
{
    public string Name { get; init; } = string.Empty;
    public double Intercept { get; init; }
    public IReadOnlyList<BotGMetaFeatureCoefficient> Features { get; init; } = [];
}

public sealed record BotGSettlementDistributionProfile
{
    public BotGCalibrationKey Key { get; init; } = BotGCalibrationKey.GlobalGoals;
    public decimal? Line { get; init; }
    public BotGOutcomeDistribution Distribution { get; init; } = BotGOutcomeDistribution.Binary(0.5d);
    public int SampleSize { get; init; }
    public double EffectiveSampleSize { get; init; }
    public DateTime EvidenceAvailableThroughUtc { get; init; }
}

/// <summary>
/// JSON-compatible, immutable snapshot loaded by the runtime.  The property names serialize to
/// configurationVersion, family, supportedMarkets, modelVersion, featureSchemaVersion,
/// trainedThroughUtc, runtimeSettings, uncertainty, ood, training, model, ensemble,
/// calibration, oodFeatureStats and settlementProfiles under the repository's camel-case JSON convention.
/// </summary>
public sealed record BotGModelArtifact
{
    public required string ConfigurationVersion { get; init; }
    public required string TrainingContractVersion { get; init; }
    public required string Family { get; init; }
    public IReadOnlyList<BotGMarketType> SupportedMarkets { get; init; } = [];
    public required string ModelVersion { get; init; }
    public required string FeatureSchemaVersion { get; init; }
    public required DateTime TrainedThroughUtc { get; init; }
    public bool Deployable { get; init; }
    public bool Synthetic { get; init; }
    public required BotGArtifactRuntimeSettings RuntimeSettings { get; init; }
    public required BotGArtifactUncertaintySettings Uncertainty { get; init; }
    public required BotGArtifactOodSettings Ood { get; init; }
    public required BotGArtifactFootballIntelligenceSettings FootballIntelligence { get; init; }
    public required BotGArtifactTrainingMetadata Training { get; init; }
    public required BotGLogitResidualLogisticModel Model { get; init; }
    public IReadOnlyList<BotGEnsembleMember> Ensemble { get; init; } = [];
    public IReadOnlyList<BotGCalibrationProfile> Calibration { get; init; } = [];
    public IReadOnlyList<BotGOodFeatureReference> OodFeatureStats { get; init; } = [];
    public IReadOnlyList<BotGSettlementDistributionProfile> SettlementProfiles { get; init; } = [];
}
