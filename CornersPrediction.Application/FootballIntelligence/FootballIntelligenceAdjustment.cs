using CornersPrediction.Domain.FootballIntelligence;

namespace CornersPrediction.Application.FootballIntelligence;

public sealed record FootballIntelligenceAdjustmentConfiguration
{
    public bool Enabled { get; init; }
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

public sealed record FootballIntelligenceAdjustmentResult(
    bool IsApplied,
    double ProbabilityBefore,
    double ProbabilityAdjustment,
    double ProbabilityAfter,
    double HomeSignal,
    double AwaySignal,
    long? HomeSnapshotId,
    long? AwaySnapshotId,
    IntelligenceEvidenceStatus HomeEvidenceStatus,
    IntelligenceEvidenceStatus AwayEvidenceStatus,
    PreMatchDecision Recommendation,
    IReadOnlyList<string> Reasons)
{
    public static FootballIntelligenceAdjustmentResult Neutral(
        double probability,
        IntelligenceEvidenceStatus homeStatus,
        IntelligenceEvidenceStatus awayStatus,
        params string[] reasons) =>
        new(false, probability, 0d, probability, 0d, 0d, null, null,
            homeStatus, awayStatus, PreMatchDecision.Keep, reasons);
}

public static class FootballIntelligenceAdjustmentCalculator
{
    public static FootballIntelligenceAdjustmentResult Calculate(
        DateTime asOfDateUtc,
        string marketType,
        string selectedSide,
        double probabilityBefore,
        MatchIntelligenceSnapshotPair? snapshots,
        FootballIntelligenceAdjustmentConfiguration configuration)
    {
        Validate(configuration);
        var probability = Clamp01(probabilityBefore);
        if (!configuration.Enabled)
        {
            return FootballIntelligenceAdjustmentResult.Neutral(
                probability,
                IntelligenceEvidenceStatus.Missing,
                IntelligenceEvidenceStatus.Missing,
                "FootballIntelligenceDisabled");
        }

        if (snapshots is null)
        {
            return FootballIntelligenceAdjustmentResult.Neutral(
                probability,
                IntelligenceEvidenceStatus.Missing,
                IntelligenceEvidenceStatus.Missing,
                "NoIntelligenceSnapshot");
        }

        var asOfUtc = EnsureUtc(asOfDateUtc);
        var homeStatus = EvidenceStatus(snapshots.Home, asOfUtc, configuration);
        var awayStatus = EvidenceStatus(snapshots.Away, asOfUtc, configuration);
        if (homeStatus != IntelligenceEvidenceStatus.Available
            && awayStatus != IntelligenceEvidenceStatus.Available)
        {
            return FootballIntelligenceAdjustmentResult.Neutral(
                probability,
                homeStatus,
                awayStatus,
                "NoUsableIntelligenceEvidence");
        }

        var homeComponents = homeStatus == IntelligenceEvidenceStatus.Available
            ? MarketSignal(snapshots.Home!, marketType, configuration)
            : TeamMarketSignal.Neutral;
        var awayComponents = awayStatus == IntelligenceEvidenceStatus.Available
            ? MarketSignal(snapshots.Away!, marketType, configuration)
            : TeamMarketSignal.Neutral;
        var overSignal = CombineForMarket(marketType, homeComponents, awayComponents);
        var homeSignal = homeComponents.Offensive + homeComponents.DefensiveVulnerability;
        var awaySignal = awayComponents.Offensive + awayComponents.DefensiveVulnerability;
        var selectedSignal = selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase)
            ? -overSignal
            : overSignal;
        var rawAdjustment = selectedSignal * configuration.Weight;
        var boundedAdjustment = Math.Clamp(
            rawAdjustment,
            -configuration.MaximumProbabilityAdjustment,
            configuration.MaximumProbabilityAdjustment);
        var probabilityAfter = Clamp01(probability + boundedAdjustment);
        var adjustment = probabilityAfter - probability;

        if (Math.Abs(adjustment) < 1e-12)
        {
            return new FootballIntelligenceAdjustmentResult(
                false,
                probability,
                0d,
                probability,
                homeSignal,
                awaySignal,
                snapshots.Home?.Id,
                snapshots.Away?.Id,
                homeStatus,
                awayStatus,
                PreMatchDecision.Keep,
                ["UsableEvidenceWithoutMeasuredMarketImpact"]);
        }

        var reasons = new List<string> { "FootballIntelligenceApplied" };
        var conflictCount = (snapshots.Home?.ConflictCount ?? 0) + (snapshots.Away?.ConflictCount ?? 0);
        var recommendation = conflictCount > 0
            ? PreMatchDecision.ReduceConfidence
            : PreMatchDecision.Recalculate;
        if (conflictCount > 0)
        {
            reasons.Add("ConflictingIntelligenceSources");
        }

        return new FootballIntelligenceAdjustmentResult(
            true,
            probability,
            adjustment,
            probabilityAfter,
            homeSignal,
            awaySignal,
            snapshots.Home?.Id,
            snapshots.Away?.Id,
            homeStatus,
            awayStatus,
            recommendation,
            reasons);
    }

    public static void Validate(FootballIntelligenceAdjustmentConfiguration value)
    {
        if (string.IsNullOrWhiteSpace(value.Version))
            throw new ArgumentException("Football intelligence adjustment version is required.");
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
            throw new ArgumentException("Football intelligence evidence thresholds must be positive.");
        }
        var weights = value.AttackWeight + value.DefenceWeight + value.WidthWeight + value.SetPieceWeight;
        if (Math.Abs(weights - 1d) > 0.0001d)
            throw new ArgumentException("Football intelligence market weights must add up to 1.0.");
    }

    private static IntelligenceEvidenceStatus EvidenceStatus(
        MatchTeamIntelligenceSnapshot? snapshot,
        DateTime asOfUtc,
        FootballIntelligenceAdjustmentConfiguration configuration)
    {
        if (snapshot is null)
            return IntelligenceEvidenceStatus.Missing;
        var cutoff = EnsureUtc(snapshot.CutoffAtUtc);
        if (cutoff > asOfUtc)
            return IntelligenceEvidenceStatus.FutureCutoff;
        if (snapshot.SnapshotAgeMinutes > configuration.MaximumSnapshotAgeMinutes)
            return IntelligenceEvidenceStatus.Stale;
        if (snapshot.ActionableFactCount < configuration.MinimumActionableFacts
            || snapshot.IndependentSourceCount < configuration.MinimumIndependentSources)
            return IntelligenceEvidenceStatus.NoActionableFacts;
        if (Convert.ToDouble(snapshot.OverallNewsConfidence) < configuration.MinimumTeamConfidence)
            return IntelligenceEvidenceStatus.LowConfidence;
        return IntelligenceEvidenceStatus.Available;
    }

    private static TeamMarketSignal MarketSignal(
        MatchTeamIntelligenceSnapshot snapshot,
        string marketType,
        FootballIntelligenceAdjustmentConfiguration configuration)
    {
        var confidence = Clamp01(Convert.ToDouble(snapshot.OverallNewsConfidence));
        var attack = Convert.ToDouble(snapshot.AttackAvailabilityImpact);
        var defence = Convert.ToDouble(snapshot.DefenceAvailabilityImpact)
            + Convert.ToDouble(snapshot.GoalkeeperAvailabilityImpact);
        var width = Convert.ToDouble(snapshot.WidthAvailabilityImpact)
            + Convert.ToDouble(snapshot.CornerCreationImpact);
        var setPiece = Convert.ToDouble(snapshot.SetPieceAvailabilityImpact);
        var normalizedMarket = marketType.Trim().ToUpperInvariant();
        var ownAttackPenalty = normalizedMarket.Contains("CORNER", StringComparison.Ordinal)
            ? configuration.AttackWeight * attack
                + configuration.WidthWeight * width
                + configuration.SetPieceWeight * setPiece
            : normalizedMarket.Contains("SHOTSONGOAL", StringComparison.Ordinal)
                ? configuration.AttackWeight * (attack + Convert.ToDouble(snapshot.FinishingAvailabilityImpact))
                    + configuration.SetPieceWeight * Convert.ToDouble(snapshot.MissingSotShare)
                : normalizedMarket.Contains("SHOT", StringComparison.Ordinal)
                    ? configuration.AttackWeight * (attack + Convert.ToDouble(snapshot.ShotGenerationImpact))
                        + configuration.SetPieceWeight * Convert.ToDouble(snapshot.MissingShotShare)
                    : configuration.AttackWeight * (attack + Convert.ToDouble(snapshot.GoalScoringAvailabilityImpact))
                        + configuration.SetPieceWeight * Convert.ToDouble(snapshot.MissingGoalShare);

        // Offensive is negative when the team is expected to generate less.
        // DefensiveVulnerability is positive and only benefits the opponent.
        return new TeamMarketSignal(
            confidence * Math.Clamp(-ownAttackPenalty, -1d, 0d),
            confidence * Math.Clamp(configuration.DefenceWeight * defence, 0d, 1d));
    }

    private static double CombineForMarket(
        string marketType,
        TeamMarketSignal home,
        TeamMarketSignal away)
    {
        var normalized = marketType.Trim().ToUpperInvariant();
        if (normalized.Contains("HOME", StringComparison.Ordinal))
            return home.Offensive + away.DefensiveVulnerability;
        if (normalized.Contains("AWAY", StringComparison.Ordinal))
            return away.Offensive + home.DefensiveVulnerability;
        return (home.Offensive + away.Offensive
            + home.DefensiveVulnerability + away.DefensiveVulnerability) / 2d;
    }

    private readonly record struct TeamMarketSignal(double Offensive, double DefensiveVulnerability)
    {
        public static TeamMarketSignal Neutral => new(0d, 0d);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
    }
}
