using System.Text.Json;
using CornersPrediction.Application.RobustPickEvaluation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/robust-pick-evaluations")]
public sealed class RobustPickEvaluationsController : ControllerBase
{
    private readonly IRobustPickEvaluationRepository _repository;
    private readonly IRobustPickBackfillService _backfillService;
    private readonly RobustPickEvaluationOptions _options;
    private readonly ILogger<RobustPickEvaluationsController> _logger;

    public RobustPickEvaluationsController(
        IRobustPickEvaluationRepository repository,
        IRobustPickBackfillService backfillService,
        IOptions<RobustPickEvaluationOptions> options,
        ILogger<RobustPickEvaluationsController> logger)
    {
        _repository = repository;
        _backfillService = backfillService;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("{selectionId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(
        [FromRoute] long selectionId,
        CancellationToken cancellationToken = default)
    {
        if (selectionId <= 0) return BadRequest(new { error = "selectionId must be positive." });
        try
        {
            var detail = await _repository.GetCurrentBySelectionIdAsync(selectionId, cancellationToken);
            return detail is null ? NotFound() : Ok(ToDetailResponse(selectionId, detail));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load robust evaluation for selection {SelectionId}", selectionId);
            return Unavailable("Could not load the robust pick evaluation.");
        }
    }

    [HttpGet("{selectionId:long}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<RobustPickEvaluationSnapshot>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromRoute] long selectionId,
        CancellationToken cancellationToken = default)
    {
        if (selectionId <= 0) return BadRequest(new { error = "selectionId must be positive." });
        try
        {
            return Ok(await _repository.GetHistoryBySelectionIdAsync(selectionId, cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load robust history for selection {SelectionId}", selectionId);
            return Unavailable("Could not load robust evaluation history.");
        }
    }

    [HttpGet("{selectionId:long}/comparison")]
    [ProducesResponseType(typeof(RobustEvaluationComparisonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetComparison(
        [FromRoute] long selectionId,
        CancellationToken cancellationToken = default)
    {
        if (selectionId <= 0) return BadRequest(new { error = "selectionId must be positive." });
        try
        {
            var value = await _repository.GetComparisonBySelectionIdAsync(selectionId, cancellationToken);
            return value is null ? NotFound() : Ok(value);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load robust comparison for selection {SelectionId}", selectionId);
            return Unavailable("Could not load current-versus-robust comparison.");
        }
    }

    [HttpGet("metrics")]
    [ProducesResponseType(typeof(RobustEvaluationMetricsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? botKey,
        [FromQuery] string? marketFamily,
        [FromQuery] string? marketType,
        [FromQuery] string? evaluationVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _repository.GetMetricsAsync(
                new RobustEvaluationMetricsFilter(
                    fromUtc, toUtc, botKey, marketFamily, marketType, evaluationVersion),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load robust evaluation metrics");
            return Unavailable("Could not load robust evaluation metrics.");
        }
    }

    /// <summary>
    /// Leakage audit and resumable append-only backfill. The repository admits only
    /// immutable pre-match candidates with an exact bilateral odds snapshot.
    /// </summary>
    [HttpPost("backfill")]
    [ProducesResponseType(typeof(RobustBackfillExecutionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Backfill(
        [FromBody] RobustBackfillRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { error = "Request body is required." });

        try
        {
            var evaluationVersion = string.IsNullOrWhiteSpace(request.EvaluationVersion)
                ? _options.Version
                : request.EvaluationVersion.Trim();
            if (!evaluationVersion.Equals(_options.Version, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    error = $"EvaluationVersion '{evaluationVersion}' is not the deployed evaluator '{_options.Version}'."
                });
            }

            var filter = new RobustBackfillPreviewFilter(
                    request.FromUtc,
                    request.ToUtc,
                    request.BotKey,
                    request.MarketFamily,
                    request.MarketType,
                    request.FixtureId,
                    evaluationVersion,
                    request.DryRun,
                    request.Force,
                    request.AfterPredictionTimestampUtc,
                    request.AfterSourceEvaluationId);
            var result = await _backfillService.ExecuteAsync(
                new RobustBackfillExecutionRequest(
                    filter,
                    request.BatchSize,
                    request.MaximumCandidates),
                cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not preview robust evaluation backfill");
            return Unavailable("Could not preview robust evaluation backfill.");
        }
    }

    [HttpGet("policies/effective")]
    [ProducesResponseType(typeof(RobustPolicySnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEffectivePolicy(
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] string? botKey,
        [FromQuery] string? marketFamily,
        [FromQuery] string? marketType,
        [FromQuery] string? marketScope,
        [FromQuery] string? side,
        [FromQuery] string? league,
        [FromQuery] decimal? line,
        [FromQuery] decimal? odds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var policy = await _repository.GetEffectivePolicyAsync(
                new RobustPolicyQuery(
                    asOfUtc ?? DateTime.UtcNow,
                    botKey, marketFamily, marketType, marketScope, side, league, line, odds),
                cancellationToken);
            return policy is null ? NotFound() : Ok(policy);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load the effective robust policy");
            return Unavailable("Could not load the effective robust policy.");
        }
    }

    [HttpGet("policies/history")]
    [ProducesResponseType(typeof(IReadOnlyList<RobustPolicySnapshot>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicyHistory(
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] string? botKey,
        [FromQuery] string? marketFamily,
        [FromQuery] string? marketType,
        [FromQuery] string? marketScope,
        [FromQuery] string? side,
        [FromQuery] string? league,
        [FromQuery] decimal? line,
        [FromQuery] decimal? odds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _repository.GetPolicyHistoryAsync(
                new RobustPolicyQuery(
                    asOfUtc ?? DateTime.UtcNow,
                    botKey, marketFamily, marketType, marketScope, side, league, line, odds),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load robust policy history");
            return Unavailable("Could not load robust policy history.");
        }
    }

    [HttpPost("policies")]
    [ProducesResponseType(typeof(AppendRobustPolicyResult), StatusCodes.Status201Created)]
    public async Task<IActionResult> AppendPolicy(
        [FromBody] AppendRobustPolicyCommand? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { error = "Request body is required." });
        try
        {
            var result = await _repository.AppendPolicyAsync(request, cancellationToken);
            return result.Inserted
                ? Created($"/api/robust-pick-evaluations/policies/{result.RobustPolicyId}", result)
                : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not append robust policy {PolicyVersion}", request.PolicyVersion);
            return Unavailable("Could not append the robust policy.");
        }
    }

    private ObjectResult Unavailable(string detail) => Problem(
        title: "Robust Pick Evaluation is unavailable",
        detail: detail,
        statusCode: StatusCodes.Status503ServiceUnavailable);

    private static object ToDetailResponse(long selectionId, RobustPickEvaluationDetail detail)
    {
        var value = detail.Evaluation;
        return new
        {
            pickId = selectionId,
            identity = new
            {
                value.BotKey,
                value.MarketFamily,
                value.MarketType,
                value.Side,
                value.Line,
                value.Bookmaker,
                value.FixtureId,
                value.SourceEvaluationId,
                value.SourceOddsSnapshotId
            },
            evaluation = new
            {
                id = value.RobustEvaluationId,
                sequence = value.EvaluationSequence,
                mode = value.EvaluationMode,
                currentDecision = value.CurrentSystemDecision,
                robustDecision = value.RobustDecision,
                value.HumanReadableReason,
                value.RobustnessScore,
                value.OriginalStake,
                value.RecommendedStake,
                value.AsOfUtc,
                value.EvaluationVersion,
                value.IsCurrent,
                value.SupersedesEvaluationId
            },
            versions = new
            {
                value.BaseModelVersion,
                value.SelectorVersion,
                value.CalibrationVersion,
                value.IntelligenceVersion,
                value.SettlementVersion,
                value.RobustnessVersion,
                value.PolicyVersion
            },
            predictions = new
            {
                direct = value.DirectPrediction,
                home = value.HomePrediction,
                away = value.AwayPrediction,
                components = value.ComponentsPrediction,
                context = value.ContextPrediction,
                reconciled = value.ReconciledPrediction
            },
            consensus = new
            {
                value.SideAgreement,
                range = value.ConsensusRange,
                value.CoherenceGap,
                value.WorstCasePrediction,
                value.WorstCaseDistance,
                value.NormalizedWorstCaseDistance,
                magnitudeAgreement = value.MagnitudeAgreementScore,
                probabilityAgreement = value.ProbabilityAgreementScore,
                value.CoherenceScore,
                scenarioStability = value.ScenarioStability,
                value.DirectDistance,
                value.ComponentsDistance,
                value.ContextDistance,
                value.ReconciledDistance
            },
            distribution = new
            {
                value.P10,
                value.P50,
                value.P90,
                value.PWin,
                value.PHalfWin,
                value.PPush,
                value.PHalfLoss,
                value.PLoss,
                value.ErrorScale,
                effectiveN = value.DistributionEffectiveN,
                value.SimulationCount,
                value.DistributionMethod,
                value.DistributionVersion
            },
            probability = new
            {
                central = value.CalibratedProbability ?? value.RawProbability,
                lower = value.ProbabilityLowerBound,
                upper = value.ProbabilityUpperBound,
                fair = value.RobustModelFairProbability ?? value.ModelFairProbability,
                marketImplied = value.MarketImpliedProbability,
                marketNoVig = value.MarketNoVigProbability,
                conservativeMarket = value.ConservativeMarketProbability,
                raw = value.RawProbability,
                beforeCalibration = value.RawProbability,
                afterCalibration = value.CalibratedProbability
            },
            value = new
            {
                value.PointEdge,
                value.RobustEdge,
                pointEv = value.PointExpectedValue,
                robustEv = value.RobustExpectedValue,
                value.PositiveEvStability,
                value.ExpectedValueP10,
                value.ExpectedValueP50,
                value.ExpectedValueP90
            },
            calibration = new
            {
                effectiveN = value.CalibrationEffectiveN,
                reliability = value.CalibrationReliability,
                fallbackLevel = value.CalibrationFallbackLevel,
                exactMarketN = value.CalibrationExactMarketN,
                familyN = value.CalibrationFamilyN,
                globalN = value.CalibrationGlobalN,
                value.CalibrationSpecificityScore,
                value.CalibrationRecencyScore,
                value.CalibrationErrorScore,
                priorWeight = ReadDecimal(value.EvaluationPayloadJson, "calibration", "priorWeight"),
                intervalMethod = ReadString(value.EvaluationPayloadJson, "calibration", "intervalMethod"),
                confidenceLevel = ReadDecimal(value.EvaluationPayloadJson, "calibration", "confidenceLevel")
            },
            preMatchData = new
            {
                value.LineupStatus,
                value.IntelligenceEvidenceStatus,
                value.FatigueDataStatus,
                value.GameStateModelStatus,
                value.OddsAgeSeconds,
                value.QuoteTimestampUtc,
                value.OddsReliability,
                oddsAvailabilityStatus = ReadString(
                    value.EvaluationPayloadJson, "preMatch", "oddsAvailabilityStatus"),
                snapshotAgeSeconds = Multiply(
                    ReadInt(value.EvaluationPayloadJson, "preMatch", "intelligenceSnapshotAgeMinutes"), 60),
                actionableFacts = ReadInt(value.EvaluationPayloadJson, "preMatch", "actionableFactCount"),
                independentSources = ReadInt(value.EvaluationPayloadJson, "preMatch", "independentSourceCount")
            },
            reasons = ReadStringArray(value.RejectionReasonCodesJson),
            warnings = ReadStringArray(value.WarningCodesJson),
            components = detail.Components.Select(component => new
            {
                component.ComponentSequence,
                component.ComponentType,
                component.PredictedValue,
                component.ProbabilityForSelection,
                component.Weight,
                component.IsUsable,
                component.SourceVersion,
                component.AsOfUtc,
                component.ExclusionReason,
                component.DataQualityScore
            })
        };
    }

    private static IReadOnlyList<string> ReadStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? ReadInt(string json, params string[] path)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            foreach (var propertyName in path)
            {
                if (value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty(propertyName, out value))
                    return null;
            }
            if (value.TryGetInt32(out var number))
                return number;
        }
        catch (JsonException)
        {
            // Optional presentation metadata must never make the audit unavailable.
        }
        return null;
    }

    private static decimal? ReadDecimal(string json, params string[] path)
    {
        var value = ReadPath(json, path);
        return value.HasValue && value.Value.TryGetDecimal(out var number) ? number : null;
    }

    private static string? ReadString(string json, params string[] path)
    {
        var value = ReadPath(json, path);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.String
            ? value.Value.GetString()
            : null;
    }

    private static JsonElement? ReadPath(string json, params string[] path)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            foreach (var propertyName in path)
            {
                if (value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty(propertyName, out value))
                    return null;
            }
            return value.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Multiply(int? value, int multiplier) => value.HasValue
        ? checked(value.Value * multiplier)
        : null;
}

public sealed record RobustBackfillRequest(
    DateTime FromUtc,
    DateTime ToUtc,
    string? BotKey = null,
    string? MarketFamily = null,
    string? MarketType = null,
    long? FixtureId = null,
    string? EvaluationVersion = null,
    bool DryRun = true,
    bool Force = false,
    int BatchSize = 100,
    int MaximumCandidates = 1000,
    DateTime? AfterPredictionTimestampUtc = null,
    long? AfterSourceEvaluationId = null);
