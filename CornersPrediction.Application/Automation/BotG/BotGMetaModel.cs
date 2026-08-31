using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

public static class BotGLogitResidual
{
    private const double Epsilon = 1e-12d;

    public static double Apply(double marketProbability, double residualLogit, double maximumAbsoluteResidualLogit = 4d)
    {
        if (!double.IsFinite(marketProbability) || marketProbability <= 0d || marketProbability >= 1d)
            throw new ArgumentOutOfRangeException(nameof(marketProbability), "Market probability must be strictly between zero and one.");
        if (!double.IsFinite(residualLogit))
            throw new ArgumentOutOfRangeException(nameof(residualLogit), "Residual logit must be finite.");
        if (!double.IsFinite(maximumAbsoluteResidualLogit) || maximumAbsoluteResidualLogit <= 0d)
            throw new ArgumentOutOfRangeException(nameof(maximumAbsoluteResidualLogit));

        var boundedMarket = Math.Clamp(marketProbability, Epsilon, 1d - Epsilon);
        var marketLogit = Math.Log(boundedMarket / (1d - boundedMarket));
        return Sigmoid(marketLogit + Math.Clamp(residualLogit, -maximumAbsoluteResidualLogit, maximumAbsoluteResidualLogit));
    }

    public static double Sigmoid(double value) => value >= 0d
        ? 1d / (1d + Math.Exp(-value))
        : Math.Exp(value) / (1d + Math.Exp(value));
}

public sealed class UnavailableBotGMetaModelService : IBotGMetaModelService
{
    private readonly string _reason;

    public UnavailableBotGMetaModelService(string reason = "The Bot G meta-model artifact is unavailable.") =>
        _reason = string.IsNullOrWhiteSpace(reason) ? "The Bot G meta-model artifact is unavailable." : reason;

    public BotGMetaModelPrediction Predict(BotGMetaModelInput input) =>
        BotGMetaModelPrediction.Unavailable(_reason);
}

/// <summary>
/// Deterministic scorer for an already loaded Bot G JSON artifact.  It performs no I/O and is
/// safe to register as a singleton.
/// </summary>
public sealed class InMemoryBotGMetaModelService : IBotGMetaModelService, IBotGArtifactEvidenceProvider
{
    private readonly BotGModelArtifact _artifact;

    public InMemoryBotGMetaModelService(BotGModelArtifact artifact)
    {
        _artifact = ValidateArtifact(artifact);
    }

    public IReadOnlyList<BotGCalibrationProfile> CalibrationProfiles => _artifact.Calibration;
    public IReadOnlyList<BotGOodFeatureReference> OodReferenceFeatures => _artifact.OodFeatureStats;

    public BotGMetaModelPrediction Predict(BotGMetaModelInput input)
    {
        if (input.PredictionTimestampUtc.Kind != DateTimeKind.Utc)
            return Unavailable("PredictionTimestampUtc must be explicit UTC.");
        if (!TryValidateRuntimeCompatibility(input, out var compatibilityError))
            return Unavailable(compatibilityError!);
        if (!string.Equals(input.FeatureSchemaVersion, _artifact.FeatureSchemaVersion, StringComparison.Ordinal))
            return Unavailable(
                $"Feature schema mismatch: input '{input.FeatureSchemaVersion}', artifact '{_artifact.FeatureSchemaVersion}'.");
        if (_artifact.TrainedThroughUtc >= input.PredictionTimestampUtc)
            return Unavailable("The meta-model trained-through timestamp is not strictly before prediction time.");
        if (!double.IsFinite(input.MarketNoVigProbability)
            || input.MarketNoVigProbability <= 0d || input.MarketNoVigProbability >= 1d)
            return Unavailable("A strict market probability between zero and one is required.");

        if (!TryScore(_artifact.Model.Intercept, _artifact.Model.Features, input.NumericFeatures, out var residual, out var error))
            return Unavailable(error!);

        var probability = BotGLogitResidual.Apply(
            input.MarketNoVigProbability,
            residual,
            _artifact.RuntimeSettings.MaximumAbsoluteResidualLogit);
        var ensembleProbabilities = new List<double>(_artifact.Ensemble.Count + 1) { probability };
        foreach (var member in _artifact.Ensemble)
        {
            if (!TryScore(member.Intercept, member.Features, input.NumericFeatures, out var memberResidual, out error))
                return Unavailable($"Ensemble member '{member.Name}' is unavailable: {error}");
            ensembleProbabilities.Add(BotGLogitResidual.Apply(
                input.MarketNoVigProbability,
                memberResidual,
                _artifact.RuntimeSettings.MaximumAbsoluteResidualLogit));
        }

        var distribution = ResolveSettlementDistribution(input);
        return new BotGMetaModelPrediction(
            true,
            probability,
            residual,
            _artifact.ModelVersion,
            _artifact.FeatureSchemaVersion,
            _artifact.TrainedThroughUtc,
            StandardDeviation(ensembleProbabilities),
            distribution);
    }

    private BotGOutcomeDistribution? ResolveSettlementDistribution(BotGMetaModelInput input)
    {
        var matches = _artifact.SettlementProfiles
            .Where(profile => Matches(profile.Key, input))
            .Where(profile => !profile.Line.HasValue || profile.Line.Value == input.Line)
            .Where(profile => profile.EffectiveSampleSize >= _artifact.RuntimeSettings.MinimumSettlementEffectiveSampleSize)
            .Where(profile => profile.EvidenceAvailableThroughUtc.AddHours(_artifact.RuntimeSettings.SettlementEvidenceLagHours)
                < input.PredictionTimestampUtc)
            .OrderByDescending(profile => profile.Line.HasValue)
            .ThenByDescending(profile => Specificity(profile.Key))
            .ThenByDescending(profile => profile.EffectiveSampleSize)
            .ToArray();
        return matches.Length == 0 ? null : BotGOutcomeDistribution.Validate(matches[0].Distribution);
    }

    private static bool Matches(BotGCalibrationKey key, BotGMetaModelInput input) =>
        key.Family.Equals(BotGMarketQuote.MarketFamily, StringComparison.OrdinalIgnoreCase)
        && (!key.MarketType.HasValue || key.MarketType == input.MarketType)
        && (!key.Selection.HasValue || key.Selection == input.Selection)
        && (string.IsNullOrWhiteSpace(key.Bookmaker)
            || key.Bookmaker.Equals(input.Bookmaker, StringComparison.OrdinalIgnoreCase));

    private static int Specificity(BotGCalibrationKey key) =>
        (key.MarketType.HasValue ? 1 : 0)
        + (key.Selection.HasValue ? 1 : 0)
        + (!string.IsNullOrWhiteSpace(key.Bookmaker) ? 1 : 0);

    private static bool TryScore(
        double intercept,
        IReadOnlyList<BotGMetaFeatureCoefficient> coefficients,
        IReadOnlyDictionary<string, double> vector,
        out double score,
        out string? error)
    {
        score = intercept;
        error = null;
        if (!double.IsFinite(intercept))
        {
            error = "The model intercept is not finite.";
            return false;
        }

        foreach (var coefficient in coefficients)
        {
            if (!vector.TryGetValue(coefficient.Name, out var value))
            {
                error = $"Required feature '{coefficient.Name}' is missing.";
                return false;
            }
            if (!double.IsFinite(value))
            {
                error = $"Feature '{coefficient.Name}' is not finite.";
                return false;
            }

            score += ((value - coefficient.Mean) / coefficient.Scale) * coefficient.Coefficient;
        }

        if (!double.IsFinite(score))
        {
            error = "The computed residual logit is not finite.";
            return false;
        }
        return true;
    }

    private BotGMetaModelPrediction Unavailable(string reason) => new(
        false,
        0d,
        0d,
        _artifact.ModelVersion,
        _artifact.FeatureSchemaVersion,
        _artifact.TrainedThroughUtc,
        0d,
        null,
        reason);

    private bool TryValidateRuntimeCompatibility(BotGMetaModelInput input, out string? error)
    {
        error = null;
        BotGConfiguration configuration;
        try
        {
            configuration = BotGConfiguration.Validate(input.RuntimeConfiguration);
        }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
        {
            error = $"Runtime configuration is invalid: {exception.Message}";
            return false;
        }

        if (!string.Equals(configuration.ConfigurationVersion, _artifact.ConfigurationVersion, StringComparison.Ordinal))
        {
            error = $"Configuration version mismatch: runtime '{configuration.ConfigurationVersion}', artifact '{_artifact.ConfigurationVersion}'.";
            return false;
        }
        if (!string.Equals(configuration.MetaModel.ModelVersion, _artifact.ModelVersion, StringComparison.Ordinal))
        {
            error = $"Meta-model version mismatch: runtime '{configuration.MetaModel.ModelVersion}', artifact '{_artifact.ModelVersion}'.";
            return false;
        }
        if (!string.Equals(configuration.FeatureSchemaVersion, _artifact.FeatureSchemaVersion, StringComparison.Ordinal)
            || !string.Equals(configuration.MetaModel.FeatureSchemaVersion, _artifact.FeatureSchemaVersion, StringComparison.Ordinal))
        {
            error = "Runtime and artifact feature-schema versions are incompatible.";
            return false;
        }
        if (!SameMarkets(configuration.SupportedMarkets, _artifact.SupportedMarkets)
            || !_artifact.SupportedMarkets.Contains(input.MarketType))
        {
            error = "Runtime and artifact supported GOALS markets are incompatible.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.LegacyModelVersion)
            || !string.Equals(input.LegacyModelVersion, configuration.LegacyModelVersion, StringComparison.Ordinal))
        {
            error = $"Legacy base-model version mismatch: runtime '{input.LegacyModelVersion}', configured '{configuration.LegacyModelVersion}'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(input.Model2026Version)
            || !string.Equals(input.Model2026Version, configuration.Model2026Version, StringComparison.Ordinal))
        {
            error = $"Models 2026 version mismatch: runtime '{input.Model2026Version}', configured '{configuration.Model2026Version}'.";
            return false;
        }
        if (!_artifact.Training.LegacyModelVersions.Contains(input.LegacyModelVersion, StringComparer.Ordinal)
            || !_artifact.Training.Model2026Versions.Contains(input.Model2026Version, StringComparer.Ordinal))
        {
            error = "Runtime base-model lineage is absent from the artifact training lineage.";
            return false;
        }

        var settings = _artifact.RuntimeSettings;
        if (!NearlyEqual(settings.MaximumAbsoluteResidualLogit, configuration.MetaModel.MaximumAbsoluteResidualLogit)
            || !NearlyEqual(settings.MinimumSettlementEffectiveSampleSize,
                configuration.Thresholds.MinimumSettlementEffectiveSampleSize)
            || settings.SettlementEvidenceLagHours != configuration.Calibration.OutcomeAvailabilityLagHours)
        {
            error = "Runtime meta/settlement settings do not match the artifact contract.";
            return false;
        }

        var uncertainty = _artifact.Uncertainty;
        var runtimeUncertainty = configuration.Uncertainty;
        if (!string.Equals(uncertainty.Version, runtimeUncertainty.Version, StringComparison.Ordinal)
            || !NearlyEqual(uncertainty.ConfidenceZScore, runtimeUncertainty.ConfidenceZScore)
            || !NearlyEqual(uncertainty.ConservativeLambda, runtimeUncertainty.ConservativeLambda)
            || uncertainty.UseLowerBound != runtimeUncertainty.UseLowerBound
            || !NearlyEqual(uncertainty.MinimumUncertainty, runtimeUncertainty.MinimumUncertainty)
            || !NearlyEqual(uncertainty.MaximumUncertainty, runtimeUncertainty.MaximumUncertainty))
        {
            error = "Runtime uncertainty settings do not match the artifact contract.";
            return false;
        }

        var ood = _artifact.Ood;
        var runtimeOod = configuration.OutOfDistribution;
        if (!string.Equals(ood.Version, runtimeOod.Version, StringComparison.Ordinal)
            || ood.MinimumReferenceSampleSize != runtimeOod.MinimumReferenceSampleSize
            || !NearlyEqual(ood.RobustZScoreThreshold, runtimeOod.RobustZScoreThreshold)
            || !NearlyEqual(ood.SevereRobustZScore, runtimeOod.SevereRobustZScore))
        {
            error = "Runtime OOD settings do not match the artifact contract.";
            return false;
        }

        if (_artifact.Calibration.Any(profile =>
                !string.Equals(profile.Version, configuration.Calibration.Version, StringComparison.Ordinal)
                || !string.Equals(profile.Method, configuration.Calibration.Method, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Runtime calibration identity does not match the artifact profiles.";
            return false;
        }
        return true;
    }

    private static BotGModelArtifact ValidateArtifact(BotGModelArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!string.Equals(
                artifact.ConfigurationVersion,
                BotGConfiguration.DefaultConfigurationVersion,
                StringComparison.Ordinal))
            throw new ArgumentException(
                $"Bot G only accepts configurationVersion '{BotGConfiguration.DefaultConfigurationVersion}'.");
        if (!string.Equals(artifact.Family, BotGMarketQuote.MarketFamily, StringComparison.Ordinal))
            throw new ArgumentException("Bot G artifact family must be exactly GOALS.");
        if (!SameMarkets(artifact.SupportedMarkets, SupportedGoalMarkets))
            throw new ArgumentException("Bot G artifact supportedMarkets must contain exactly the three v1 GOALS markets.");
        if (string.IsNullOrWhiteSpace(artifact.ModelVersion)
            || string.IsNullOrWhiteSpace(artifact.FeatureSchemaVersion))
            throw new ArgumentException("The Bot G artifact requires model and feature-schema versions.");
        if (!artifact.Deployable)
            throw new ArgumentException("The Bot G artifact is not marked deployable by the offline promotion gate.");
        if (artifact.Synthetic)
            throw new ArgumentException("Synthetic Bot G artifacts cannot be loaded by the production runtime.");
        if (artifact.Model is null || artifact.RuntimeSettings is null
            || artifact.Uncertainty is null || artifact.Ood is null || artifact.Training is null)
            throw new ArgumentException("The Bot G artifact requires model, runtime, uncertainty, OOD and training sections.");
        if (!double.IsFinite(artifact.RuntimeSettings.MaximumAbsoluteResidualLogit)
            || artifact.RuntimeSettings.MaximumAbsoluteResidualLogit <= 0d
            || !double.IsFinite(artifact.RuntimeSettings.MinimumSettlementEffectiveSampleSize)
            || artifact.RuntimeSettings.MinimumSettlementEffectiveSampleSize < 1d
            || artifact.RuntimeSettings.SettlementEvidenceLagHours is < 0 or > 168)
            throw new ArgumentException("The Bot G artifact runtime settings are invalid.");
        if (string.IsNullOrWhiteSpace(artifact.Uncertainty.Version)
            || !string.Equals(
                artifact.Uncertainty.Method,
                ExpectedUncertaintyMethod,
                StringComparison.Ordinal)
            || !double.IsFinite(artifact.Uncertainty.ConfidenceZScore)
            || artifact.Uncertainty.ConfidenceZScore < 0d
            || !double.IsFinite(artifact.Uncertainty.ConservativeLambda)
            || artifact.Uncertainty.ConservativeLambda < 0d
            || !double.IsFinite(artifact.Uncertainty.MinimumUncertainty)
            || !double.IsFinite(artifact.Uncertainty.MaximumUncertainty)
            || artifact.Uncertainty.MinimumUncertainty < 0d
            || artifact.Uncertainty.MaximumUncertainty < artifact.Uncertainty.MinimumUncertainty)
            throw new ArgumentException("The Bot G artifact uncertainty settings are invalid.");
        if (string.IsNullOrWhiteSpace(artifact.Ood.Version)
            || !string.Equals(artifact.Ood.Method, "robust-mad-percentile-v1", StringComparison.Ordinal)
            || artifact.Ood.MinimumReferenceSampleSize < 1
            || !double.IsFinite(artifact.Ood.RobustZScoreThreshold)
            || !double.IsFinite(artifact.Ood.SevereRobustZScore)
            || artifact.Ood.RobustZScoreThreshold <= 0d
            || artifact.Ood.SevereRobustZScore < artifact.Ood.RobustZScoreThreshold)
            throw new ArgumentException("The Bot G artifact OOD settings are invalid.");
        var legacyModelVersions = NormalizeVersions(
            artifact.Training.LegacyModelVersions,
            "legacy model");
        var model2026Versions = NormalizeVersions(
            artifact.Training.Model2026Versions,
            "Models 2026");
        if (!string.Equals(artifact.Model.Type, "LogitResidualLogistic", StringComparison.Ordinal))
            throw new ArgumentException("Bot G only accepts a LogitResidualLogistic model artifact.");
        if (!double.IsFinite(artifact.Model.Intercept))
            throw new ArgumentException("The Bot G model intercept must be finite.");
        if (artifact.Ensemble is null || artifact.Calibration is null
            || artifact.OodFeatureStats is null || artifact.SettlementProfiles is null)
            throw new ArgumentException("Bot G artifact collections cannot be null.");
        ValidateCoefficients(artifact.Model.Features, "model");
        foreach (var member in artifact.Ensemble)
        {
            if (member is null || string.IsNullOrWhiteSpace(member.Name))
                throw new ArgumentException("Every Bot G ensemble member requires a name.");
            if (!double.IsFinite(member.Intercept))
                throw new ArgumentException($"Bot G ensemble member '{member.Name}' has a non-finite intercept.");
            ValidateCoefficients(member.Features, $"ensemble member '{member.Name}'");
        }
        var trainedThroughUtc = NormalizeExplicitUtc(artifact.TrainedThroughUtc, "trainedThroughUtc");
        var calibration = artifact.Calibration.Select(profile =>
        {
            if (profile is null || profile.Key is null
                || !profile.Key.Family.Equals(BotGMarketQuote.MarketFamily, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Every calibration profile must have an explicit GOALS key.");
            if (string.IsNullOrWhiteSpace(profile.Version) || string.IsNullOrWhiteSpace(profile.Method)
                || profile.SampleSize < 0 || !double.IsFinite(profile.EffectiveSampleSize)
                || profile.EffectiveSampleSize < 0d || !double.IsFinite(profile.Intercept)
                || !double.IsFinite(profile.LogProbabilityCoefficient)
                || !double.IsFinite(profile.LogOneMinusProbabilityCoefficient))
                throw new ArgumentException("The Bot G artifact contains an invalid calibration profile.");
            return profile with
            {
                EvidenceAvailableThroughUtc = NormalizeExplicitUtc(
                    profile.EvidenceAvailableThroughUtc,
                    $"calibration profile '{profile.Version}' evidenceAvailableThroughUtc")
            };
        }).ToArray();
        var oodFeatureStats = artifact.OodFeatureStats.Select(reference =>
        {
            if (reference is null || string.IsNullOrWhiteSpace(reference.Name)
                || reference.SampleSize < 0 || !double.IsFinite(reference.Median)
                || !double.IsFinite(reference.MedianAbsoluteDeviation)
                || reference.MedianAbsoluteDeviation < 0d
                || !double.IsFinite(reference.Percentile01)
                || !double.IsFinite(reference.Percentile99)
                || reference.Percentile99 < reference.Percentile01)
                throw new ArgumentException("The Bot G artifact contains an invalid OOD feature reference.");
            return reference;
        }).ToArray();
        var settlementProfiles = artifact.SettlementProfiles.Select(profile =>
        {
            if (profile is null || profile.Key is null
                || !profile.Key.Family.Equals(BotGMarketQuote.MarketFamily, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Every settlement profile must have an explicit GOALS key.");
            if (!double.IsFinite(profile.EffectiveSampleSize) || profile.EffectiveSampleSize < 0d || profile.SampleSize < 0)
                throw new ArgumentException("Settlement profile sample sizes must be finite and non-negative.");
            if (profile.Line.HasValue && profile.Line.Value < 0m)
                throw new ArgumentException("Settlement profile lines must be non-negative.");
            if (profile.Distribution is null)
                throw new ArgumentException("Every settlement profile requires a five-state distribution.");
            BotGOutcomeDistribution.Validate(profile.Distribution);
            return profile with
            {
                EvidenceAvailableThroughUtc = NormalizeExplicitUtc(
                    profile.EvidenceAvailableThroughUtc,
                    "settlement profile evidenceAvailableThroughUtc")
            };
        }).ToArray();
        return artifact with
        {
            TrainedThroughUtc = trainedThroughUtc,
            Training = artifact.Training with
            {
                LegacyModelVersions = legacyModelVersions,
                Model2026Versions = model2026Versions
            },
            Calibration = calibration,
            OodFeatureStats = oodFeatureStats,
            SettlementProfiles = settlementProfiles
        };
    }

    private static string[] NormalizeVersions(IReadOnlyList<string>? versions, string label)
    {
        if (versions is null || versions.Count == 0 || versions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"The Bot G artifact requires non-empty {label} training versions.");
        var normalized = versions.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length != versions.Count)
            throw new ArgumentException($"The Bot G artifact contains duplicate {label} training versions.");
        return normalized;
    }

    private static bool SameMarkets(
        IReadOnlyList<BotGMarketType>? left,
        IReadOnlyList<BotGMarketType>? right) =>
        left is not null && right is not null
        && left.Count == right.Count
        && left.All(Enum.IsDefined)
        && left.Distinct().Count() == left.Count
        && left.Order().SequenceEqual(right.Order());

    private static bool NearlyEqual(double left, double right) =>
        double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= 1e-12d;

    private static readonly BotGMarketType[] SupportedGoalMarkets =
    [
        BotGMarketType.TotalGoals,
        BotGMarketType.HomeTeamGoals,
        BotGMarketType.AwayTeamGoals
    ];

    private const string ExpectedUncertaintyMethod =
        "fixture-cluster bootstrap dispersion plus calibration sampling error";

    private static void ValidateCoefficients(IReadOnlyList<BotGMetaFeatureCoefficient>? values, string label)
    {
        if (values is null || values.Any(value => value is null))
            throw new ArgumentException($"The {label} feature collection cannot contain null values.");
        if (values.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw new ArgumentException($"The {label} contains duplicate feature names.");
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Name) || !double.IsFinite(value.Mean)
                || !double.IsFinite(value.Scale) || value.Scale <= 0d
                || !double.IsFinite(value.Coefficient))
                throw new ArgumentException($"The {label} contains an invalid standardized coefficient.");
        }
    }

    private static double StandardDeviation(IReadOnlyCollection<double> values)
    {
        if (values.Count <= 1) return 0d;
        var mean = values.Average();
        return Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2d)));
    }

    private static DateTime NormalizeExplicitUtc(DateTime value, string label) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => throw new ArgumentException($"Bot G artifact {label} must carry an explicit UTC offset.")
    };
}
