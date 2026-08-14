using CornersPredictionApi.NewGenerationMl;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/ml/models-2026")]
public sealed class NewGenerationModelsController : ControllerBase
{
    private readonly NewGenerationPredictionService _service;
    private readonly ILogger<NewGenerationModelsController> _logger;

    public NewGenerationModelsController(
        NewGenerationPredictionService service,
        ILogger<NewGenerationModelsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("model-info")]
    [ProducesResponseType(typeof(NewGenerationModelCatalogInfo), StatusCodes.Status200OK)]
    public IActionResult ModelInfo() => Ok(_service.GetCatalogInfo());

    [HttpGet("health")]
    [ProducesResponseType(typeof(NewGenerationModelCatalogInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NewGenerationModelCatalogInfo), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetCatalogHealthAsync(cancellationToken));
        }
        catch (NewGenerationModelNotReadyException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new NewGenerationModelCatalogInfo
            {
                Status = "pending_artifacts",
                Ready = false,
                Available = false,
                TotalModels = NewGenerationModelDefinitions.All.Count,
                Error = exception.Message
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            _logger.LogError(exception, "Models 2026 health check failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new NewGenerationModelCatalogInfo
            {
                Status = "unhealthy",
                Ready = false,
                Available = true,
                TotalModels = NewGenerationModelDefinitions.All.Count,
                Error = exception.Message
            });
        }
    }

    [HttpPost("predict")]
    [ProducesResponseType(typeof(NewGenerationBatchPredictionResult), StatusCodes.Status200OK)]
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
            return Ok(await _service.PredictAllAsync(request, cancellationToken));
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
            _logger.LogError(exception, "Models 2026 batch prediction failed");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }
}
