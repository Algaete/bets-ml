using System.Collections.Concurrent;
using CornersPrediction.Application.AutomatedCorners;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/automated-corners/general-picks")]
public sealed class AutomatedBotGeneralPicksController(
    IGetAutomatedBotGeneralPicksUseCase useCase,
    IGetGeneralPickLabUseCase labUseCase,
    IGeneralPickEvidenceRepository evidence,
    IGeneralPickManualSettlementRepository settlements,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
    ILogger<AutomatedBotGeneralPicksController> logger) : ControllerBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GeneralPickGates = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GeneralPickLabGates = new();
    private static long _generalPicksCacheVersion;

    [HttpGet("lab")]
    [ProducesResponseType(typeof(GeneralPickLab), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLab(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? marketFamily = null,
        [FromQuery] string? marketType = null,
        [FromQuery] string? botKey = null,
        [FromQuery] string? publicationStatus = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new AutomatedBotResearchFilterRequest(
                dateFrom, dateTo, marketFamily, marketType, botKey,
                "Approved", publicationStatus);
            var cacheKey = BuildLabCacheKey(request, Volatile.Read(ref _generalPicksCacheVersion));
            if (cache.TryGetValue<GeneralPickLab>(cacheKey, out var cached) && cached is not null)
                return Ok(cached);

            var gate = GeneralPickLabGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (cache.TryGetValue<GeneralPickLab>(cacheKey, out cached) && cached is not null)
                    return Ok(cached);
                var result = await labUseCase.GetAsync(request, cancellationToken);
                cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));
                return Ok(result);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to load the approved General Picks lab");
            return Problem(title: "Could not load the General Picks lab",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{evaluationId:long}/evidence")]
    public async Task<IActionResult> GetEvidence(long evaluationId, CancellationToken cancellationToken)
    {
        if (evaluationId <= 0) return NotFound();
        var result = await evidence.GetEvidenceAsync(evaluationId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{recordId:long}/settlement")]
    public async Task<IActionResult> Settle(long recordId,
        [FromBody] GeneralPickManualSettlementRequest request,
        [FromHeader(Name = "X-Acting-User")] string? actor,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await settlements.SettleAsync(recordId, request, actor ?? "", cancellationToken);
            cache.Remove("automated-bot-performance-scorecards-v1");
            Interlocked.Increment(ref _generalPicksCacheVersion);
            return Ok(result);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to manually settle general pick {RecordId}", recordId);
            return Problem(title: "No se pudo guardar la liquidación manual.", statusCode: 500);
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(AutomatedBotResearchPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? marketFamily = null,
        [FromQuery] string? marketType = null,
        [FromQuery] string? botKey = null,
        [FromQuery] string? modelDecision = null,
        [FromQuery] string? publicationStatus = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new AutomatedBotResearchFilterRequest(
                dateFrom, dateTo, marketFamily, marketType, botKey,
                modelDecision, publicationStatus, page, pageSize, sortBy, sortDirection);
            var cacheKey = BuildCacheKey(request, Volatile.Read(ref _generalPicksCacheVersion));
            if (cache.TryGetValue<AutomatedBotResearchPage>(cacheKey, out var cached) && cached is not null)
                return Ok(cached);

            // Open tabs often request the same page together. Run one SQL query
            // and let the others reuse it instead of competing for Azure I/O.
            var gate = GeneralPickGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (cache.TryGetValue<AutomatedBotResearchPage>(cacheKey, out cached) && cached is not null)
                    return Ok(cached);
                var result = await useCase.GetAsync(request, cancellationToken);
                cache.Set(cacheKey, result, TimeSpan.FromSeconds(30));
                return Ok(result);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to load general bot picks");
            return Problem(title: "Could not load general bot picks",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string BuildCacheKey(AutomatedBotResearchFilterRequest request, long version) => string.Join('|',
        "general-picks-v2", version,
        request.DateFrom?.Date.Ticks ?? 0, request.DateTo?.Date.Ticks ?? 0,
        request.MarketFamily?.Trim().ToUpperInvariant() ?? "",
        request.MarketType?.Trim().ToUpperInvariant() ?? "",
        request.BotKey?.Trim().ToUpperInvariant() ?? "",
        request.ModelDecision?.Trim().ToUpperInvariant() ?? "",
        request.PublicationStatus?.Trim().ToUpperInvariant() ?? "",
        request.Page, request.PageSize,
        request.SortBy?.Trim().ToUpperInvariant() ?? "MATCHDATE",
        request.SortDirection?.Trim().ToUpperInvariant() ?? "DESC");

    private static string BuildLabCacheKey(AutomatedBotResearchFilterRequest request, long version) => string.Join('|',
        "general-picks-lab-v1", version,
        request.DateFrom?.Date.Ticks ?? 0, request.DateTo?.Date.Ticks ?? 0,
        request.MarketFamily?.Trim().ToUpperInvariant() ?? "",
        request.MarketType?.Trim().ToUpperInvariant() ?? "",
        request.BotKey?.Trim().ToUpperInvariant() ?? "",
        request.PublicationStatus?.Trim().ToUpperInvariant() ?? "");
}
