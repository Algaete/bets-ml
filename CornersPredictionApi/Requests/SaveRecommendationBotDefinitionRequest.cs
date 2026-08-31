using CornersPrediction.Application.Automation;

namespace CornersPredictionApi.Requests;

public sealed record SaveRecommendationBotDefinitionRequest(
    string BotKey,
    string DisplayName,
    string? Description,
    string BaseStrategy,
    bool IsEnabled,
    IReadOnlyCollection<string>? MarketFamilies,
    double? MinEdge = null,
    double? MinExpectedValue = null,
    double? MinDistanceToLine = null,
    double? MaxContextDifference = null,
    bool? AllowModelDisagreement = null,
    double? MinOddsExclusive = null,
    double? MinProbabilityLiftOverImplied = null,
    decimal? StakeMultiplier = null,
    string? StrategyConfigurationJson = null,
    bool? PublishEnabled = null,
    IReadOnlyCollection<RecommendationBotLeagueFilter>? LeagueFilters = null);
