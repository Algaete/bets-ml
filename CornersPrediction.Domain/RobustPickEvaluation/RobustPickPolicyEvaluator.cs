namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface IRobustPickPolicyEvaluator
{
    RobustPickEvaluationResult Evaluate(
        RobustPickPolicyInput input,
        RobustPickPolicyOptions options);
}

public sealed class RobustPickPolicyEvaluator : IRobustPickPolicyEvaluator
{
    public RobustPickEvaluationResult Evaluate(
        RobustPickPolicyInput input,
        RobustPickPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        Validate(input, options);

        var reasons = new List<RobustReasonCode>();
        var warnings = new List<RobustReasonCode>();

        // Deliberately accumulate every failure in the documented policy order.
        AddIf(reasons, !input.DataIsValid || input.DataQualityScore < options.MinDataQuality,
            RobustReasonCode.DataQualityTooLow);
        AddIf(reasons, !input.TemporalDataIsValid, RobustReasonCode.LookaheadDataDetected);
        AddIf(reasons, !input.ModelWasTrainedBeforeFixture, RobustReasonCode.ModelTrainedAfterFixture);
        AddIf(reasons, !input.MarketPriceAvailable, RobustReasonCode.MarketPriceUnavailable);
        AddIf(reasons, !input.OddsAreFresh, RobustReasonCode.OddsTooOld);
        AddIf(reasons, input.SnapshotExpired, RobustReasonCode.SnapshotExpired);
        AddIf(reasons, !input.ErrorScaleAvailable, RobustReasonCode.ErrorScaleUnavailable);
        AddIf(reasons, input.ResidualEffectiveN < options.MinResidualEffectiveN,
            RobustReasonCode.ResidualSampleTooSmall);
        AddIf(reasons, options.RequireSideAgreement && !input.SideAgreement,
            RobustReasonCode.SideDisagreement);
        AddIf(reasons, input.NormalizedWorstCaseDistance < options.MinNormalizedWorstCaseDistance,
            RobustReasonCode.WorstCaseDistanceTooSmall);
        AddIf(reasons, input.NormalizedConsensusRange > options.MaxNormalizedConsensusRange,
            RobustReasonCode.ConsensusRangeTooLarge);
        AddIf(reasons,
            input.NormalizedCoherenceGap.HasValue
                && input.NormalizedCoherenceGap > options.MaxNormalizedCoherenceGap,
            RobustReasonCode.CoherenceGapTooLarge);
        if (!input.NormalizedCoherenceGap.HasValue)
        {
            (options.RequireCoherence ? reasons : warnings)
                .Add(RobustReasonCode.EvidenceInsufficient);
        }
        AddIf(reasons, input.CalibrationReliability < options.MinCalibrationReliability,
            RobustReasonCode.CalibrationReliabilityTooLow);
        AddIf(reasons, input.PointEdge < options.MinPointEdge,
            RobustReasonCode.PointEdgeBelowMinimum);
        AddIf(reasons, input.PointExpectedValue < options.MinPointExpectedValue,
            RobustReasonCode.PointEvBelowMinimum);
        AddIf(reasons, !input.RobustEdge.HasValue || input.RobustEdge < options.MinRobustEdge,
            RobustReasonCode.RobustEdgeBelowMinimum);
        AddIf(reasons,
            !input.RobustExpectedValue.HasValue
                || input.RobustExpectedValue <= options.MinRobustExpectedValue,
            RobustReasonCode.RobustEvNotPositive);
        AddIf(reasons, input.PositiveEvStability < options.MinPositiveEvStability,
            RobustReasonCode.PositiveEvStabilityTooLow);
        AddIf(reasons, input.ScenarioSideStability < options.MinScenarioSideStability,
            RobustReasonCode.LineupScenarioUnstable);
        AddIf(reasons, !input.ExposureAvailable, RobustReasonCode.ExposureLimitExceeded);
        AddIf(reasons, !input.CorrelatedExposureAvailable,
            RobustReasonCode.CorrelatedExposureLimitExceeded);
        AddIf(reasons, input.OriginalStake > 0m && input.RiskAdjustedStake <= 0m,
            RobustReasonCode.RobustnessScoreTooLow);

        AddEvidenceReasons(input, options, reasons, warnings);
        AddIf(warnings, input.NoVigStatus == NoVigStatus.Unavailable,
            RobustReasonCode.NoVigUnavailable);
        if (options.RequireNoVig && input.NoVigStatus == NoVigStatus.Unavailable)
        {
            reasons.Add(RobustReasonCode.NoVigUnavailable);
        }
        AddIf(warnings, !input.MarketAutomationNameMatches,
            RobustReasonCode.MarketAutomationNameMismatch);

        reasons = reasons.Distinct().ToList();
        warnings = warnings.Distinct().Where(item => !reasons.Contains(item)).ToList();
        var robustDecision = reasons.Count > 0
            ? RobustDecision.Reject
            : options.ManualReviewOnScenarioConflict && input.ScenarioConflictRequiresReview
                ? RobustDecision.ManualReview
                : input.RiskAdjustedStake < input.OriginalStake
                    ? RobustDecision.ReduceStake
                    : RobustDecision.Approve;
        var robustStake = robustDecision switch
        {
            RobustDecision.Approve or RobustDecision.ReduceStake =>
                Math.Min(input.OriginalStake, input.RiskAdjustedStake),
            _ => 0m
        };

        var currentEquivalent = input.CurrentDecision == CurrentSystemDecision.Bet
            ? RobustDecision.Approve
            : RobustDecision.Reject;
        var enforceDecision = input.CurrentDecision == CurrentSystemDecision.NoBet
            ? RobustDecision.Reject
            : robustDecision;
        var effectiveDecision = input.Mode == EvaluationMode.Enforce
            ? enforceDecision
            : currentEquivalent;
        var effectiveStake = input.Mode == EvaluationMode.Enforce
            ? (effectiveDecision is RobustDecision.Approve or RobustDecision.ReduceStake ? robustStake : 0m)
            : (input.CurrentDecision == CurrentSystemDecision.Bet ? input.OriginalStake : 0m);
        var humanReason = reasons.Count == 0
            ? robustDecision == RobustDecision.ManualReview
                ? "Manual review is required by the robust policy."
                : "All robust policy controls passed."
            : string.Join(", ", reasons.Select(reason => reason.ToStableCode()));

        return new RobustPickEvaluationResult(
            input.Mode,
            input.CurrentDecision,
            robustDecision,
            effectiveDecision,
            input.OriginalStake,
            robustStake,
            effectiveStake,
            input.OriginalStake > 0m ? robustStake / input.OriginalStake : 0m,
            Math.Clamp(input.RobustnessScore, 0m, 1m),
            reasons,
            warnings,
            humanReason);
    }

    private static void AddEvidenceReasons(
        RobustPickPolicyInput input,
        RobustPickPolicyOptions options,
        ICollection<RobustReasonCode> reasons,
        ICollection<RobustReasonCode> warnings)
    {
        if (input.IntelligenceEvidenceStatus == EvidenceStatus.SourceUnavailable)
        {
            (options.RequireIntelligence ? reasons : warnings)
                .Add(RobustReasonCode.IntelligenceSourceUnavailable);
        }
        else if (input.IntelligenceEvidenceStatus == EvidenceStatus.InsufficientEvidence)
        {
            (options.RequireIntelligence ? reasons : warnings)
                .Add(RobustReasonCode.EvidenceInsufficient);
        }
        else if (input.IntelligenceEvidenceStatus == EvidenceStatus.SnapshotExpired)
        {
            (options.RequireIntelligence ? reasons : warnings)
                .Add(RobustReasonCode.SnapshotExpired);
        }
    }

    private static void AddIf(
        ICollection<RobustReasonCode> target,
        bool condition,
        RobustReasonCode reason)
    {
        if (condition)
        {
            target.Add(reason);
        }
    }

    private static void Validate(RobustPickPolicyInput input, RobustPickPolicyOptions options)
    {
        if (input.OriginalStake < 0m
            || input.RiskAdjustedStake < 0m
            || input.RiskAdjustedStake > input.OriginalStake
            || options.MinResidualEffectiveN < 0m
            || options.MinCalibrationReliability is < 0m or > 1m
            || options.MinPositiveEvStability is < 0m or > 1m
            || options.MinScenarioSideStability is < 0m or > 1m
            || options.MinDataQuality is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Invalid policy input or options.");
        }
    }
}
