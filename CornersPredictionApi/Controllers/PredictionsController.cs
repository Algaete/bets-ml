using CornersPrediction.Application.Predictions;
using CornersPrediction.Infrastructure.Options;
using CornersPredictionApi.Requests;
using CornersPrediction.Domain.Predictions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.Controllers;

/// <summary>
/// Exposes HTTP endpoints for ML predictions.
/// </summary>
[ApiController]
[Route("")]
public sealed class PredictionsController : ControllerBase
{
    private readonly IPredictTotalCornersUseCase _predictTotalCornersUseCase;
    private readonly IOverUnderPredictionUseCase _overUnderPredictionUseCase;
    private readonly IShotsOnGoalPredictionUseCase _shotsOnGoalPredictionUseCase;
    private readonly IModelDebugPredictionUseCase _modelDebugPredictionUseCase;
    private readonly PredictionAdjustmentOptions _predictionAdjustmentOptions;
    private readonly ILogger<PredictionsController> _logger;

    public PredictionsController(
        IPredictTotalCornersUseCase predictTotalCornersUseCase,
        IOverUnderPredictionUseCase overUnderPredictionUseCase,
        IShotsOnGoalPredictionUseCase shotsOnGoalPredictionUseCase,
        IModelDebugPredictionUseCase modelDebugPredictionUseCase,
        IOptions<PredictionAdjustmentOptions> predictionAdjustmentOptions,
        ILogger<PredictionsController> logger)
    {
        _predictTotalCornersUseCase = predictTotalCornersUseCase;
        _overUnderPredictionUseCase = overUnderPredictionUseCase;
        _shotsOnGoalPredictionUseCase = shotsOnGoalPredictionUseCase;
        _modelDebugPredictionUseCase = modelDebugPredictionUseCase;
        _predictionAdjustmentOptions = predictionAdjustmentOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Receives match features as JSON and returns the predicted total corners.
    /// </summary>
    [HttpPost("predict")]
    [ProducesResponseType(typeof(PredictionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Predict([FromBody] PredictTotalCornersRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            // Convert the typed request back to JSON so the Application layer stays model-agnostic.
            var features = request.ToJsonElement();
            var result = await _predictTotalCornersUseCase.PredictAsync(features, cancellationToken);
            var adjustedResult = ApplyRankingAdjustment(result, request);
            return Ok(adjustedResult);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (PredictionException exception)
        {
            _logger.LogError(exception, "Prediction failed with error type {ErrorType}", exception.ErrorType);

            var statusCode = exception.ErrorType switch
            {
                PredictionErrorType.Timeout => StatusCodes.Status504GatewayTimeout,
                PredictionErrorType.PythonNotFound => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.MissingDependency => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.ScriptNotFound => StatusCodes.Status500InternalServerError,
                PredictionErrorType.InvalidOutput => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };

            return Problem(
                detail: exception.Message,
                statusCode: statusCode,
                title: "Prediction failed");
        }
    }

    private PredictionResult ApplyRankingAdjustment(PredictionResult baseResult, PredictTotalCornersRequest request)
    {
        var baseHomeCorners = baseResult.PredHomeCorners ?? baseResult.LegacyHomeCorners;
        var baseAwayCorners = baseResult.PredAwayCorners ?? baseResult.LegacyAwayCorners;
        var baseTotalCorners = baseResult.PredFinal ?? baseResult.PredictedTotalCorners;

        if (!_predictionAdjustmentOptions.EnableRankingAdjustment)
        {
            return BuildRankingAdjustedResult(
                baseResult,
                baseHomeCorners,
                baseAwayCorners,
                baseTotalCorners,
                baseHomeCorners,
                baseAwayCorners,
                baseTotalCorners,
                new RankingAdjustmentResult
                {
                    Enabled = false,
                    Applied = false,
                    Reason = "Ranking adjustment disabled by configuration"
                });
        }

        var validationError = ValidateRankingRequest(request);
        if (validationError is not null || baseHomeCorners is null || baseAwayCorners is null)
        {
            return BuildRankingAdjustedResult(
                baseResult,
                baseHomeCorners,
                baseAwayCorners,
                baseTotalCorners,
                baseHomeCorners,
                baseAwayCorners,
                baseTotalCorners,
                new RankingAdjustmentResult
                {
                    Enabled = true,
                    Applied = false,
                    Reason = validationError ?? "Base home/away ML prediction not available"
                });
        }

        var totalTeams = request.RankingTotalTeams!.Value;
        var homeStrength = 1 - ((request.HomeRankingPosition!.Value - 1d) / (totalTeams - 1d));
        var awayStrength = 1 - ((request.AwayRankingPosition!.Value - 1d) / (totalTeams - 1d));
        var strengthDiff = homeStrength - awayStrength;
        var homeMaxImpact = Math.Abs(_predictionAdjustmentOptions.HomeRankingMaxImpactPct);
        var awayMaxImpact = Math.Abs(_predictionAdjustmentOptions.AwayRankingMaxImpactPct);
        var homeAdjustmentPct = Math.Clamp(strengthDiff * homeMaxImpact, -homeMaxImpact, homeMaxImpact);
        var awayAdjustmentPct = Math.Clamp(-strengthDiff * awayMaxImpact, -awayMaxImpact, awayMaxImpact);
        var finalHomeCorners = Math.Max(0, baseHomeCorners.Value * (1 + homeAdjustmentPct));
        var finalAwayCorners = Math.Max(0, baseAwayCorners.Value * (1 + awayAdjustmentPct));
        var finalTotalCorners = finalHomeCorners + finalAwayCorners;

        return BuildRankingAdjustedResult(
            baseResult,
            baseHomeCorners,
            baseAwayCorners,
            baseTotalCorners,
            finalHomeCorners,
            finalAwayCorners,
            finalTotalCorners,
            new RankingAdjustmentResult
            {
                Enabled = true,
                Applied = true,
                HomeRankingPosition = request.HomeRankingPosition,
                AwayRankingPosition = request.AwayRankingPosition,
                RankingTotalTeams = request.RankingTotalTeams,
                RankingSource = NormalizeOptional(request.RankingSource),
                RankingSeason = NormalizeOptional(request.RankingSeason),
                HomeRankingStrength = homeStrength,
                AwayRankingStrength = awayStrength,
                RankingStrengthDiff = strengthDiff,
                HomeAdjustmentPct = homeAdjustmentPct,
                AwayAdjustmentPct = awayAdjustmentPct
            });
    }

    private static PredictionResult BuildRankingAdjustedResult(
        PredictionResult baseResult,
        double? baseHomeCorners,
        double? baseAwayCorners,
        double baseTotalCorners,
        double? finalHomeCorners,
        double? finalAwayCorners,
        double finalTotalCorners,
        RankingAdjustmentResult rankingAdjustment)
    {
        return new PredictionResult(finalTotalCorners)
        {
            PredTotalDirect = baseResult.PredTotalDirect,
            PredHomeCorners = finalHomeCorners,
            PredAwayCorners = finalAwayCorners,
            PredTotalCombined = finalHomeCorners is null || finalAwayCorners is null
                ? baseResult.PredTotalCombined
                : finalHomeCorners + finalAwayCorners,
            PredFinal = finalTotalCorners,
            PredFinalRounded = Math.Round(finalTotalCorners, 2),
            ProbableRangeLow = baseResult.ProbableRangeLow,
            ProbableRangeHigh = baseResult.ProbableRangeHigh,
            WideRangeLow = baseResult.WideRangeLow,
            WideRangeHigh = baseResult.WideRangeHigh,
            BettingLine = baseResult.BettingLine,
            RecommendedSide = baseResult.RecommendedSide,
            DistanceToLine = baseResult.BettingLine is null
                ? baseResult.DistanceToLine
                : Math.Abs(finalTotalCorners - baseResult.BettingLine.Value),
            Confidence = baseResult.Confidence,
            Message = baseResult.Message,
            LegacyHomeCorners = baseResult.LegacyHomeCorners,
            LegacyAwayCorners = baseResult.LegacyAwayCorners,
            LegacyTotalCorners = baseResult.LegacyTotalCorners,
            ModelDifference = baseResult.ModelDifference,
            ModelConsensus = baseResult.ModelConsensus,
            BaseHomeCorners = baseHomeCorners,
            BaseAwayCorners = baseAwayCorners,
            BaseTotalCorners = baseTotalCorners,
            RankingAdjustment = rankingAdjustment,
            FinalHomeCorners = finalHomeCorners,
            FinalAwayCorners = finalAwayCorners,
            FinalTotalCorners = finalTotalCorners
        };
    }

    private static string? ValidateRankingRequest(PredictTotalCornersRequest request)
    {
        if (request.HomeRankingPosition is null && request.AwayRankingPosition is null)
        {
            return "Ranking data not provided or incomplete";
        }

        if (request.HomeRankingPosition is null ||
            request.AwayRankingPosition is null ||
            request.RankingTotalTeams is null)
        {
            return "Ranking data not provided or incomplete";
        }

        if (request.HomeRankingPosition < 1)
        {
            return "HomeRankingPosition must be greater than or equal to 1";
        }

        if (request.AwayRankingPosition < 1)
        {
            return "AwayRankingPosition must be greater than or equal to 1";
        }

        if (request.RankingTotalTeams < 2)
        {
            return "RankingTotalTeams must be greater than or equal to 2";
        }

        if (request.HomeRankingPosition > request.RankingTotalTeams)
        {
            return "HomeRankingPosition cannot be greater than RankingTotalTeams";
        }

        if (request.AwayRankingPosition > request.RankingTotalTeams)
        {
            return "AwayRankingPosition cannot be greater than RankingTotalTeams";
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Receives match features plus a betting line and returns the Over/Under recommendation.
    /// </summary>
    [HttpPost("predict/over-under")]
    [ProducesResponseType(typeof(OverUnderPredictionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> PredictOverUnder(
        [FromBody] OverUnderPredictionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Features is null || request.Features.Count == 0)
        {
            return BadRequest(new { error = "Over/Under prediction features payload must be a JSON object." });
        }

        try
        {
            var result = await _overUnderPredictionUseCase.PredictAsync(
                request.ToJsonElement(),
                cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (PredictionException exception)
        {
            _logger.LogError(exception, "Over/Under prediction failed with error type {ErrorType}", exception.ErrorType);

            var statusCode = exception.ErrorType switch
            {
                PredictionErrorType.Timeout => StatusCodes.Status504GatewayTimeout,
                PredictionErrorType.PythonNotFound => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.MissingDependency => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.ScriptNotFound => StatusCodes.Status500InternalServerError,
                PredictionErrorType.InvalidOutput => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };

            return Problem(
                detail: exception.Message,
                statusCode: statusCode,
                title: "Over/Under prediction failed");
        }
    }

    /// <summary>
    /// Receives match features and returns the predicted total shots on goal.
    /// </summary>
    [HttpPost("predict/shots-on-goal")]
    [ProducesResponseType(typeof(ShotsOnGoalPredictionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> PredictShotsOnGoal(
        [FromBody] ShotsOnGoalPredictionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Features is null || request.Features.Count == 0)
        {
            return BadRequest(new { error = "Shots-on-goal prediction features payload must be a JSON object." });
        }

        try
        {
            var result = await _shotsOnGoalPredictionUseCase.PredictAsync(
                request.ToJsonElement(),
                cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (PredictionException exception)
        {
            _logger.LogError(exception, "Shots-on-goal prediction failed with error type {ErrorType}", exception.ErrorType);

            var statusCode = exception.ErrorType switch
            {
                PredictionErrorType.Timeout => StatusCodes.Status504GatewayTimeout,
                PredictionErrorType.PythonNotFound => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.MissingDependency => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.ScriptNotFound => StatusCodes.Status500InternalServerError,
                PredictionErrorType.InvalidOutput => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };

            return Problem(
                detail: exception.Message,
                statusCode: statusCode,
                title: "Shots-on-goal prediction failed");
        }
    }

    /// <summary>
    /// Debug endpoint: executes a raw model artifact by key. Valid keys include corners-total, shots-total and sog-total.
    /// </summary>
    [HttpPost("predict/debug/{modelKey}")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public Task<IActionResult> PredictDebugModel(
        [FromRoute] string modelKey,
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken)
    {
        return PredictDebugModelCore(modelKey, request, cancellationToken);
    }

    [HttpPost("predict/debug/corners/total")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugCornersTotal(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("corners-total", request, cancellationToken);

    [HttpPost("predict/debug/corners/home")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugCornersHome(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("corners-home", request, cancellationToken);

    [HttpPost("predict/debug/corners/away")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugCornersAway(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("corners-away", request, cancellationToken);

    [HttpPost("predict/debug/shots/total")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugShotsTotal(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("shots-total", request, cancellationToken);

    [HttpPost("predict/debug/shots/home")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugShotsHome(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("shots-home", request, cancellationToken);

    [HttpPost("predict/debug/shots/away")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugShotsAway(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("shots-away", request, cancellationToken);

    [HttpPost("predict/debug/sog/total")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugSogTotal(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("sog-total", request, cancellationToken);

    [HttpPost("predict/debug/sog/home")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugSogHome(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("sog-home", request, cancellationToken);

    [HttpPost("predict/debug/sog/away")]
    [ProducesResponseType(typeof(DebugModelPredictionResult), StatusCodes.Status200OK)]
    public Task<IActionResult> PredictDebugSogAway(
        [FromBody] ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken) =>
        PredictDebugModelCore("sog-away", request, cancellationToken);

    private async Task<IActionResult> PredictDebugModelCore(
        string modelKey,
        ModelDebugPredictionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Debug model features payload must be a JSON object." });
        }

        try
        {
            var result = await _modelDebugPredictionUseCase.PredictAsync(
                modelKey,
                request.ToJsonElement(),
                cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (PredictionException exception)
        {
            _logger.LogError(
                exception,
                "Debug model prediction failed for model {ModelKey} with error type {ErrorType}",
                modelKey,
                exception.ErrorType);

            var statusCode = exception.ErrorType switch
            {
                PredictionErrorType.Timeout => StatusCodes.Status504GatewayTimeout,
                PredictionErrorType.PythonNotFound => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.MissingDependency => StatusCodes.Status503ServiceUnavailable,
                PredictionErrorType.ScriptNotFound => StatusCodes.Status500InternalServerError,
                PredictionErrorType.InvalidOutput => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };

            return Problem(
                detail: exception.Message,
                statusCode: statusCode,
                title: "Debug model prediction failed");
        }
    }
}
