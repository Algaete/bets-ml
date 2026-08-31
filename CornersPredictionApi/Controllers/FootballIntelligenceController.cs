using CornersPrediction.Application.FootballIntelligence;
using CornersPredictionApi.FootballIntelligence;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/intelligence/fixtures")]
public sealed class FootballIntelligenceController : ControllerBase
{
    private readonly IMatchIntelligenceService _service;
    private readonly IIntelligenceSnapshotRepository _snapshotRepository;
    private readonly INewsDocumentRepository _documentRepository;
    private readonly INewsFactRepository _factRepository;
    private readonly FootballIntelligenceSchemaInitializer _schemaInitializer;
    private readonly ILogger<FootballIntelligenceController> _logger;

    public FootballIntelligenceController(
        IMatchIntelligenceService service,
        IIntelligenceSnapshotRepository snapshotRepository,
        INewsDocumentRepository documentRepository,
        INewsFactRepository factRepository,
        FootballIntelligenceSchemaInitializer schemaInitializer,
        ILogger<FootballIntelligenceController> logger)
    {
        _service = service;
        _snapshotRepository = snapshotRepository;
        _documentRepository = documentRepository;
        _factRepository = factRepository;
        _schemaInitializer = schemaInitializer;
        _logger = logger;
    }

    [HttpPost("{fixtureId:long}/run")]
    [ProducesResponseType(typeof(MatchIntelligenceResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Run(
        long fixtureId,
        [FromBody] RunFootballIntelligenceRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _schemaInitializer.EnsureReadyAsync(cancellationToken);
            var cutoff = request?.CutoffUtc ?? DateTime.UtcNow;
            return Ok(await _service.RunAsync(
                new RunMatchIntelligenceCommand(fixtureId, cutoff, request?.ForceRefresh ?? false),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Pre-match intelligence run failed for FixtureId={FixtureId}",
                fixtureId);
            return Problem(
                title: "Pre-match intelligence run failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{fixtureId:long}")]
    [HttpGet("{fixtureId:long}/latest")]
    [ProducesResponseType(typeof(MatchIntelligenceResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatest(
        long fixtureId,
        [FromQuery] DateTime? cutoffUtc,
        CancellationToken cancellationToken)
    {
        await _schemaInitializer.EnsureReadyAsync(cancellationToken);
        var result = await _service.GetLatestAsync(fixtureId, cutoffUtc ?? DateTime.UtcNow, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{fixtureId:long}/snapshots")]
    public async Task<IActionResult> GetSnapshots(long fixtureId, CancellationToken cancellationToken)
    {
        await _schemaInitializer.EnsureReadyAsync(cancellationToken);
        return Ok(await _snapshotRepository.GetHistoryAsync(fixtureId, cancellationToken));
    }

    [HttpGet("{fixtureId:long}/documents")]
    public async Task<IActionResult> GetDocuments(
        long fixtureId,
        [FromQuery] DateTime? cutoffUtc,
        CancellationToken cancellationToken)
    {
        await _schemaInitializer.EnsureReadyAsync(cancellationToken);
        var rows = await _documentRepository.GetByFixtureAsync(
            fixtureId,
            cutoffUtc ?? DateTime.UtcNow,
            cancellationToken);
        return Ok(rows.Select(row => new
        {
            row.Id,
            row.FixtureId,
            row.TeamId,
            row.Url,
            row.CanonicalUrl,
            row.SourceDomain,
            row.SourceTier,
            row.Title,
            row.Author,
            row.LanguageCode,
            row.PublishedAtUtc,
            row.UpdatedAtUtc,
            row.FirstSeenAtUtc,
            row.RetrievedAtUtc,
            row.ExtractionStatus,
            TextLength = row.NormalizedText?.Length ?? 0,
            row.ErrorMessage
        }));
    }

    [HttpGet("{fixtureId:long}/facts")]
    public async Task<IActionResult> GetFacts(
        long fixtureId,
        [FromQuery] DateTime? cutoffUtc,
        CancellationToken cancellationToken)
    {
        await _schemaInitializer.EnsureReadyAsync(cancellationToken);
        return Ok(await _factRepository.GetByFixtureAsync(
            fixtureId,
            cutoffUtc ?? DateTime.UtcNow,
            cancellationToken));
    }
}

public sealed record RunFootballIntelligenceRequest(
    DateTime? CutoffUtc = null,
    bool ForceRefresh = false);
