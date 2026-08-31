using CornersPrediction.Domain.FootballIntelligence;

namespace CornersPrediction.Application.FootballIntelligence;

public sealed class FootballIntelligenceOptions
{
    public const string SectionName = "FootballIntelligence";

    public bool Enabled { get; init; }
    public bool WorkerEnabled { get; init; }
    public int WorkerPollMinutes { get; init; } = 5;
    public int FixtureLookAheadHours { get; init; } = 72;
    public int MaxConcurrentFixtures { get; init; } = 3;
    public int ArticleMaxCharacters { get; init; } = 12_000;
    public int ArticleRelevantWindowCharacters { get; init; } = 800;
    public int MaximumQueriesPerTeam { get; init; } = 6;
    public int MaximumArticlesPerTeam { get; init; } = 8;
    public decimal MinFixtureRelevance { get; init; } = 0.60m;
    public decimal MinFactConfidence { get; init; } = 0.55m;
    public int NewsRecencyHalfLifeHours { get; init; } = 72;
    public int[] CutoffsMinutesBeforeKickoff { get; init; } = [4320, 1440, 360, 90, 40, 10];
    public PlayerImportanceOptions PlayerImportance { get; init; } = new();
    public SnapshotImpactOptions SnapshotImpact { get; init; } = new();
    public IReadOnlyDictionary<string, decimal> Sources { get; init; } = DefaultSourceWeights();

    public static IReadOnlyDictionary<string, decimal> DefaultSourceWeights() =>
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(NewsSourceTier.Official)] = 1.00m,
            [nameof(NewsSourceTier.StructuredProvider)] = 0.95m,
            [nameof(NewsSourceTier.MajorMedia)] = 0.85m,
            [nameof(NewsSourceTier.LocalJournalist)] = 0.75m,
            [nameof(NewsSourceTier.Aggregator)] = 0.55m,
            [nameof(NewsSourceTier.Unknown)] = 0.45m,
            [nameof(NewsSourceTier.Rumor)] = 0.25m
        };
}

public sealed class SnapshotImpactOptions
{
    public decimal GoalkeeperFallbackImportance { get; init; } = 0.55m;
    public decimal DefenderFallbackImportance { get; init; } = 0.55m;
    public decimal MidfielderFallbackImportance { get; init; } = 0.60m;
    public decimal AttackerFallbackImportance { get; init; } = 0.65m;
    public decimal UnknownPositionFallbackImportance { get; init; } = 0m;
    public decimal UnknownReplacementGap { get; init; } = 0.35m;
    public decimal DoubtfulUnavailability { get; init; } = 0.50m;
    public decimal ExpectedOutUnavailability { get; init; } = 0.80m;
    public decimal MaximumPlayerImpact { get; init; } = 0.25m;
}

public sealed class PlayerImportanceOptions
{
    public decimal StartRateWeight { get; init; } = 0.30m;
    public decimal MinutesShareWeight { get; init; } = 0.25m;
    public decimal RecentMinutesWeight { get; init; } = 0.20m;
    public decimal MarketContributionWeight { get; init; } = 0.15m;
    public decimal SetPieceWeight { get; init; } = 0.10m;
    public decimal ShrinkageStrength { get; init; } = 10m;
}

public sealed class NewsSearchOptions
{
    public const string SectionName = "FootballIntelligence:NewsSearch";
    public string Provider { get; init; } = "None";
    public string BaseUrl { get; init; } = "https://api.search.brave.com/res/v1/";
    public string? ApiKey { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public int MaximumResultsPerQuery { get; init; } = 10;
    public int MinimumRequestDelayMilliseconds { get; init; } = 250;
}

public sealed class NewsLlmOptions
{
    public const string SectionName = "FootballIntelligence:OpenAI";
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string? ApiKey { get; init; }
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = "football-news-v1";
    public int TimeoutSeconds { get; init; } = 60;
}
