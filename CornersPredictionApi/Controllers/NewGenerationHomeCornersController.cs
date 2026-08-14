using CornersPredictionApi.NewGenerationMl;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/ml/home-corners-2026")]
[Route("api/ml/corners/home")]
public sealed class NewGenerationHomeCornersController : ControllerBase
{
    private readonly NewGenerationPredictionService _service;
    private readonly ILogger<NewGenerationHomeCornersController> _logger;

    public NewGenerationHomeCornersController(
        NewGenerationPredictionService service,
        ILogger<NewGenerationHomeCornersController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("model-info")]
    [ProducesResponseType(typeof(NewGenerationModelInfo), StatusCodes.Status200OK)]
    public IActionResult ModelInfo() => Ok(_service.GetModelInfo());

    [HttpGet("health")]
    [ProducesResponseType(typeof(NewGenerationModelInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NewGenerationModelInfo), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetHealthAsync(cancellationToken));
        }
        catch (NewGenerationModelNotReadyException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new NewGenerationModelInfo
            {
                Status = "pending_artifacts",
                Ready = false,
                Loaded = false,
                Error = exception.Message
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            _logger.LogError(exception, "New-generation model health check failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new NewGenerationModelInfo
            {
                Status = "unhealthy",
                Ready = true,
                Loaded = false,
                Error = exception.Message
            });
        }
    }

    [HttpPost("predict")]
    [ProducesResponseType(typeof(NewGenerationPredictionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Predict(
        [FromBody] NewGenerationPredictionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }
        try
        {
            return Ok(await _service.PredictAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (NewGenerationModelNotReadyException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = exception.Message });
        }
        catch (TimeoutException exception)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = exception.Message });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            _logger.LogError(exception, "New-generation home-corners prediction failed");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }
}
