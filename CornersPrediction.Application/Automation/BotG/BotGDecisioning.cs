using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

public sealed class BotGAbstentionService : IBotGAbstentionService
{
    public BotGDecision Decide(BotGDecisionInput input, BotGConfiguration configuration)
    {
        var config = BotGConfiguration.Validate(configuration);
        var abstain = new List<BotGDecisionReason>();
        var reject = new List<BotGDecisionReason>();

        if (!config.SupportedMarkets.Contains(input.Quote.MarketType))
            abstain.Add(BotGDecisionReason.UnsupportedMarket);
        var lineSupported = input.Quote.Line >= 0m
            && input.Quote.Line * 4m == decimal.Truncate(input.Quote.Line * 4m);
        if (!lineSupported)
            abstain.Add(BotGDecisionReason.UnsupportedLine);
        if (input.Quote.SelectedOdds is not > 1m || input.Quote.OppositeOdds is not > 1m)
            abstain.Add(BotGDecisionReason.InvalidOdds);
        if (!input.MarketProbability.IsAvailable)
            abstain.Add(BotGDecisionReason.NoVigUnavailable);

        var oddsAge = input.Quote.PredictionTimestampUtc - input.Quote.OddsTimestampUtc;
        if (input.Quote.PredictionTimestampUtc.Kind != DateTimeKind.Utc
            || input.Quote.OddsTimestampUtc.Kind != DateTimeKind.Utc
            || oddsAge < TimeSpan.Zero
            || oddsAge.TotalMinutes > config.Thresholds.MaximumOddsAgeMinutes)
            abstain.Add(BotGDecisionReason.StaleOdds);

        if (!input.MetaPrediction.IsAvailable)
            abstain.Add(MetaUnavailableReason(input.MetaPrediction.UnavailableReason));
        if (lineSupported
            && BotGAsianSettlementCalculator.RequiresFiveStateDistribution(input.Quote.Line)
            && !input.SettlementDistributionAvailable)
            abstain.Add(BotGDecisionReason.SettlementDistributionUnavailable);
        if (!input.Calibration.IsAvailable)
            abstain.Add(BotGDecisionReason.InsufficientCalibrationEvidence);
        else
        {
            if (input.Calibration.EffectiveSampleSize < config.Calibration.MinimumEffectiveSampleSize)
                abstain.Add(BotGDecisionReason.InsufficientCalibrationEvidence);
            if (input.Calibration.Reliability < config.Thresholds.MinimumCalibrationReliability)
                abstain.Add(BotGDecisionReason.CalibrationUnreliable);
        }
        if (input.HistoricalMatches < config.Thresholds.MinimumHistoricalMatches)
            abstain.Add(BotGDecisionReason.InsufficientHistory);
        if (!double.IsFinite(input.DataQualityScore)
            || input.DataQualityScore < config.Thresholds.MinimumDataQuality)
            abstain.Add(BotGDecisionReason.LowDataQuality);
        if (!double.IsFinite(input.ModelDisagreement)
            || input.ModelDisagreement > config.Thresholds.MaximumModelDisagreement)
            abstain.Add(BotGDecisionReason.ModelDisagreement);
        if (!double.IsFinite(input.Uncertainty.ProbabilityUncertainty)
            || input.Uncertainty.ProbabilityUncertainty > config.Thresholds.MaximumProbabilityUncertainty)
            abstain.Add(BotGDecisionReason.HighUncertainty);
        if (!input.OutOfDistribution.IsAvailable
            || !double.IsFinite(input.OutOfDistribution.Score)
            || input.OutOfDistribution.Score > config.Thresholds.MaximumOodScore)
            abstain.Add(BotGDecisionReason.OutOfDistribution);

        var selectedOdds = input.Quote.SelectedOdds;
        if (selectedOdds.HasValue
            && (selectedOdds.Value < Convert.ToDecimal(config.Thresholds.MinimumOdds)
                || selectedOdds.Value > Convert.ToDecimal(config.Thresholds.MaximumOdds)))
            reject.Add(BotGDecisionReason.OddsOutOfRange);
        if (!double.IsFinite(input.FinalProbability)
            || input.FinalProbability < config.Thresholds.MinimumFinalProbability)
            reject.Add(BotGDecisionReason.LowFinalProbability);
        if (!double.IsFinite(input.ConservativeEdge)
            || input.ConservativeEdge < config.Thresholds.MinimumConservativeEdge)
            reject.Add(BotGDecisionReason.LowConservativeEdge);
        if (!double.IsFinite(input.ConservativeExpectedValue)
            || input.ConservativeExpectedValue < config.Thresholds.MinimumConservativeExpectedValue)
            reject.Add(BotGDecisionReason.LowConservativeExpectedValue);

        if (abstain.Count > 0)
            return Decision(BotGDecisionStatus.Abstain, abstain, "Bot G abstained because required evidence is unsafe or unavailable.");
        if (reject.Count > 0)
            return Decision(BotGDecisionStatus.Rejected, reject, "Bot G rejected the candidate because it did not clear value thresholds.");
        return new BotGDecision(
            BotGDecisionStatus.Approved,
            BotGDecisionReason.Approved,
            [BotGDecisionReason.Approved],
            "Bot G approved the highest-quality market-anchored GOALS candidate.");
    }

    private static BotGDecision Decision(
        BotGDecisionStatus status,
        IEnumerable<BotGDecisionReason> reasons,
        string explanation)
    {
        var distinct = reasons.Distinct().ToArray();
        return new BotGDecision(status, distinct[0], distinct, explanation);
    }

    private static BotGDecisionReason MetaUnavailableReason(string? reason)
    {
        if (reason?.Contains("schema", StringComparison.OrdinalIgnoreCase) == true)
            return BotGDecisionReason.ModelSchemaMismatch;
        if (reason?.Contains("timestamp", StringComparison.OrdinalIgnoreCase) == true
            || reason?.Contains("trained-through", StringComparison.OrdinalIgnoreCase) == true)
            return BotGDecisionReason.ModelTemporalLeakage;
        return BotGDecisionReason.ModelUnavailable;
    }
}

public sealed class BotGSelector : IBotGSelector
{
    public double Score(BotGCandidate candidate, BotGConfiguration configuration)
    {
        var config = BotGConfiguration.Validate(configuration);
        var weights = config.Ranking;
        var ev = NormalizePositive(candidate.ConservativeExpectedValue, 0.20d);
        var edge = NormalizePositive(candidate.ConservativeEdge, 0.15d);
        var reliability = Clamp01(candidate.CalibrationReliability);
        var quality = Clamp01(candidate.DataQualityScore);
        var inverseUncertainty = 1d - NormalizePositive(
            candidate.ProbabilityUncertainty,
            config.Uncertainty.MaximumUncertainty);
        var agreement = Clamp01(candidate.ContextAgreementScore);
        return Clamp01(
            weights.ConservativeExpectedValueWeight * ev
            + weights.ConservativeEdgeWeight * edge
            + weights.CalibrationReliabilityWeight * reliability
            + weights.DataQualityWeight * quality
            + weights.InverseUncertaintyWeight * inverseUncertainty
            + weights.ContextAgreementWeight * agreement);
    }

    public IReadOnlyList<BotGCandidate> SelectBestPerFixture(
        IEnumerable<BotGCandidate> candidates,
        BotGConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var config = BotGConfiguration.Validate(configuration);
        return candidates
            .Where(candidate => candidate.Decision == BotGDecisionStatus.Approved)
            .Select(candidate => candidate with { GSelectionScore = Score(candidate, config) })
            .GroupBy(candidate => candidate.FixtureId)
            .Select(group => group
                .OrderByDescending(candidate => candidate.GSelectionScore)
                .ThenByDescending(candidate => candidate.ConservativeExpectedValue)
                .ThenByDescending(candidate => candidate.ConservativeEdge)
                .ThenBy(candidate => candidate.Bookmaker, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.MarketType)
                .ThenBy(candidate => candidate.Selection)
                .ThenBy(candidate => candidate.Line)
                .ThenBy(candidate => candidate.CandidateUuid)
                .First())
            .OrderBy(candidate => candidate.FixtureDateUtc)
            .ThenBy(candidate => candidate.FixtureId)
            .ToArray();
    }

    private static double NormalizePositive(double value, double upper) =>
        !double.IsFinite(value) ? 0d : Math.Clamp(value / upper, 0d, 1d);

    private static double Clamp01(double value) =>
        !double.IsFinite(value) ? 0d : Math.Clamp(value, 0d, 1d);
}
