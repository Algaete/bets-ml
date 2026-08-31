namespace CornersPrediction.Application.RobustPickEvaluation;

/// <summary>
/// Runtime controls for the robust evaluation layer.  Shadow is deliberately the
/// default: changing the feature flag alone must never alter a published pick.
/// </summary>
public sealed class RobustPickEvaluationOptions
{
    public const string SectionName = "RobustPickEvaluation";

    public bool Enabled { get; init; } = true;
    public string Mode { get; init; } = "Shadow";
    public string Version { get; init; } = "robust-pick-evaluation-1.0.0";
    public int SimulationCount { get; init; } = 5_000;
    public int OuterScenarioCount { get; init; } = 500;
    public decimal ProbabilityLowerQuantile { get; init; } = 0.10m;
    public decimal ProbabilityUpperQuantile { get; init; } = 0.90m;
    public int OutcomeAvailabilityLagHours { get; init; } = 8;
    public int EvaluationTimeoutSeconds { get; init; } = 30;
    public int MinimumReevaluationIntervalSeconds { get; init; } = 60;
    public bool ReevaluateOnOddsMovement { get; init; } = true;
    public decimal SignificantOddsMovement { get; init; } = 0.02m;
    public decimal SignificantLineMovement { get; init; } = 0.25m;
    public int MaxLineupOddsAgeSeconds { get; init; } = 300;
    public int DefaultMaxOddsAgeSeconds { get; init; } = 1_800;
    public IReadOnlyDictionary<string, int> MaxOddsAgeSecondsBySource { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pinnacle"] = 900,
            ["Betano"] = 1_800
        };
    public RobustResidualOptions Residuals { get; init; } = new();
    public RobustPolicyOptions Policy { get; init; } = new();
    public RobustStakeOptions Stake { get; init; } = new();
    public RobustExposureOptions Exposure { get; init; } = new();
}

public sealed class RobustResidualOptions
{
    public decimal MinimumEffectiveN { get; init; } = 30m;
    public decimal TargetEffectiveN { get; init; } = 150m;
    public decimal RecencyHalfLifeDays { get; init; } = 90m;
    public bool UseLineSimilarity { get; init; } = true;
    public bool UseOddsSimilarity { get; init; } = true;
    public bool AllowSelectedPicksOnly { get; init; } = true;
    public decimal ErrorScaleEpsilon { get; init; } = 0.000001m;
}

public sealed class RobustPolicyOptions
{
    public decimal MinRobustEdge { get; init; } = 0.005m;
    public decimal MinRobustExpectedValue { get; init; } = 0m;
    public decimal MinPositiveEvStability { get; init; } = 0.75m;
    public decimal MinScenarioSideStability { get; init; } = 0.75m;
    public decimal MinNormalizedWorstCaseDistance { get; init; } = 0.25m;
    public decimal MaxNormalizedConsensusRange { get; init; } = 0.75m;
    public decimal MaxNormalizedCoherenceGap { get; init; } = 0.75m;
    public decimal MinCalibrationReliability { get; init; } = 0.50m;
    public bool RequireSideAgreement { get; init; } = true;
}

public sealed class RobustStakeOptions
{
    public bool AllowIncrease { get; init; } = false;
    public decimal HighRobustnessThreshold { get; init; } = 0.90m;
    public decimal MediumRobustnessThreshold { get; init; } = 0.80m;
    public decimal MinimumRobustnessThreshold { get; init; } = 0.75m;
}

public sealed class RobustExposureOptions
{
    public bool Enabled { get; init; } = true;
    public decimal MaximumStakePerFixture { get; init; } = 1.5m;
    public decimal MaximumStakePerTeam { get; init; } = 1.0m;
    public decimal MaximumStakePerCorrelationCluster { get; init; } = 0.75m;
    public int MaximumRelatedPicksPerFixture { get; init; } = 3;
}
