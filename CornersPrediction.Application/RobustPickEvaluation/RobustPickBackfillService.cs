using System.Text.Json;
using CornersPrediction.Domain.RobustPickEvaluation;
using Microsoft.Extensions.Logging;

namespace CornersPrediction.Application.RobustPickEvaluation;

public interface IRobustPickBackfillService
{
    Task<RobustBackfillExecutionResult> ExecuteAsync(
        RobustBackfillExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record RobustBackfillExecutionRequest(
    RobustBackfillPreviewFilter Filter,
    int BatchSize = 100,
    int MaximumCandidates = 1_000);

public sealed record RobustBackfillCheckpoint(
    DateTime? PredictionTimestampUtc,
    long? SourceEvaluationId);

public sealed record RobustBackfillFailure(long SourceEvaluationId, string Error);

public sealed class RobustBackfillExecutionResult
{
    public bool DryRun { get; init; }
    public RobustBackfillPreviewResult Preview { get; init; } = new();
    public int Loaded { get; init; }
    public int Evaluated { get; init; }
    public int Inserted { get; init; }
    public int Idempotent { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<RobustBackfillFailure> Failures { get; init; } = [];
    public RobustBackfillCheckpoint Checkpoint { get; init; } = new(null, null);
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Replays only immutable, pre-match audit candidates. The repository has already
/// enforced the quote, prediction and training cutoffs; this orchestrator keeps
/// each candidate's original AsOfUtc and forces Shadow semantics.
/// </summary>
public sealed class RobustPickBackfillService : IRobustPickBackfillService
{
    private readonly IRobustPickEvaluationRepository _repository;
    private readonly IRobustPickEvaluationService _evaluationService;
    private readonly ILogger<RobustPickBackfillService> _logger;

    public RobustPickBackfillService(
        IRobustPickEvaluationRepository repository,
        IRobustPickEvaluationService evaluationService,
        ILogger<RobustPickBackfillService> logger)
    {
        _repository = repository;
        _evaluationService = evaluationService;
        _logger = logger;
    }

    public async Task<RobustBackfillExecutionResult> ExecuteAsync(
        RobustBackfillExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BatchSize is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(request), "BatchSize must be between 1 and 1000.");
        if (request.MaximumCandidates is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(request), "MaximumCandidates must be between 1 and 100000.");

        var preview = await _repository.PreviewBackfillAsync(request.Filter, cancellationToken);
        if (request.Filter.DryRun)
        {
            return new RobustBackfillExecutionResult
            {
                DryRun = true,
                Preview = preview,
                Checkpoint = new(
                    request.Filter.AfterPredictionTimestampUtc,
                    request.Filter.AfterSourceEvaluationId),
                Message = "Dry-run completed. No robust evaluation was appended."
            };
        }

        var loaded = 0;
        var evaluated = 0;
        var inserted = 0;
        var idempotent = 0;
        var skipped = 0;
        var failures = new List<RobustBackfillFailure>();
        var cursorTimestamp = request.Filter.AfterPredictionTimestampUtc;
        var cursorId = request.Filter.AfterSourceEvaluationId;

        while (loaded < request.MaximumCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = Math.Min(request.BatchSize, request.MaximumCandidates - loaded);
            var pageFilter = request.Filter with
            {
                DryRun = false,
                AfterPredictionTimestampUtc = cursorTimestamp,
                AfterSourceEvaluationId = cursorId
            };
            var candidates = await _repository.LoadBackfillCandidatesAsync(
                pageFilter,
                size,
                cancellationToken);
            if (candidates.Count == 0)
                break;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                loaded++;
                try
                {
                    var execution = await _evaluationService.EvaluateAsync(
                        Map(candidate),
                        persist: true,
                        cancellationToken);
                    if (execution is null)
                    {
                        skipped++;
                    }
                    else
                    {
                        evaluated++;
                        if (execution.Persistence?.Inserted == true) inserted++;
                        else idempotent++;
                    }

                    cursorTimestamp = candidate.PredictionTimestampUtc;
                    cursorId = candidate.SourceEvaluationId;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(new RobustBackfillFailure(
                        candidate.SourceEvaluationId,
                        exception.Message));
                    _logger.LogError(
                        exception,
                        "Robust backfill stopped at SourceEvaluationId={SourceEvaluationId}; resume from checkpoint Timestamp={CheckpointTimestamp} Id={CheckpointId}",
                        candidate.SourceEvaluationId,
                        cursorTimestamp,
                        cursorId);
                    return Result("Backfill stopped on the first failed candidate; use the returned checkpoint to resume safely.");
                }
            }

            _logger.LogInformation(
                "Robust backfill progress: Loaded={Loaded} Evaluated={Evaluated} Inserted={Inserted} Idempotent={Idempotent} CheckpointTimestamp={CheckpointTimestamp} CheckpointId={CheckpointId}",
                loaded,
                evaluated,
                inserted,
                idempotent,
                cursorTimestamp,
                cursorId);
        }

        return Result(loaded >= request.MaximumCandidates
            ? "MaximumCandidates reached. Resume with the returned checkpoint."
            : "Backfill completed for the requested filter.");

        RobustBackfillExecutionResult Result(string message) => new()
        {
            DryRun = false,
            Preview = preview,
            Loaded = loaded,
            Evaluated = evaluated,
            Inserted = inserted,
            Idempotent = idempotent,
            Skipped = skipped,
            Failures = failures,
            Checkpoint = new(cursorTimestamp, cursorId),
            Message = message
        };
    }

    private static RobustPickEvaluationInput Map(RobustBackfillCandidateDto row)
    {
        var feature = Parse(row.FeatureSnapshotJson);
        var calibrationTier = Text(feature, "empiricalCalibration", "result", "EvidenceTier");
        var intelligenceStatus = IntelligenceStatus(feature, row.IntelligenceVersion);
        var originalStake = row.OriginalStake > 0m ? row.OriginalStake : 1m;
        var lower = row.ProbabilityLowerBound ?? Math.Min(row.RawProbability, row.CalibratedProbability);
        var upper = row.ProbabilityUpperBound ?? Math.Max(row.RawProbability, row.CalibratedProbability);

        return new RobustPickEvaluationInput
        {
            SourceEvaluationId = row.SourceEvaluationId,
            BotPickSelectionId = row.PublishedSelectionId,
            SourceOddsSnapshotId = row.SourceOddsSnapshotId,
            EvaluationSubjectKey = $"source-evaluation:{row.SourceEvaluationId}",
            BotKey = row.BotKey,
            MarketFamily = row.MarketFamily,
            MarketType = row.MarketType,
            SelectedSide = row.Side,
            League = row.League,
            HomeTeam = row.HomeTeam,
            AwayTeam = row.AwayTeam,
            Bookmaker = row.Bookmaker,
            AutomationVersion = row.AutomationVersion,
            FixtureId = row.FixtureId,
            ExternalFixtureId = row.ExternalFixtureId,
            FixtureStartUtc = Utc(row.MatchDateUtc),
            PredictionAsOfUtc = Utc(row.PredictionTimestampUtc),
            EvaluationAsOfUtc = Utc(row.PredictionTimestampUtc),
            QuoteTimestampUtc = Utc(row.OddsTimestampUtc),
            Line = row.Line,
            SelectedOdds = row.SelectedOdds,
            OverOdds = row.OverOdds,
            UnderOdds = row.UnderOdds,
            OriginalStake = originalStake,
            CurrentMinimumPointEdge = Decimal(feature, "selector", "thresholds", "MinimumFinalEdge") ?? 0m,
            CurrentMinimumPointExpectedValue = Decimal(feature, "selector", "thresholds", "MinimumFinalExpectedValue") ?? 0m,
            CurrentDecision = row.PublishedSelectionId.HasValue
                ? CurrentSystemDecision.Bet
                : CurrentSystemDecision.NoBet,
            EvaluationModeOverride = EvaluationMode.Shadow,
            PrimaryPrediction = row.PrimaryPrediction,
            DirectPrediction = row.DirectPrediction ?? row.PrimaryPrediction,
            HomePrediction = row.HomePrediction,
            AwayPrediction = row.AwayPrediction,
            ContextPrediction = row.ContextPrediction,
            ConfiguredModelMae = Decimal(feature, "model", "sigma"),
            RawProbability = row.RawProbability,
            CalibratedProbability = row.CalibratedProbability,
            ProbabilityBeforeIntelligence = Decimal(
                feature,
                "footballIntelligence", "probabilityBeforeFootballIntelligence"),
            ProbabilityLowerBound = Math.Clamp(lower, 0m, 1m),
            ProbabilityUpperBound = Math.Clamp(Math.Max(lower, upper), 0m, 1m),
            DataQualityScore = row.DataQualityScore,
            BaseModelVersion = row.BaseModelVersion,
            ModelTrainedThroughUtc = Utc(row.ModelTrainedThroughUtc),
            SelectorVersion = row.SelectorVersion,
            CalibrationVersion = row.CalibrationVersion,
            IntelligenceVersion = row.IntelligenceVersion,
            CalibrationEffectiveN = Decimal(feature, "empiricalCalibration", "result", "EffectiveSampleSize"),
            CalibrationExactMarketN = Integer(feature, "empiricalCalibration", "result", "ExactMarketRows") ?? 0,
            CalibrationFamilyN = Integer(feature, "empiricalCalibration", "result", "FamilyRows") ?? 0,
            CalibrationGlobalN = Integer(feature, "empiricalCalibration", "result", "GlobalRows") ?? 0,
            CalibrationFallbackLevel = CalibrationFallback(calibrationTier),
            CalibrationError = Decimal(feature, "empiricalCalibration", "result", "SourceBrierScore"),
            CalibrationPriorWeight = Decimal(feature, "empiricalCalibration", "result", "PriorWeight"),
            CalibrationIntervalMethod = Text(feature, "empiricalCalibration", "result", "IntervalMethod"),
            CalibrationConfidenceLevel = Decimal(feature, "empiricalCalibration", "result", "ConfidenceLevel"),
            ScenarioEvidence = new Dictionary<ScenarioType, ScenarioEvidenceSnapshot>
            {
                [ScenarioType.Base] = new(
                    ScenarioType.Base,
                    "Historical base model snapshot",
                    EvidenceStatus.ReviewedNeutral,
                    HasStructuredEvidence: true,
                    IsAdjustmentValidated: true,
                    ProbabilityWeight: 1m,
                    PredictionAdjustment: 0m,
                    ProbabilityAdjustment: 0m,
                    Confidence: Math.Max(0.01m, row.DataQualityScore),
                    EvidenceIds:
                    [
                        $"source-evaluation:{row.SourceEvaluationId}",
                        $"odds-snapshot:{row.SourceOddsSnapshotId}",
                        $"model:{row.BaseModelVersion}"
                    ],
                    AsOfUtc: Utc(row.PredictionTimestampUtc),
                    ExpiresUtc: Utc(row.MatchDateUtc),
                    AdjustmentVersion: row.BaseModelVersion,
                    HistoricalEventObservationCount: 0,
                    Reason: "IMMUTABLE_HISTORICAL_BASE_EVIDENCE")
            },
            IntelligenceEvidenceStatus = intelligenceStatus,
            LineupStatus = row.IntelligenceVersion is null
                ? nameof(EvidenceStatus.NotApplicable)
                : nameof(EvidenceStatus.InsufficientEvidence),
            FatigueDataStatus = nameof(EvidenceStatus.NotApplicable),
            GameStateModelStatus = nameof(EvidenceStatus.NotApplicable)
        };
    }

    private static EvidenceStatus IntelligenceStatus(JsonElement? root, string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return EvidenceStatus.NotApplicable;
        var applied = Boolean(root, "footballIntelligence", "result", "IsApplied") ?? false;
        var adjustment = Decimal(root, "footballIntelligence", "result", "ProbabilityAdjustment") ?? 0m;
        if (applied) return adjustment < 0m ? EvidenceStatus.AppliedNegative : EvidenceStatus.AppliedPositive;
        var home = Text(root, "footballIntelligence", "result", "HomeEvidenceStatus");
        var away = Text(root, "footballIntelligence", "result", "AwayEvidenceStatus");
        var statuses = new[] { home, away }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (statuses.Any(value => value!.Equals("Stale", StringComparison.OrdinalIgnoreCase)
            || value.Equals("FutureCutoff", StringComparison.OrdinalIgnoreCase)))
            return EvidenceStatus.SnapshotExpired;
        if (statuses.Any(value => value!.Equals("Available", StringComparison.OrdinalIgnoreCase)))
            return EvidenceStatus.ReviewedNeutral;
        return statuses.Length == 0 ? EvidenceStatus.SourceUnavailable : EvidenceStatus.InsufficientEvidence;
    }

    private static CalibrationFallbackLevel CalibrationFallback(string? value) =>
        value?.Contains("Exact", StringComparison.OrdinalIgnoreCase) == true
            ? CalibrationFallbackLevel.ExactMarket
            : value?.Contains("Family", StringComparison.OrdinalIgnoreCase) == true
                ? CalibrationFallbackLevel.MarketFamily
                : value?.Contains("Global", StringComparison.OrdinalIgnoreCase) == true
                    ? CalibrationFallbackLevel.Global
                    : CalibrationFallbackLevel.Unavailable;

    private static JsonElement? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryPath(JsonElement? root, out JsonElement value, params string[] path)
    {
        value = default;
        if (!root.HasValue) return false;
        var current = root.Value;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return false;
            var match = current.EnumerateObject().FirstOrDefault(property =>
                property.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (match.Equals(default(JsonProperty))) return false;
            current = match.Value;
        }
        value = current;
        return true;
    }

    private static string? Text(JsonElement? root, params string[] path) =>
        TryPath(root, out var value, path)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static decimal? Decimal(JsonElement? root, params string[] path) =>
        TryPath(root, out var value, path) && value.TryGetDecimal(out var number) ? number : null;

    private static int? Integer(JsonElement? root, params string[] path) =>
        TryPath(root, out var value, path) && value.TryGetInt32(out var number) ? number : null;

    private static bool? Boolean(JsonElement? root, params string[] path) =>
        TryPath(root, out var value, path) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
