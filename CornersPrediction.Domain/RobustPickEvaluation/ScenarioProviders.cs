namespace CornersPrediction.Domain.RobustPickEvaluation;

public enum ScenarioType
{
    Base,
    Lineup,
    Intelligence,
    Fatigue,
    GameState,
    MarketMovement
}

public sealed record ScenarioEvidenceSnapshot(
    ScenarioType ScenarioType,
    string ScenarioName,
    EvidenceStatus EvidenceStatus,
    bool HasStructuredEvidence,
    bool IsAdjustmentValidated,
    decimal? ProbabilityWeight,
    decimal? PredictionAdjustment,
    decimal? ProbabilityAdjustment,
    decimal? Confidence,
    IReadOnlyList<string> EvidenceIds,
    DateTime AsOfUtc,
    DateTime? ExpiresUtc,
    string? AdjustmentVersion,
    int HistoricalEventObservationCount,
    string? Reason);

public sealed class ScenarioProviderRequest
{
    public required DateTime EvaluationAsOfUtc { get; init; }
    public required MarketFamily MarketFamily { get; init; }
    public required string MarketType { get; init; }
    public required decimal BasePrediction { get; init; }
    public required decimal BaseProbability { get; init; }
    public int MinimumGameStateEventObservations { get; init; } = 1;
    public IReadOnlyCollection<ScenarioType> ApplicableScenarioTypes { get; init; } =
        Enum.GetValues<ScenarioType>();
    public IReadOnlyDictionary<ScenarioType, ScenarioEvidenceSnapshot> Evidence { get; init; } =
        new Dictionary<ScenarioType, ScenarioEvidenceSnapshot>();
}

public sealed record ScenarioProviderResult(
    ScenarioType ScenarioType,
    string ScenarioName,
    decimal ProbabilityWeight,
    decimal PredictionAdjustment,
    decimal ProbabilityAdjustment,
    EvidenceStatus EvidenceStatus,
    decimal Confidence,
    IReadOnlyList<string> EvidenceIds,
    DateTime? AsOfUtc,
    DateTime? ExpiresUtc,
    bool IsUsable,
    string Reason);

public sealed record ScenarioDataReadinessResult(
    ScenarioType ScenarioType,
    bool IsReady,
    EvidenceStatus EvidenceStatus,
    string Reason,
    ScenarioEvidenceSnapshot? Evidence);

public interface IScenarioProvider
{
    ScenarioType ScenarioType { get; }

    ScenarioProviderResult Evaluate(ScenarioProviderRequest request);
}

public interface IScenarioDataReadinessEvaluator
{
    ScenarioDataReadinessResult Evaluate(
        ScenarioProviderRequest request,
        ScenarioType scenarioType);
}

public sealed class ScenarioDataReadinessEvaluator : IScenarioDataReadinessEvaluator
{
    public ScenarioDataReadinessResult Evaluate(
        ScenarioProviderRequest request,
        ScenarioType scenarioType)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (request.ApplicableScenarioTypes is null || request.Evidence is null)
        {
            throw new ArgumentException("Scenario collections cannot be null.", nameof(request));
        }
        if (!request.ApplicableScenarioTypes.Contains(scenarioType))
        {
            return NotReady(scenarioType, EvidenceStatus.NotApplicable, "SCENARIO_NOT_APPLICABLE");
        }
        if (!request.Evidence.TryGetValue(scenarioType, out var evidence))
        {
            return NotReady(scenarioType, EvidenceStatus.InsufficientEvidence, "SCENARIO_EVIDENCE_MISSING");
        }
        if (evidence.ScenarioType != scenarioType)
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "SCENARIO_EVIDENCE_TYPE_MISMATCH",
                evidence);
        }
        if (evidence.EvidenceStatus is EvidenceStatus.SourceUnavailable
            or EvidenceStatus.InsufficientEvidence
            or EvidenceStatus.NotApplicable
            or EvidenceStatus.SnapshotExpired)
        {
            return NotReady(
                scenarioType,
                evidence.EvidenceStatus,
                evidence.Reason ?? evidence.EvidenceStatus.ToString().ToUpperInvariant(),
                evidence);
        }
        if (evidence.AsOfUtc.Kind != DateTimeKind.Utc
            || evidence.AsOfUtc > request.EvaluationAsOfUtc)
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "LOOKAHEAD_SCENARIO_EVIDENCE_DETECTED",
                evidence);
        }
        if (evidence.ExpiresUtc.HasValue
            && (evidence.ExpiresUtc.Value.Kind != DateTimeKind.Utc
                || evidence.ExpiresUtc.Value <= request.EvaluationAsOfUtc))
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.SnapshotExpired,
                "SCENARIO_SNAPSHOT_EXPIRED",
                evidence);
        }
        if (!evidence.HasStructuredEvidence
            || evidence.EvidenceIds is null
            || evidence.EvidenceIds.Count == 0
            || evidence.EvidenceIds.Any(string.IsNullOrWhiteSpace))
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "STRUCTURED_SCENARIO_EVIDENCE_MISSING",
                evidence);
        }
        if (evidence.ProbabilityWeight is not (> 0m and <= 1m)
            || evidence.Confidence is not (> 0m and <= 1m))
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "SCENARIO_WEIGHT_OR_CONFIDENCE_INVALID",
                evidence);
        }
        if (scenarioType == ScenarioType.GameState
            && evidence.HistoricalEventObservationCount < request.MinimumGameStateEventObservations)
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "GAME_STATE_EVENT_HISTORY_UNAVAILABLE",
                evidence);
        }
        if (scenarioType == ScenarioType.Base)
        {
            if (evidence.EvidenceStatus != EvidenceStatus.ReviewedNeutral
                || evidence.PredictionAdjustment.GetValueOrDefault() != 0m
                || evidence.ProbabilityAdjustment.GetValueOrDefault() != 0m)
            {
                return NotReady(
                    scenarioType,
                    EvidenceStatus.InsufficientEvidence,
                    "BASE_SCENARIO_REQUIRES_REAL_NEUTRAL_MODEL_EVIDENCE",
                    evidence);
            }
            return Ready(scenarioType, evidence);
        }
        if (evidence.EvidenceStatus == EvidenceStatus.ReviewedNeutral)
        {
            if (evidence.PredictionAdjustment.GetValueOrDefault() != 0m
                || evidence.ProbabilityAdjustment.GetValueOrDefault() != 0m)
            {
                return NotReady(
                    scenarioType,
                    EvidenceStatus.InsufficientEvidence,
                    "REVIEWED_NEUTRAL_CANNOT_HAVE_ADJUSTMENT",
                    evidence);
            }
            return Ready(scenarioType, evidence);
        }
        if (evidence.EvidenceStatus is not (EvidenceStatus.AppliedPositive or EvidenceStatus.AppliedNegative)
            || !evidence.IsAdjustmentValidated
            || string.IsNullOrWhiteSpace(evidence.AdjustmentVersion))
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "VALIDATED_SCENARIO_ADJUSTMENT_UNAVAILABLE",
                evidence);
        }

        var predictionAdjustment = evidence.PredictionAdjustment.GetValueOrDefault();
        var probabilityAdjustment = evidence.ProbabilityAdjustment.GetValueOrDefault();
        var hasPositiveAdjustment = predictionAdjustment > 0m || probabilityAdjustment > 0m;
        var hasNegativeAdjustment = predictionAdjustment < 0m || probabilityAdjustment < 0m;
        var signIsValid = evidence.EvidenceStatus switch
        {
            EvidenceStatus.AppliedPositive => hasPositiveAdjustment && !hasNegativeAdjustment,
            EvidenceStatus.AppliedNegative => hasNegativeAdjustment && !hasPositiveAdjustment,
            _ => false
        };
        if (!signIsValid)
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "SCENARIO_ADJUSTMENT_SIGN_MISMATCH",
                evidence);
        }
        if (request.BasePrediction + predictionAdjustment < 0m
            || request.BaseProbability + probabilityAdjustment is < 0m or > 1m)
        {
            return NotReady(
                scenarioType,
                EvidenceStatus.InsufficientEvidence,
                "SCENARIO_ADJUSTMENT_OUT_OF_RANGE",
                evidence);
        }

        return Ready(scenarioType, evidence);
    }

    private static ScenarioDataReadinessResult Ready(
        ScenarioType scenarioType,
        ScenarioEvidenceSnapshot evidence) => new(
            scenarioType,
            true,
            evidence.EvidenceStatus,
            evidence.Reason ?? "SCENARIO_EVIDENCE_READY",
            evidence);

    private static ScenarioDataReadinessResult NotReady(
        ScenarioType scenarioType,
        EvidenceStatus status,
        string reason,
        ScenarioEvidenceSnapshot? evidence = null) => new(
            scenarioType,
            false,
            status,
            reason,
            evidence);

    private static void ValidateRequest(ScenarioProviderRequest request)
    {
        if (request.EvaluationAsOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("EvaluationAsOfUtc must be UTC.", nameof(request));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MarketType);
        if (request.BasePrediction < 0m
            || request.BaseProbability is < 0m or > 1m
            || request.MinimumGameStateEventObservations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Invalid scenario request values.");
        }
    }
}

public abstract class EvidenceBackedScenarioProvider : IScenarioProvider
{
    private readonly IScenarioDataReadinessEvaluator _readiness;

    protected EvidenceBackedScenarioProvider(IScenarioDataReadinessEvaluator readiness) =>
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));

    public abstract ScenarioType ScenarioType { get; }

    protected abstract string DefaultScenarioName { get; }

    public ScenarioProviderResult Evaluate(ScenarioProviderRequest request)
    {
        var readiness = _readiness.Evaluate(request, ScenarioType);
        if (!readiness.IsReady || readiness.Evidence is null)
        {
            return new ScenarioProviderResult(
                ScenarioType,
                readiness.Evidence?.ScenarioName ?? DefaultScenarioName,
                0m,
                0m,
                0m,
                readiness.EvidenceStatus,
                0m,
                readiness.Evidence?.EvidenceIds ?? [],
                readiness.Evidence?.AsOfUtc,
                readiness.Evidence?.ExpiresUtc,
                false,
                readiness.Reason);
        }

        var evidence = readiness.Evidence;
        var isBase = ScenarioType == ScenarioType.Base;
        return new ScenarioProviderResult(
            ScenarioType,
            string.IsNullOrWhiteSpace(evidence.ScenarioName)
                ? DefaultScenarioName
                : evidence.ScenarioName,
            evidence.ProbabilityWeight!.Value,
            isBase ? 0m : evidence.PredictionAdjustment.GetValueOrDefault(),
            isBase ? 0m : evidence.ProbabilityAdjustment.GetValueOrDefault(),
            evidence.EvidenceStatus,
            evidence.Confidence!.Value,
            evidence.EvidenceIds.ToArray(),
            evidence.AsOfUtc,
            evidence.ExpiresUtc,
            true,
            readiness.Reason);
    }
}

public sealed class BaseScenarioProvider(IScenarioDataReadinessEvaluator readiness)
    : EvidenceBackedScenarioProvider(readiness)
{
    public override ScenarioType ScenarioType => ScenarioType.Base;
    protected override string DefaultScenarioName => "Base";
}

public sealed class LineupScenarioProvider(IScenarioDataReadinessEvaluator readiness)
    : EvidenceBackedScenarioProvider(readiness)
{
    public override ScenarioType ScenarioType => ScenarioType.Lineup;
    protected override string DefaultScenarioName => "Lineup";
}

public sealed class IntelligenceScenarioProvider(IScenarioDataReadinessEvaluator readiness)
    : EvidenceBackedScenarioProvider(readiness)
{
    public override ScenarioType ScenarioType => ScenarioType.Intelligence;
    protected override string DefaultScenarioName => "Intelligence";
}

public sealed class FatigueScenarioProvider(IScenarioDataReadinessEvaluator readiness)
    : EvidenceBackedScenarioProvider(readiness)
{
    public override ScenarioType ScenarioType => ScenarioType.Fatigue;
    protected override string DefaultScenarioName => "Fatigue";
}

public sealed class GameStateScenarioProvider(IScenarioDataReadinessEvaluator readiness)
    : EvidenceBackedScenarioProvider(readiness)
{
    public override ScenarioType ScenarioType => ScenarioType.GameState;
    protected override string DefaultScenarioName => "GameState";
}

public sealed class MarketMovementScenarioProvider(IScenarioDataReadinessEvaluator readiness)
    : EvidenceBackedScenarioProvider(readiness)
{
    public override ScenarioType ScenarioType => ScenarioType.MarketMovement;
    protected override string DefaultScenarioName => "MarketMovement";
}
