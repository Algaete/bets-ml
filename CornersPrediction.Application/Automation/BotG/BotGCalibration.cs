using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

/// <summary>
/// Applies leakage-safe beta-calibration profiles from global GOALS to the exact bookmaker leaf.
/// A profile from another family, market, side or bookmaker can never enter the chain.
/// </summary>
public sealed class BotGHierarchicalCalibrationService : IBotGCalibrationService
{
    private const double Epsilon = 1e-12d;

    public BotGCalibrationResult Calibrate(BotGCalibrationInput input, BotGConfiguration configuration)
    {
        var config = BotGConfiguration.Validate(configuration).Calibration;
        if (input.PredictionTimestampUtc.Kind != DateTimeKind.Utc)
            return BotGCalibrationResult.Unavailable(input.CandidateProbability, "PredictionTimestampUtc must be explicit UTC.");
        if (!double.IsFinite(input.CandidateProbability)
            || input.CandidateProbability <= 0d || input.CandidateProbability >= 1d)
            return BotGCalibrationResult.Unavailable(input.CandidateProbability, "Candidate probability must be strictly between zero and one.");
        if (string.IsNullOrWhiteSpace(input.Bookmaker))
            return BotGCalibrationResult.Unavailable(input.CandidateProbability, "Bookmaker is required for calibration isolation.");

        var desired = new[]
        {
            new DesiredProfile(BotGCalibrationLevel.GlobalGoals, config.GlobalPriorStrength,
                profile => profile.Key.MarketType is null && profile.Key.Selection is null && string.IsNullOrWhiteSpace(profile.Key.Bookmaker)),
            new DesiredProfile(BotGCalibrationLevel.MarketType, config.MarketPriorStrength,
                profile => profile.Key.MarketType == input.MarketType && profile.Key.Selection is null && string.IsNullOrWhiteSpace(profile.Key.Bookmaker)),
            new DesiredProfile(BotGCalibrationLevel.MarketTypeAndSelection, config.SelectionPriorStrength,
                profile => profile.Key.MarketType == input.MarketType && profile.Key.Selection == input.Selection && string.IsNullOrWhiteSpace(profile.Key.Bookmaker)),
            new DesiredProfile(BotGCalibrationLevel.MarketTypeSelectionAndBookmaker, config.BookmakerPriorStrength,
                profile => profile.Key.MarketType == input.MarketType && profile.Key.Selection == input.Selection
                    && string.Equals(profile.Key.Bookmaker?.Trim(), input.Bookmaker.Trim(), StringComparison.OrdinalIgnoreCase))
        };

        var usable = input.Profiles
            .Where(profile => profile.Key.Family.Equals(BotGMarketQuote.MarketFamily, StringComparison.OrdinalIgnoreCase))
            .Where(profile => IsValidProfile(profile, config.Method))
            .Where(profile => IsEvidenceAvailable(profile, input.PredictionTimestampUtc, config.OutcomeAvailabilityLagHours))
            .ToArray();

        var selected = desired
            .Select(level => new SelectedProfile(level, ChooseNewest(usable.Where(level.Matches))))
            .Where(value => value.Profile is not null)
            .ToArray();
        if (selected.Length == 0)
            return BotGCalibrationResult.Unavailable(input.CandidateProbability, "No leakage-safe GOALS calibration profile matches this market.");

        var maximumEvidence = selected.Max(value => value.Profile!.EffectiveSampleSize);
        if (maximumEvidence < config.MinimumEffectiveSampleSize)
            return BotGCalibrationResult.Unavailable(
                input.CandidateProbability,
                $"Calibration effective sample size {maximumEvidence:0.###} is below {config.MinimumEffectiveSampleSize}.");

        var probability = input.CandidateProbability;
        var residualUnreliability = 1d;
        foreach (var item in selected)
        {
            var profile = item.Profile!;
            // Every hierarchical leaf was fitted against the same OOF candidate probability;
            // reproduce that training contract rather than recursively calibrating a calibration.
            var target = BetaCalibrate(input.CandidateProbability, profile);
            var reliability = profile.EffectiveSampleSize / (profile.EffectiveSampleSize + item.Desired.PriorStrength);
            probability = Math.Clamp((1d - reliability) * probability + reliability * target, Epsilon, 1d - Epsilon);
            residualUnreliability *= 1d - reliability;
        }

        var leaf = selected[^1];
        return new BotGCalibrationResult(
            true,
            input.CandidateProbability,
            probability,
            Math.Clamp(1d - residualUnreliability, 0d, 1d),
            leaf.Profile!.EffectiveSampleSize,
            string.Join(">", selected.Select(item => item.Profile!.Version)),
            leaf.Desired.Level,
            selected.Select(item => item.Desired.Level).ToArray());
    }

    private static BotGCalibrationProfile? ChooseNewest(IEnumerable<BotGCalibrationProfile> profiles) => profiles
        .OrderByDescending(profile => profile.EvidenceAvailableThroughUtc)
        .ThenByDescending(profile => profile.EffectiveSampleSize)
        .ThenByDescending(profile => profile.Version, StringComparer.Ordinal)
        .FirstOrDefault();

    private static bool IsValidProfile(BotGCalibrationProfile profile, string expectedMethod) =>
        profile.Method.Equals(expectedMethod, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(profile.Version)
        && profile.SampleSize >= 0
        && double.IsFinite(profile.EffectiveSampleSize)
        && profile.EffectiveSampleSize > 0d
        && double.IsFinite(profile.Intercept)
        && double.IsFinite(profile.LogProbabilityCoefficient)
        && double.IsFinite(profile.LogOneMinusProbabilityCoefficient)
        && profile.EvidenceAvailableThroughUtc.Kind == DateTimeKind.Utc;

    private static bool IsEvidenceAvailable(BotGCalibrationProfile profile, DateTime asOfUtc, int lagHours) =>
        profile.EvidenceAvailableThroughUtc.AddHours(lagHours) < asOfUtc;

    private static double BetaCalibrate(double probability, BotGCalibrationProfile profile)
    {
        var p = Math.Clamp(probability, Epsilon, 1d - Epsilon);
        var logit = profile.Intercept
            + profile.LogProbabilityCoefficient * Math.Log(p)
            + profile.LogOneMinusProbabilityCoefficient * Math.Log(1d - p);
        return BotGLogitResidual.Sigmoid(logit);
    }

    private sealed record DesiredProfile(
        BotGCalibrationLevel Level,
        double PriorStrength,
        Func<BotGCalibrationProfile, bool> Matches);

    private sealed record SelectedProfile(DesiredProfile Desired, BotGCalibrationProfile? Profile);
}
