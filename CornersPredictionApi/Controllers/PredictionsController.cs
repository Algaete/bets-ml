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
    private readonly ILogger<PredictionsController> _logger;

    public PredictionsController(
        IPredictTotalCornersUseCase predictTotalCornersUseCase,
        ILogger<PredictionsController> logger)
    {
        _predictTotalCornersUseCase = predictTotalCornersUseCase;
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
}
