using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.AutomationBots;

/// <summary>
/// Lets an independently hosted bot describe itself to the administration catalog
/// without teaching the controller or the web page a list of bot keys.
/// </summary>
public interface IAutomationBotCatalogContributor
{
    IReadOnlyCollection<RecommendationBotDefinitionDto> GetActiveBots();
}

public static class AutomationBotCatalog
{
    public static IReadOnlyList<RecommendationBotDefinitionDto> Merge(
        IEnumerable<RecommendationBotDefinitionDto> maintainedDefinitions,
        IEnumerable<IAutomationBotCatalogContributor> contributors)
    {
        var catalog = maintainedDefinitions.ToDictionary(
            definition => definition.BotKey,
            StringComparer.OrdinalIgnoreCase);

        foreach (var bot in contributors.SelectMany(contributor => contributor.GetActiveBots()))
        {
            if (bot.IsEnabled)
            {
                catalog[bot.BotKey] = bot;
            }
        }

        return catalog.Values
            .OrderBy(definition => definition.BotKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class BotIAutomationBotCatalogContributor : IAutomationBotCatalogContributor
{
    private readonly BotIShadowCollectorOptions _options;

    public BotIAutomationBotCatalogContributor(IOptions<BotIShadowCollectorOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyCollection<RecommendationBotDefinitionDto> GetActiveBots()
    {
        if (!_options.Enabled)
        {
            return [];
        }

        var now = DateTime.UtcNow;
        return
        [
            new RecommendationBotDefinitionDto(
                "I2026",
                "Bot I · Market Movement Shadow",
                "Laboratorio independiente que estudia movimientos de cuota. Nunca publica picks ni apuestas reales.",
                "MARKET_MOVEMENT_SHADOW",
                IsEnabled: true,
                PublishEnabled: false,
                IsBuiltIn: true,
                MarketFamilies: ["CORNERS", "GOALS"],
                MinEdge: null,
                MinExpectedValue: null,
                MinDistanceToLine: null,
                MaxContextDifference: null,
                AllowModelDisagreement: null,
                MinOddsExclusive: null,
                MinProbabilityLiftOverImplied: null,
                StakeMultiplier: null,
                StrategyConfigurationJson: null,
                CreatedAtUtc: now,
                UpdatedAtUtc: now)
            {
                SupportsRecommendationJobs = false,
                CanEdit = false,
                CanClone = false,
                LifecycleLabel = "Shadow independiente · movimiento de mercado",
                RuntimeConfiguration = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Estado"] = "Activo",
                    ["Frecuencia"] = $"Cada {_options.PollMinutes} minutos",
                    ["Próximos partidos"] = $"{_options.FixtureLookAheadDays} días",
                    ["Máximo por ciclo"] = _options.MaximumFixtures.ToString(),
                    ["Publicación"] = "Bloqueada · sólo Shadow"
                }
            }
        ];
    }
}
