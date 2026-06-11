using CornersPrediction.Application.Predictions;
using CornersPredictionApi.Requests;
using CornersPrediction.Domain.Predictions;
using Microsoft.AspNetCore.Mvc;

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
    private readonly ILogger<PredictionsController> _logger;

    public PredictionsController(
        IPredictTotalCornersUseCase predictTotalCornersUseCase,
        IOverUnderPredictionUseCase overUnderPredictionUseCase,
        IShotsOnGoalPredictionUseCase shotsOnGoalPredictionUseCase,
        IModelDebugPredictionUseCase modelDebugPredictionUseCase,
        ILogger<PredictionsController> logger)
    {
        _predictTotalCornersUseCase = predictTotalCornersUseCase;
        _overUnderPredictionUseCase = overUnderPredictionUseCase;
        _shotsOnGoalPredictionUseCase = shotsOnGoalPredictionUseCase;
        _modelDebugPredictionUseCase = modelDebugPredictionUseCase;
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
            return Ok(result);
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
