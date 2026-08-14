using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CornersPrediction.Application.AutomatedCorners;

namespace CornersPrediction.Application.Automation.BotE;

/// <summary>
/// A labelled decision produced by the source bot.  SourceProbability is the
/// probability that the source bot actually evaluated, not the raw ML output.
/// </summary>
public sealed record BotECalibrationObservation(
    long EvaluationId,
    long FixtureId,
    DateTime MatchDateUtc,
    string MarketType,
    string SelectedSide,
    decimal Line,
    decimal Odds,
    int ActualValue,
    double SourceProbability,
    double MarketNoVigProbability,
    double DataQualityScore,
    string BaseModelVersion);

public sealed record BotEEmpiricalCalibrationConfiguration
{
    public bool Enabled { get; init; }
    public string Version { get; init; } = "bot-e-empirical-calibration-1.0.2";
    public string SourceBotKey { get; init; } = "C2026";
    public int MinimumObservations { get; init; } = 20;
    public int MinimumExactMarketObservations { get; init; } = 12;
    public int MinimumEffectiveObservations { get; init; } = 8;
    public int TargetEffectiveObservations { get; init; } = 80;
    public int OutcomeAvailabilityLagHours { get; init; } = 8;
    public double ProbabilityBandwidth { get; init; } = 0.10d;
    public double GlobalPriorStrength { get; init; } = 40d;
    public double FamilyPriorStrength { get; init; } = 80d;
    public double ExactMarketPriorStrength { get; init; } = 40d;
    public double RecencyHalfLifeDays { get; init; } = 45d;
    public double QualityWeightFloor { get; init; } = 0.50d;
    public double MinimumReliability { get; init; } = 0.15d;
    public double ConfidenceZScore { get; init; } = 0.50d;
    public bool RequireSameBaseModelVersion { get; init; }
    public bool RequireNoVigProbability { get; init; } = true;
}

public sealed record BotEEmpiricalCalibrationResult(
    bool IsAvailable,
    int InputRows,
    int TemporallyAcceptedRows,
    int ExactMarketRows,
    int ExactMarketFixtures,
    int FamilyRows,
    int FamilyFixtures,
    int GlobalRows,
    int GlobalFixtures,
    int SelectedFixtures,
    string EvidenceTier,
    double EffectiveSampleSize,
    double WeightedAsianReturn,
    double MarketAnchorProbability,
    double MarketAnchorExpectedValue,
    double PosteriorExpectedValue,
    double StandardError,
    double ConservativeExpectedValue,
    double ConservativeEquivalentProbability,
    double Reliability,
    double SourceBrierScore,
    double MarketBrierScore,
    string EvidenceHash,
    IReadOnlyDictionary<string, int> OutcomeCounts,
    IReadOnlyList<string> RiskFlags)
{
    public static BotEEmpiricalCalibrationResult Unavailable(
        int input,
        int temporal,
        int exactRows,
        int exactFixtures,
        int familyRows,
        int familyFixtures,
        int globalRows,
        int globalFixtures,
        string reason) =>
        new(false, input, temporal, exactRows, exactFixtures, familyRows, familyFixtures,
            globalRows, globalFixtures, 0, "Unavailable", 0d, 0d, 0d, 0d, 0d,
            0d, 0d, 0d, 0d, 0d, 0d, string.Empty,
            new Dictionary<string, int>(StringComparer.Ordinal), [reason]);
}

/// <summary>
/// Walk-forward empirical calibration.  Every outcome must be old enough to
/// have been available before the candidate, and a fixture contributes at most
/// one independent observation to each hierarchy level.
/// </summary>
public static class BotEEmpiricalCalibrationCalculator
{
    public static BotEEmpiricalCalibrationResult Calculate(
        DateTime asOfDateUtc,
        string marketType,
        string selectedSide,
        decimal selectedOdds,
        string baseModelVersion,
        double sourceProbability,
        double? marketNoVigProbability,
        IReadOnlyList<BotECalibrationObservation>? observations,
        BotEEmpiricalCalibrationConfiguration configuration)
    {
        Validate(configuration);
        var input = observations ?? [];
        if (!configuration.Enabled)
        {
            return Unavailable(input.Count, "EmpiricalCalibrationDisabled");
        }

        if (selectedOdds <= 1m
            || !double.IsFinite(sourceProbability)
            || sourceProbability is <= 0d or >= 1d
            || configuration.RequireNoVigProbability
            && (!marketNoVigProbability.HasValue
                || !double.IsFinite(marketNoVigProbability.Value)
                || marketNoVigProbability.Value is <= 0d or >= 1d))
        {
            return Unavailable(input.Count, "InvalidCalibrationProbabilityInput");
        }

        var asOfUtc = EnsureUtc(asOfDateUtc);
        var normalizedMarket = marketType.Trim();
        var normalizedSide = NormalizeSide(selectedSide);
        var family = MarketFamily(normalizedMarket);
        var marketAnchor = Math.Clamp(marketNoVigProbability ?? sourceProbability, 0.001d, 0.999d);
        var candidateOdds = Convert.ToDouble(selectedOdds);
        var anchorReturn = marketAnchor * candidateOdds - 1d;
        var anchorVariance = ReturnVariance(marketAnchor, candidateOdds);

        var temporal = input
            .Where(value => EnsureUtc(value.MatchDateUtc)
                .AddHours(configuration.OutcomeAvailabilityLagHours) < asOfUtc)
            .Where(IsValid)
            .Where(value => !configuration.RequireSameBaseModelVersion
                || value.BaseModelVersion.Equals(baseModelVersion, StringComparison.Ordinal))
            .GroupBy(value => value.EvaluationId)
            .Select(group => group
                .OrderBy(CanonicalObservation, StringComparer.Ordinal)
                .First())
            .OrderBy(value => value.EvaluationId)
            .ToArray();

        var globalRows = temporal
            .Where(value => NormalizeSide(value.SelectedSide) == normalizedSide)
            .ToArray();
        var familyRows = globalRows
            .Where(value => MarketFamily(value.MarketType) == family)
            .ToArray();
        var exactRows = familyRows
            .Where(value => value.MarketType.Equals(normalizedMarket, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var global = DeduplicateFixtures(globalRows, normalizedMarket, sourceProbability);
        var familyEvidence = DeduplicateFixtures(familyRows, normalizedMarket, sourceProbability);
        var exact = DeduplicateFixtures(exactRows, normalizedMarket, sourceProbability);
        if (global.Length < configuration.MinimumObservations)
        {
            return BotEEmpiricalCalibrationResult.Unavailable(
                input.Count, temporal.Length, exactRows.Length, exact.Length,
                familyRows.Length, familyEvidence.Length, globalRows.Length, global.Length,
                "InsufficientCalibrationHistory");
        }

        var globalEstimate = Estimate(
            global, asOfUtc, sourceProbability, selectedOdds, anchorReturn,
            anchorVariance, configuration.GlobalPriorStrength, configuration);
        if (!globalEstimate.IsAvailable
            || globalEstimate.EffectiveSampleSize < configuration.MinimumEffectiveObservations)
        {
            return BotEEmpiricalCalibrationResult.Unavailable(
                input.Count, temporal.Length, exactRows.Length, exact.Length,
                familyRows.Length, familyEvidence.Length, globalRows.Length, global.Length,
                "InsufficientEffectiveCalibrationHistory");
        }

        var selectedEstimate = globalEstimate;
        var evidenceTier = "GlobalSide";
        var tierRisks = new List<string>();

        if (familyEvidence.Length >= configuration.MinimumObservations)
        {
            var familyEstimate = Estimate(
                familyEvidence, asOfUtc, sourceProbability, selectedOdds,
                globalEstimate.PosteriorReturn, globalEstimate.PosteriorVariance,
                configuration.FamilyPriorStrength, configuration);
            if (familyEstimate.IsAvailable
                && familyEstimate.EffectiveSampleSize >= configuration.MinimumEffectiveObservations)
            {
                selectedEstimate = familyEstimate;
                evidenceTier = "MarketFamilyAndSide";
            }
            else
            {
                tierRisks.Add("FamilyCalibrationEffectiveSampleCollapsed");
            }
        }

        if (exact.Length >= configuration.MinimumExactMarketObservations)
        {
            var exactEstimate = Estimate(
                exact, asOfUtc, sourceProbability, selectedOdds,
                selectedEstimate.PosteriorReturn, selectedEstimate.PosteriorVariance,
                configuration.ExactMarketPriorStrength, configuration);
            if (exactEstimate.IsAvailable
                && exactEstimate.EffectiveSampleSize >= configuration.MinimumEffectiveObservations)
            {
                selectedEstimate = exactEstimate;
                evidenceTier = "ExactMarketAndSide";
            }
            else
            {
                tierRisks.Add("ExactMarketCalibrationEffectiveSampleCollapsed");
            }
        }

        if (!selectedEstimate.IsAvailable)
        {
            return BotEEmpiricalCalibrationResult.Unavailable(
                input.Count, temporal.Length, exactRows.Length, exact.Length,
                familyRows.Length, familyEvidence.Length, globalRows.Length, global.Length,
                "CalibrationWeightsCollapsed");
        }

        var selectedEvidence = evidenceTier switch
        {
            "ExactMarketAndSide" => exact,
            "MarketFamilyAndSide" => familyEvidence,
            _ => global
        };
        var effectiveSample = selectedEstimate.EffectiveSampleSize;
        var averageQuality = selectedEstimate.AverageQuality;
        var reliability = Math.Clamp(
            effectiveSample / (effectiveSample + configuration.TargetEffectiveObservations)
            * averageQuality,
            0d,
            1d);
        var standardError = selectedEstimate.StandardError;
        var conservativeReturn = Math.Clamp(
            selectedEstimate.PosteriorReturn - configuration.ConfidenceZScore * standardError,
            -1d,
            candidateOdds - 1d);
        var equivalentProbability = Math.Clamp(
            (conservativeReturn + 1d) / candidateOdds,
            0.001d,
            0.999d);
        var risks = new List<string>(tierRisks);
        if (evidenceTier != "ExactMarketAndSide") risks.Add("CalibrationUsedBroaderEvidenceTier");
        if (effectiveSample < configuration.TargetEffectiveObservations) risks.Add("LowEffectiveCalibrationSample");
        if (reliability < configuration.MinimumReliability) risks.Add("LowCalibrationReliability");

        var allWeighted = selectedEstimate.Weighted;
        var weightSum = allWeighted.Sum(value => value.Weight);
        var sourceBrier = allWeighted.Sum(value => value.Weight
            * Math.Pow(value.Observation.SourceProbability - value.EquivalentOutcomeProbability, 2d)) / weightSum;
        var marketBrier = allWeighted.Sum(value => value.Weight
            * Math.Pow(value.Observation.MarketNoVigProbability - value.EquivalentOutcomeProbability, 2d)) / weightSum;
        var outcomeCounts = allWeighted
            .GroupBy(value => OutcomeLabel(value.OutcomeFactor), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var evidenceHash = CreateEvidenceHash(selectedEvidence);

        return new BotEEmpiricalCalibrationResult(
            true,
            input.Count,
            temporal.Length,
            exactRows.Length,
            exact.Length,
            familyRows.Length,
            familyEvidence.Length,
            globalRows.Length,
            global.Length,
            selectedEvidence.Length,
            evidenceTier,
            effectiveSample,
            selectedEstimate.WeightedReturn,
            marketAnchor,
            anchorReturn,
            selectedEstimate.PosteriorReturn,
            standardError,
            conservativeReturn,
            equivalentProbability,
            reliability,
            sourceBrier,
            marketBrier,
            evidenceHash,
            outcomeCounts,
            risks);
    }

    public static BotEEmpiricalCalibrationConfiguration Validate(
        BotEEmpiricalCalibrationConfiguration value)
    {
        if (string.IsNullOrWhiteSpace(value.Version) || string.IsNullOrWhiteSpace(value.SourceBotKey))
            throw new ArgumentException("Bot E calibration version and source bot are required.");
        if (value.MinimumObservations is < 1 or > 10000
            || value.MinimumExactMarketObservations is < 1 or > 10000
            || value.MinimumEffectiveObservations is < 1 or > 10000
            || value.TargetEffectiveObservations is < 1 or > 10000
            || value.OutcomeAvailabilityLagHours is < 0 or > 168)
            throw new ArgumentException("Bot E calibration sample thresholds are invalid.");
        RequireRange(value.ProbabilityBandwidth, 0.01d, 0.50d, nameof(value.ProbabilityBandwidth));
        RequireRange(value.GlobalPriorStrength, 1d, 1000d, nameof(value.GlobalPriorStrength));
        RequireRange(value.FamilyPriorStrength, 1d, 1000d, nameof(value.FamilyPriorStrength));
        RequireRange(value.ExactMarketPriorStrength, 1d, 1000d, nameof(value.ExactMarketPriorStrength));
        RequireRange(value.RecencyHalfLifeDays, 1d, 3650d, nameof(value.RecencyHalfLifeDays));
        RequireRange(value.QualityWeightFloor, 0d, 1d, nameof(value.QualityWeightFloor));
        RequireRange(value.MinimumReliability, 0d, 1d, nameof(value.MinimumReliability));
        RequireRange(value.ConfidenceZScore, 0d, 3d, nameof(value.ConfidenceZScore));
        return value;
    }

    private static EstimateResult Estimate(
        IReadOnlyList<BotECalibrationObservation> evidence,
        DateTime asOfUtc,
        double sourceProbability,
        decimal candidateOdds,
        double priorReturn,
        double priorVariance,
        double priorStrength,
        BotEEmpiricalCalibrationConfiguration configuration)
    {
        var weighted = evidence.Select(value =>
        {
            var candidateOutcome = AutomatedBotPickSettlementCalculator.Calculate(
                value.SelectedSide, value.Line, value.ActualValue, candidateOdds, 1m);
            var historicalOutcome = AutomatedBotPickSettlementCalculator.Calculate(
                value.SelectedSide, value.Line, value.ActualValue, value.Odds, 1m);
            var unitReturn = Convert.ToDouble(candidateOutcome.YieldPct ?? 0m);
            var equivalentOutcome = (Convert.ToDouble(historicalOutcome.YieldPct ?? 0m) + 1d)
                / Convert.ToDouble(value.Odds);
            var distance = (value.SourceProbability - sourceProbability) / configuration.ProbabilityBandwidth;
            var similarity = Math.Exp(-0.5d * distance * distance);
            var ageDays = Math.Max(0d, (asOfUtc - EnsureUtc(value.MatchDateUtc)).TotalDays);
            var recency = Math.Pow(0.5d, ageDays / configuration.RecencyHalfLifeDays);
            var quality = configuration.QualityWeightFloor
                + (1d - configuration.QualityWeightFloor) * value.DataQualityScore;
            return new WeightedObservation(
                value, Convert.ToDouble(candidateOutcome.Factor), unitReturn, equivalentOutcome,
                similarity * recency * quality);
        }).Where(value => value.Weight > 1e-9d).ToArray();

        if (weighted.Length == 0)
        {
            return EstimateResult.Unavailable(priorStrength);
        }

        var weightSum = weighted.Sum(value => value.Weight);
        var squaredWeightSum = weighted.Sum(value => value.Weight * value.Weight);
        var effectiveSample = squaredWeightSum <= 0d ? 0d : weightSum * weightSum / squaredWeightSum;
        var weightedReturn = weighted.Sum(value => value.Weight * value.UnitReturn) / weightSum;
        var posteriorReturn = (weightSum * weightedReturn + priorStrength * priorReturn)
            / (weightSum + priorStrength);
        var posteriorVariance = (
                weighted.Sum(value => value.Weight
                    * Math.Pow(value.UnitReturn - posteriorReturn, 2d))
                + priorStrength * (Math.Max(0d, priorVariance)
                    + Math.Pow(priorReturn - posteriorReturn, 2d)))
            / (weightSum + priorStrength);
        var standardError = Math.Sqrt(Math.Max(1e-9d, posteriorVariance)
            / Math.Max(1d, effectiveSample + priorStrength));
        var averageQuality = weighted.Sum(value => value.Weight * value.Observation.DataQualityScore) / weightSum;
        return new EstimateResult(
            true, weighted, weightedReturn, posteriorReturn, effectiveSample,
            posteriorVariance, standardError, averageQuality, priorStrength);
    }

    private static BotECalibrationObservation[] DeduplicateFixtures(
        IReadOnlyList<BotECalibrationObservation> rows,
        string candidateMarket,
        double candidateProbability) =>
        rows.GroupBy(value => value.FixtureId)
            .Select(group => group
                .OrderBy(value => Math.Abs(value.SourceProbability - candidateProbability))
                .ThenBy(value => value.MarketType.Equals(candidateMarket, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(value => value.EvaluationId)
                .First())
            .OrderBy(value => value.FixtureId)
            .ToArray();

    private static bool IsValid(BotECalibrationObservation value) =>
        value.FixtureId > 0
        && value.ActualValue >= 0
        && value.Line >= 0m
        && value.Odds > 1m
        && double.IsFinite(value.SourceProbability)
        && value.SourceProbability is > 0d and < 1d
        && double.IsFinite(value.MarketNoVigProbability)
        && value.MarketNoVigProbability is > 0d and < 1d
        && double.IsFinite(value.DataQualityScore)
        && value.DataQualityScore is >= 0d and <= 1d;

    private static BotEEmpiricalCalibrationResult Unavailable(int input, string reason) =>
        BotEEmpiricalCalibrationResult.Unavailable(input, 0, 0, 0, 0, 0, 0, 0, reason);

    private static string MarketFamily(string marketType)
    {
        var value = marketType.ToUpperInvariant();
        if (value.Contains("SHOTSONGOAL", StringComparison.Ordinal)) return "SOG";
        if (value.Contains("SHOTS", StringComparison.Ordinal)) return "SHOTS";
        if (value.Contains("GOALS", StringComparison.Ordinal)) return "GOALS";
        if (value.Contains("CORNERS", StringComparison.Ordinal)) return "CORNERS";
        return "OTHER";
    }

    private static string NormalizeSide(string value) => value.Trim().Equals(
        "Over", StringComparison.OrdinalIgnoreCase) ? "OVER" : value.Trim().Equals(
        "Under", StringComparison.OrdinalIgnoreCase) ? "UNDER" : throw new ArgumentException(
        "Selected side must be Over or Under.");

    private static string OutcomeLabel(double factor) => factor switch
    {
        >= 0.999d => "Win",
        >= 0.499d => "HalfWin",
        <= -0.999d => "Loss",
        <= -0.499d => "HalfLoss",
        _ => "Push"
    };

    private static double ReturnVariance(double probability, double odds)
    {
        var boundedProbability = Math.Clamp(probability, 0.001d, 0.999d);
        return odds * odds * boundedProbability * (1d - boundedProbability);
    }

    private static string CreateEvidenceHash(IEnumerable<BotECalibrationObservation> observations)
    {
        var value = string.Join("\n", observations
            .OrderBy(observation => observation.EvaluationId)
            .ThenBy(observation => observation.FixtureId)
            .Select(CanonicalObservation));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string CanonicalObservation(BotECalibrationObservation value) => string.Join(
        "|",
        value.EvaluationId.ToString(CultureInfo.InvariantCulture),
        value.FixtureId.ToString(CultureInfo.InvariantCulture),
        EnsureUtc(value.MatchDateUtc).ToString("O", CultureInfo.InvariantCulture),
        value.MarketType.Trim().ToUpperInvariant(),
        value.SelectedSide.Trim().ToUpperInvariant(),
        value.Line.ToString("G29", CultureInfo.InvariantCulture),
        value.Odds.ToString("G29", CultureInfo.InvariantCulture),
        value.ActualValue.ToString(CultureInfo.InvariantCulture),
        value.SourceProbability.ToString("R", CultureInfo.InvariantCulture),
        value.MarketNoVigProbability.ToString("R", CultureInfo.InvariantCulture),
        value.DataQualityScore.ToString("R", CultureInfo.InvariantCulture),
        value.BaseModelVersion.Trim());

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
    }

    private sealed record WeightedObservation(
        BotECalibrationObservation Observation,
        double OutcomeFactor,
        double UnitReturn,
        double EquivalentOutcomeProbability,
        double Weight);

    private sealed record EstimateResult(
        bool IsAvailable,
        IReadOnlyList<WeightedObservation> Weighted,
        double WeightedReturn,
        double PosteriorReturn,
        double EffectiveSampleSize,
        double PosteriorVariance,
        double StandardError,
        double AverageQuality,
        double PriorStrength)
    {
        public static EstimateResult Unavailable(double priorStrength) =>
            new(false, [], 0d, 0d, 0d, 0d, 0d, 0d, priorStrength);
    }
}
