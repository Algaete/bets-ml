using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

public interface IBotGFeatureBuilder
{
    BotGFeatures Build(BotGFeatureBuildInput input, BotGConfiguration configuration);
}

public interface IMarketProbabilityService
{
    BotGMarketProbabilityResult Calculate(BotGMarketQuote quote);
}

public interface IBotGMetaModelService
{
    BotGMetaModelPrediction Predict(BotGMetaModelInput input);
}

/// <summary>
/// Exposes the time-bounded evidence bundled with the currently loaded Bot G artifact.
/// Runtime loaders normally implement this together with <see cref="IBotGMetaModelService"/>.
/// </summary>
public interface IBotGArtifactEvidenceProvider
{
    IReadOnlyList<BotGCalibrationProfile> CalibrationProfiles { get; }

    IReadOnlyList<BotGOodFeatureReference> OodReferenceFeatures { get; }
}

public sealed record BotGCalibrationInput(
    DateTime PredictionTimestampUtc,
    BotGMarketType MarketType,
    BotGSelection Selection,
    string Bookmaker,
    double CandidateProbability,
    IReadOnlyList<BotGCalibrationProfile> Profiles);

public interface IBotGCalibrationService
{
    BotGCalibrationResult Calibrate(BotGCalibrationInput input, BotGConfiguration configuration);
}

public sealed record BotGUncertaintyInput(
    double FinalProbability,
    double EnsembleDispersion,
    double CalibrationEffectiveSampleSize);

public interface IBotGUncertaintyService
{
    BotGUncertaintyResult Estimate(BotGUncertaintyInput input, BotGConfiguration configuration);
}

public sealed record BotGOodInput(
    IReadOnlyDictionary<string, double> NumericFeatures,
    IReadOnlyList<BotGOodFeatureReference> ReferenceFeatures);

public interface IBotGOodService
{
    BotGOodResult Evaluate(BotGOodInput input, BotGConfiguration configuration);
}

public interface IBotGExpectedValueService
{
    BotGOutcomeDistribution Reanchor(
        BotGOutcomeDistribution baselineDistribution,
        double positiveReturnProbability);

    BotGExpectedValueResult Calculate(decimal selectedOdds, BotGOutcomeDistribution distribution);

    BotGExpectedValueResult CalculateConservative(
        decimal selectedOdds,
        BotGOutcomeDistribution distribution,
        double probabilityUncertainty,
        BotGConfiguration configuration);
}

public interface IBotGAbstentionService
{
    BotGDecision Decide(BotGDecisionInput input, BotGConfiguration configuration);
}

public interface IBotGSelector
{
    double Score(BotGCandidate candidate, BotGConfiguration configuration);

    IReadOnlyList<BotGCandidate> SelectBestPerFixture(
        IEnumerable<BotGCandidate> candidates,
        BotGConfiguration configuration);
}

public interface IBotGCandidateRepository
{
    Task<BotGCandidate> UpsertAsync(BotGCandidate candidate, CancellationToken cancellationToken);

    Task<IReadOnlyList<BotGCandidate>> GetByFixtureAsync(
        long fixtureId,
        string configurationVersion,
        CancellationToken cancellationToken);
}

public sealed class BotGTemporalLeakageException : InvalidOperationException
{
    public BotGTemporalLeakageException(string message) : base(message)
    {
    }
}
