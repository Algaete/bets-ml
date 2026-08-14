using CornersPrediction.Application.Automation;

namespace CornersPredictionApi.Requests;

public sealed record CreateRecommendationJobRequest(
    DateOnly DateFrom,
    DateOnly DateTo,
    string? Name = null,
    IReadOnlyCollection<string>? BotKeys = null,
    IReadOnlyCollection<string>? MarketFamilies = null,
    string Mode = RecommendationJobModes.HistoricalBackfill,
    int BatchSize = 25,
    int MaxAttempts = 3);
