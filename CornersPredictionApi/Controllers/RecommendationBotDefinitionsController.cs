using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation;
using CornersPredictionApi.Requests;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/recommendation-bots")]
public sealed class RecommendationBotDefinitionsController : ControllerBase
{
    private readonly IRecommendationBotDefinitionsUseCase _useCase;
    private readonly SqlAutomationRepository _schemaRepository;

    public RecommendationBotDefinitionsController(
        IRecommendationBotDefinitionsUseCase useCase,
        SqlAutomationRepository schemaRepository)
    {
        _useCase = useCase;
        _schemaRepository = schemaRepository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RecommendationBotDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        await _schemaRepository.EnsureSchemaAsync(cancellationToken);
        return Ok(await _useCase.GetAllAsync(cancellationToken));
    }

    [HttpGet("{botKey}")]
    [ProducesResponseType(typeof(RecommendationBotDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string botKey, CancellationToken cancellationToken)
    {
        await _schemaRepository.EnsureSchemaAsync(cancellationToken);
        try
        {
            var bot = await _useCase.GetAsync(botKey, cancellationToken);
            return bot is null ? NotFound(new { error = $"Bot {botKey} was not found." }) : Ok(bot);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("league-catalog")]
    [ProducesResponseType(typeof(IReadOnlyList<RecommendationBotLeagueCatalogItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LeagueCatalog(CancellationToken cancellationToken)
    {
        await _schemaRepository.EnsureSchemaAsync(cancellationToken);
        return Ok(await _useCase.GetLeagueCatalogAsync(cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(RecommendationBotDefinitionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] SaveRecommendationBotDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        await _schemaRepository.EnsureSchemaAsync(cancellationToken);
        try
        {
            var existing = await _useCase.GetAsync(request.BotKey, cancellationToken);
            if (existing is not null)
            {
                return Conflict(new { error = $"Bot {existing.BotKey} already exists. Use PUT to update it." });
            }

            var saved = await _useCase.SaveAsync(ToCommand(request), cancellationToken);
            return CreatedAtAction(nameof(Get), new { botKey = saved.BotKey }, saved);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("{botKey}")]
    [ProducesResponseType(typeof(RecommendationBotDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        string botKey,
        [FromBody] SaveRecommendationBotDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        await _schemaRepository.EnsureSchemaAsync(cancellationToken);
        try
        {
            var normalizedRouteKey = RecommendationBotDefinitionsUseCase.NormalizeBotKey(botKey);
            var normalizedBodyKey = RecommendationBotDefinitionsUseCase.NormalizeBotKey(request.BotKey);
            if (!normalizedRouteKey.Equals(normalizedBodyKey, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Route bot key and body bot key must match." });
            }

            return Ok(await _useCase.SaveAsync(ToCommand(request), cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("{botKey}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string botKey, CancellationToken cancellationToken)
    {
        await _schemaRepository.EnsureSchemaAsync(cancellationToken);
        try
        {
            var existing = await _useCase.GetAsync(botKey, cancellationToken);
            if (existing is null)
            {
                return NotFound(new { error = $"Bot {botKey} was not found." });
            }

            if (existing.IsBuiltIn)
            {
                return BadRequest(new { error = "Built-in bots cannot be deleted; disable them instead." });
            }

            return await _useCase.DeleteAsync(botKey, cancellationToken)
                ? NoContent()
                : NotFound(new { error = $"Bot {botKey} was not found." });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private static SaveRecommendationBotDefinitionCommand ToCommand(
        SaveRecommendationBotDefinitionRequest request) =>
        new(
            request.BotKey,
            request.DisplayName,
            request.Description,
            request.BaseStrategy,
            request.IsEnabled,
            request.MarketFamilies,
            request.MinEdge,
            request.MinExpectedValue,
            request.MinDistanceToLine,
            request.MaxContextDifference,
            request.AllowModelDisagreement,
            request.MinOddsExclusive,
            request.MinProbabilityLiftOverImplied,
            request.StakeMultiplier,
            request.StrategyConfigurationJson,
            request.PublishEnabled,
            request.LeagueFilters);
}
