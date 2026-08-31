using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation.BotG;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/bot-g2026")]
public sealed class BotG2026Controller : ControllerBase
{
    private static readonly TimeSpan ScorecardCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ScorecardLocks = new();
    private readonly IBotGCandidateReadRepository _repository;
    private readonly SqlAutomationRepository _schemaRepository;
    private readonly AutomatedCornersSelectionService _automation;
    private readonly BotGArtifactRuntime _artifactRuntime;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BotG2026Controller> _logger;

    public BotG2026Controller(
        IBotGCandidateReadRepository repository,
        SqlAutomationRepository schemaRepository,
        AutomatedCornersSelectionService automation,
        BotGArtifactRuntime artifactRuntime,
        IMemoryCache cache,
        ILogger<BotG2026Controller> logger)
    {
        _repository = repository;
        _schemaRepository = schemaRepository;
        _automation = automation;
        _artifactRuntime = artifactRuntime;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(BotGArtifactStatus), StatusCodes.Status200OK)]
    public IActionResult Status() => Ok(_artifactRuntime.Status);

    [HttpPost("run")]
    [ProducesResponseType(typeof(AutomatedRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Run(
        [FromBody] BotG2026RunRequest? request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            var value = request ?? new BotG2026RunRequest();
            var response = await _automation.RunAsync(new RunAutomatedCornersRequest(
                DateFrom: value.DateFrom,
                DateTo: value.DateTo,
                Stake: 1m,
                MinEdge: null,
                MinExpectedValue: null,
                MinDistanceToLine: null,
                MaxContextDifference: null,
                DryRun: value.DryRun,
                AllowModelDisagreement: null,
                League: value.League,
                ExcludeExistingSelections: false,
                BatchNumber: value.BatchNumber,
                BatchSize: value.BatchSize,
                RunBotC: false,
                HistoricalBacktest: false,
                OnlyBotC: false,
                MarketFamilies: "GOALS",
                HistoricalBackfill: false,
                BotKeys: "G2026",
                RunAllEnabledBots: false), cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("candidates")]
    [HttpGet("results")]
    [ProducesResponseType(typeof(BotGCandidateAuditPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCandidates(
        [FromQuery] DateTime? dateFromUtc,
        [FromQuery] DateTime? dateToUtc,
        [FromQuery] string? decision,
        [FromQuery] string? publicationStatus,
        [FromQuery] string? marketType,
        [FromQuery] string? selection,
        [FromQuery] string? bookmaker,
        [FromQuery] string? configurationVersion,
        [FromQuery] string? result,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (dateFromUtc.HasValue && dateToUtc.HasValue && dateToUtc <= dateFromUtc)
            return BadRequest(new { error = "dateToUtc must be later than dateFromUtc." });
        if (page < 1 || pageSize is < 1 or > 1000)
            return BadRequest(new { error = "page must be positive and pageSize must be between 1 and 1000." });

        try
        {
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _repository.GetCandidatesAsync(
                new BotGCandidateAuditFilter(
                    dateFromUtc,
                    dateToUtc,
                    decision,
                    publicationStatus,
                    marketType,
                    selection,
                    bookmaker,
                    configurationVersion,
                    result,
                    page,
                    pageSize),
                cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot G2026 candidate audits");
            return Problem(
                title: "Could not load Bot G2026 candidates",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("candidates/{candidateId:long}")]
    [ProducesResponseType(typeof(BotGCandidateAuditDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCandidate(
        [FromRoute] long candidateId,
        CancellationToken cancellationToken = default)
    {
        if (candidateId <= 0)
            return BadRequest(new { error = "candidateId must be positive." });

        try
        {
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            var candidate = await _repository.GetCandidateAsync(candidateId, cancellationToken);
            return candidate is null ? NotFound() : Ok(candidate);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot G2026 candidate {CandidateId}", candidateId);
            return Problem(
                title: "Could not load Bot G2026 candidate detail",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("settle")]
    [ProducesResponseType(typeof(SettleBotG2026CandidatesResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Settle(
        [FromBody] SettleBotG2026Request? request,
        CancellationToken cancellationToken = default)
    {
        var value = request ?? new SettleBotG2026Request();
        if (value.MaximumCandidates is < 1 or > 50000)
            return BadRequest(new { error = "maximumCandidates must be between 1 and 50000." });
        if (value.OutcomeAvailableThroughUtc > DateTime.UtcNow.AddMinutes(1))
            return BadRequest(new { error = "outcomeAvailableThroughUtc cannot be in the future." });

        try
        {
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _repository.SettlePendingAsync(
                new SettleBotG2026CandidatesCommand(
                    value.OutcomeAvailableThroughUtc,
                    value.MaximumCandidates,
                    value.DryRun),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not settle pending Bot G2026 shadow candidates");
            return Problem(
                title: "Could not settle Bot G2026 candidates",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("scorecard")]
    [ProducesResponseType(typeof(IReadOnlyList<BotGScorecardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetScorecard(
        [FromQuery] DateTime? dateFromUtc,
        [FromQuery] DateTime? dateToUtc,
        [FromQuery] string? configurationVersion,
        CancellationToken cancellationToken = default)
    {
        if (dateFromUtc.HasValue && dateToUtc.HasValue && dateToUtc <= dateFromUtc)
            return BadRequest(new { error = "dateToUtc must be later than dateFromUtc." });

        try
        {
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            var filter = new BotGScorecardFilter(dateFromUtc, dateToUtc, configurationVersion);
            var cacheKey = BuildScorecardCacheKey(filter);
            var scorecard = await GetScorecardCachedAsync(cacheKey, filter, cancellationToken);
            Response.Headers.CacheControl = "private, max-age=30";
            return Ok(scorecard);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot G2026 scorecard");
            return Problem(
                title: "Could not load Bot G2026 scorecard",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<IReadOnlyList<BotGScorecardDto>> GetScorecardCachedAsync(
        string cacheKey,
        BotGScorecardFilter filter,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<IReadOnlyList<BotGScorecardDto>>(cacheKey, out var cached) && cached is not null)
            return cached;

        // MemoryCache factories are not single-flight. This per-key gate keeps
        // several open tabs from running the same 25-second SQL aggregate at
        // once and starving the candidate query.
        var gate = ScorecardLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<IReadOnlyList<BotGScorecardDto>>(cacheKey, out cached) && cached is not null)
                return cached;

            var scorecard = await _repository.GetScorecardAsync(filter, cancellationToken);
            _cache.Set(cacheKey, scorecard, ScorecardCacheDuration);
            return scorecard;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string BuildScorecardCacheKey(BotGScorecardFilter filter) => string.Join(
        ':',
        "bot-g2026-scorecard-v1",
        filter.DateFromUtc?.ToUniversalTime().Ticks.ToString() ?? "all",
        filter.DateToUtc?.ToUniversalTime().Ticks.ToString() ?? "all",
        filter.ConfigurationVersion?.Trim().ToUpperInvariant() ?? "all");
}

public sealed record BotG2026RunRequest(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? League = null,
    bool DryRun = false,
    int BatchNumber = 1,
    int BatchSize = 100);

public sealed record SettleBotG2026Request(
    DateTime? OutcomeAvailableThroughUtc = null,
    int MaximumCandidates = 5000,
    bool DryRun = false);
