using System.Text.Json;
using System.Text.RegularExpressions;
using CornersPrediction.Application.Automation.BotC;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation;

public static class RecommendationBotBaseStrategies
{
    public const string LegacyCurrent = "LEGACY_A";
    public const string LegacyConservative = "LEGACY_B";
    public const string LegacyEmpirical = "LEGACY_EMPIRICAL";
    public const string Models2026 = "MODELS_2026";
    public const string GoalsMarketAnchored = "GOALS_MARKET_ANCHORED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [LegacyCurrent, LegacyConservative, LegacyEmpirical, Models2026, GoalsMarketAnchored],
        StringComparer.OrdinalIgnoreCase);
}

public static class RecommendationBotLifecycle
{
    private static readonly IReadOnlySet<string> RetiredBotKeys = new HashSet<string>(
        ["B"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsRetired(string? botKey) =>
        !string.IsNullOrWhiteSpace(botKey) && RetiredBotKeys.Contains(botKey.Trim());

    public static bool IsShadowOnly(string? botKey) =>
        string.Equals(botKey?.Trim(), "H2026", StringComparison.OrdinalIgnoreCase);
}

public static class RecommendationBotFootballIntelligencePolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static FootballIntelligenceAdjustmentConfiguration FromJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new FootballIntelligenceAdjustmentConfiguration();

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !TryGetProperty(document.RootElement, "footballIntelligence", out var section)
                || section.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new FootballIntelligenceAdjustmentConfiguration();
            }

            var configuration = section.Deserialize<FootballIntelligenceAdjustmentConfiguration>(JsonOptions)
                ?? new FootballIntelligenceAdjustmentConfiguration();
            FootballIntelligenceAdjustmentCalculator.Validate(configuration);
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Football intelligence strategy configuration is not valid JSON.", exception);
        }
    }

    public static string? NormalizeLegacyJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Legacy strategy configuration must be a JSON object.");
        _ = FromJson(value);
        return document.RootElement.GetRawText();
    }

    public static FootballIntelligenceAdjustmentConfiguration FromBotG(
        BotGFootballIntelligenceConfiguration value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var configuration = new FootballIntelligenceAdjustmentConfiguration
        {
            Enabled = value.Enabled,
            Version = value.Version,
            Weight = value.Weight,
            MaximumProbabilityAdjustment = value.MaximumProbabilityAdjustment,
            MinimumTeamConfidence = value.MinimumTeamConfidence,
            MaximumSnapshotAgeMinutes = value.MaximumSnapshotAgeMinutes,
            MinimumActionableFacts = value.MinimumActionableFacts,
            MinimumIndependentSources = value.MinimumIndependentSources,
            AttackWeight = value.AttackWeight,
            DefenceWeight = value.DefenceWeight,
            WidthWeight = value.WidthWeight,
            SetPieceWeight = value.SetPieceWeight
        };
        FootballIntelligenceAdjustmentCalculator.Validate(configuration);
        return configuration;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

public sealed record RecommendationBotLeagueFilter(
    string MarketFamily,
    IReadOnlyList<string> IncludedLeagues,
    IReadOnlyList<string> ExcludedLeagues);

public sealed record RecommendationBotLeagueCatalogItem(
    string Country,
    string League,
    IReadOnlyList<string> Sources);

public static class RecommendationBotLeaguePolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> AllowedMarketFamilies = new HashSet<string>(
        ["*", "CORNERS", "GOALS", "SHOTS", "SOG"],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<RecommendationBotLeagueFilter> Normalize(
        IReadOnlyCollection<RecommendationBotLeagueFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return [];
        }

        var normalized = new List<RecommendationBotLeagueFilter>();
        foreach (var group in filters.GroupBy(
                     filter => NormalizeMarketFamily(filter.MarketFamily),
                     StringComparer.OrdinalIgnoreCase))
        {
            var included = NormalizeLeagues(group.SelectMany(filter => filter.IncludedLeagues ?? []));
            var excluded = NormalizeLeagues(group.SelectMany(filter => filter.ExcludedLeagues ?? []));
            if (included.Length == 0 && excluded.Length == 0)
            {
                continue;
            }

            normalized.Add(new RecommendationBotLeagueFilter(group.Key, included, excluded));
        }

        return normalized.OrderBy(filter => filter.MarketFamily, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsAllowed(
        IReadOnlyCollection<RecommendationBotLeagueFilter>? filters,
        string marketFamily,
        string league)
    {
        var applicable = (filters ?? [])
            .Where(filter => filter.MarketFamily.Equals("*", StringComparison.OrdinalIgnoreCase)
                || filter.MarketFamily.Equals(marketFamily, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (applicable.Length == 0)
        {
            return true;
        }

        var included = applicable.SelectMany(filter => filter.IncludedLeagues).ToArray();
        var excluded = applicable.SelectMany(filter => filter.ExcludedLeagues).ToArray();
        if (excluded.Any(rule => Matches(rule, league)))
        {
            return false;
        }

        return included.Length == 0 || included.Any(rule => Matches(rule, league));
    }

    public static string? ToJson(IReadOnlyCollection<RecommendationBotLeagueFilter>? filters)
    {
        if (filters is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(Normalize(filters), JsonOptions);
    }

    public static IReadOnlyList<RecommendationBotLeagueFilter> FromJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<RecommendationBotLeagueFilter[]>(value, JsonOptions));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Stored bot league filters are not valid JSON.", exception);
        }
    }

    private static string NormalizeMarketFamily(string value)
    {
        var marketFamily = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim().ToUpperInvariant();
        if (!AllowedMarketFamilies.Contains(marketFamily))
        {
            throw new ArgumentException($"Unsupported league-filter market family: {marketFamily}.");
        }

        return marketFamily;
    }

    private static string[] NormalizeLeagues(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => Regex.Replace(value.Trim(), @"\s+", " "))
        .Where(value => value.Length > 0)
        .Select(value => value.Length <= 200
            ? value
            : throw new ArgumentException("League filters cannot exceed 200 characters."))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool Matches(string rule, string league)
    {
        var effectiveLeague = Regex.Replace(league?.Trim() ?? string.Empty, @"\s+", " ");
        return rule.EndsWith('*')
            ? effectiveLeague.StartsWith(rule[..^1].TrimEnd(), StringComparison.OrdinalIgnoreCase)
            : effectiveLeague.Equals(rule, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RecommendationBotDefinitionDto(
    string BotKey,
    string DisplayName,
    string Description,
    string BaseStrategy,
    bool IsEnabled,
    bool PublishEnabled,
    bool IsBuiltIn,
    IReadOnlyList<string> MarketFamilies,
    double? MinEdge,
    double? MinExpectedValue,
    double? MinDistanceToLine,
    double? MaxContextDifference,
    bool? AllowModelDisagreement,
    double? MinOddsExclusive,
    double? MinProbabilityLiftOverImplied,
    decimal? StakeMultiplier,
    string? StrategyConfigurationJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public IReadOnlyList<RecommendationBotLeagueFilter> LeagueFilters { get; init; } = [];

    public bool IsLeagueAllowed(string marketFamily, string league) =>
        RecommendationBotLeaguePolicy.IsAllowed(LeagueFilters, marketFamily, league);

    public bool UsesPickSelector => BaseStrategy.Equals(
            RecommendationBotBaseStrategies.Models2026,
            StringComparison.OrdinalIgnoreCase)
        || BaseStrategy.Equals(
            RecommendationBotBaseStrategies.LegacyEmpirical,
            StringComparison.OrdinalIgnoreCase);

    public bool UsesNewGenerationModels => BaseStrategy.Equals(
        RecommendationBotBaseStrategies.Models2026,
        StringComparison.OrdinalIgnoreCase);

    public bool UsesBotG => BaseStrategy.Equals(
        RecommendationBotBaseStrategies.GoalsMarketAnchored,
        StringComparison.OrdinalIgnoreCase);

    public bool UsesMachineLearning => UsesPickSelector || UsesBotG;

    public FootballIntelligenceAdjustmentConfiguration FootballIntelligenceConfiguration =>
        UsesPickSelector
            ? SelectorConfiguration!.FootballIntelligence
            : UsesBotG
                ? RecommendationBotFootballIntelligencePolicy.FromBotG(
                    GoalsMarketAnchoredConfiguration!.FootballIntelligence)
                : RecommendationBotFootballIntelligencePolicy.FromJson(StrategyConfigurationJson);

    public BotGConfiguration? GoalsMarketAnchoredConfiguration => UsesBotG
        ? BuildBotGConfiguration()
        : null;

    public BotCStrategyConfiguration? SelectorConfiguration => UsesPickSelector
        ? BuildSelectorConfiguration()
        : null;

    public BotCStrategyManifest? StrategyManifest => UsesPickSelector
        ? BotCStrategyCatalog.Build(SelectorConfiguration!.ToJson())
        : null;

    private BotCStrategyConfiguration BuildSelectorConfiguration()
    {
        var stored = BotCStrategyConfiguration.FromJson(StrategyConfigurationJson);
        return BotCStrategyConfiguration.Validate(stored with
        {
            MinimumFinalEdge = MinEdge ?? stored.MinimumFinalEdge,
            MinimumFinalExpectedValue = MinExpectedValue ?? stored.MinimumFinalExpectedValue,
            MaximumBaseContextDistanceSigma = MaxContextDifference ?? stored.MaximumBaseContextDistanceSigma,
            MinimumOdds = MinOddsExclusive ?? stored.MinimumOdds
        });
    }

    private BotGConfiguration BuildBotGConfiguration()
    {
        var stored = BotGConfiguration.FromJson(StrategyConfigurationJson);
        return BotGConfiguration.Validate(stored with
        {
            BotKey = BotKey,
            Name = DisplayName,
            Stake = StakeMultiplier ?? stored.Stake
        });
    }
}

public sealed record SaveRecommendationBotDefinitionCommand(
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

public interface IRecommendationBotDefinitionRepository
{
    Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetByKeysAsync(
        IReadOnlyCollection<string> botKeys,
        CancellationToken cancellationToken);

    Task<RecommendationBotDefinitionDto?> GetAsync(string botKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationBotLeagueCatalogItem>> GetLeagueCatalogAsync(
        CancellationToken cancellationToken);

    Task<RecommendationBotDefinitionDto> UpsertAsync(
        SaveRecommendationBotDefinitionCommand command,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string botKey, CancellationToken cancellationToken);
}

public interface IRecommendationBotDefinitionsUseCase
{
    Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<RecommendationBotDefinitionDto?> GetAsync(string botKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationBotLeagueCatalogItem>> GetLeagueCatalogAsync(
        CancellationToken cancellationToken);

    Task<RecommendationBotDefinitionDto> SaveAsync(
        SaveRecommendationBotDefinitionCommand command,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string botKey, CancellationToken cancellationToken);
}

public sealed class RecommendationBotDefinitionsUseCase : IRecommendationBotDefinitionsUseCase
{
    private static readonly Regex BotKeyPattern = new("^[A-Z0-9][A-Z0-9_-]{0,49}$", RegexOptions.Compiled);
    private static readonly string[] DefaultMarketFamilies = ["CORNERS", "GOALS", "SHOTS", "SOG"];
    private static readonly IReadOnlySet<string> AllowedMarketFamilies = new HashSet<string>(
        DefaultMarketFamilies,
        StringComparer.OrdinalIgnoreCase);

    private readonly IRecommendationBotDefinitionRepository _repository;

    public RecommendationBotDefinitionsUseCase(IRecommendationBotDefinitionRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetAllAsync(CancellationToken cancellationToken) =>
        _repository.GetAllAsync(cancellationToken);

    public Task<RecommendationBotDefinitionDto?> GetAsync(
        string botKey,
        CancellationToken cancellationToken) =>
        _repository.GetAsync(NormalizeBotKey(botKey), cancellationToken);

    public Task<IReadOnlyList<RecommendationBotLeagueCatalogItem>> GetLeagueCatalogAsync(
        CancellationToken cancellationToken) =>
        _repository.GetLeagueCatalogAsync(cancellationToken);

    public Task<RecommendationBotDefinitionDto> SaveAsync(
        SaveRecommendationBotDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        var botKey = NormalizeBotKey(command.BotKey);
        if (command.IsEnabled && RecommendationBotLifecycle.IsRetired(botKey))
        {
            throw new ArgumentException($"Bot {botKey} is retired and cannot be enabled.");
        }
        if (command.PublishEnabled == true && RecommendationBotLifecycle.IsShadowOnly(botKey))
        {
            throw new ArgumentException($"Bot {botKey} is a shadow-only challenger and cannot publish.");
        }

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
            ? throw new ArgumentException("Bot display name is required.")
            : command.DisplayName.Trim();
        if (displayName.Length > 120)
        {
            throw new ArgumentException("Bot display name cannot exceed 120 characters.");
        }

        var baseStrategy = string.IsNullOrWhiteSpace(command.BaseStrategy)
            ? throw new ArgumentException("Base strategy is required.")
            : command.BaseStrategy.Trim().ToUpperInvariant();
        if (!RecommendationBotBaseStrategies.All.Contains(baseStrategy))
        {
            throw new ArgumentException("Base strategy must be LEGACY_A, LEGACY_B, LEGACY_EMPIRICAL, MODELS_2026 or GOALS_MARKET_ANCHORED.");
        }

        var isBotG = baseStrategy.Equals(
            RecommendationBotBaseStrategies.GoalsMarketAnchored,
            StringComparison.OrdinalIgnoreCase);
        var markets = (command.MarketFamilies is null || command.MarketFamilies.Count == 0
                ? isBotG ? ["GOALS"] : DefaultMarketFamilies
                : command.MarketFamilies)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var invalidMarkets = markets.Where(value => !AllowedMarketFamilies.Contains(value)).ToArray();
        if (invalidMarkets.Length > 0)
        {
            throw new ArgumentException($"Unsupported market families: {string.Join(", ", invalidMarkets)}.");
        }
        if (isBotG && (markets.Length != 1 || !markets[0].Equals("GOALS", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("GOALS_MARKET_ANCHORED supports only the GOALS market family.");
        }

        var leagueFilters = command.LeagueFilters is null
            ? null
            : RecommendationBotLeaguePolicy.Normalize(command.LeagueFilters);
        var invalidLeagueFilterMarkets = (leagueFilters ?? [])
            .Where(filter => filter.MarketFamily != "*" && !markets.Contains(filter.MarketFamily, StringComparer.OrdinalIgnoreCase))
            .Select(filter => filter.MarketFamily)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (invalidLeagueFilterMarkets.Length > 0)
        {
            throw new ArgumentException(
                $"League filters reference markets not enabled for this bot: {string.Join(", ", invalidLeagueFilterMarkets)}.");
        }

        ValidateRange(command.MinEdge, 0, 1, nameof(command.MinEdge));
        ValidateRange(command.MinExpectedValue, 0, 10, nameof(command.MinExpectedValue));
        ValidateRange(command.MinDistanceToLine, 0, 100, nameof(command.MinDistanceToLine));
        ValidateRange(command.MaxContextDifference, 0, 100, nameof(command.MaxContextDifference));
        ValidateRange(command.MinProbabilityLiftOverImplied, 0, 1, nameof(command.MinProbabilityLiftOverImplied));
        ValidateRange(command.MinOddsExclusive, 1, 100, nameof(command.MinOddsExclusive));
        if (command.StakeMultiplier is <= 0 or > 10)
        {
            throw new ArgumentException("StakeMultiplier must be greater than 0 and at most 10.");
        }

        var usesPickSelector = baseStrategy.Equals(RecommendationBotBaseStrategies.Models2026, StringComparison.OrdinalIgnoreCase)
            || baseStrategy.Equals(RecommendationBotBaseStrategies.LegacyEmpirical, StringComparison.OrdinalIgnoreCase);
        string? strategyConfigurationJson = null;
        if (usesPickSelector)
        {
            var selector = BotCStrategyConfiguration.FromJson(command.StrategyConfigurationJson);
            var expectedSource = baseStrategy.Equals(RecommendationBotBaseStrategies.LegacyEmpirical, StringComparison.OrdinalIgnoreCase)
                ? "LEGACY"
                : "MODELS_2026";
            if (!selector.BasePredictionSource.Equals(expectedSource, StringComparison.Ordinal))
            {
                throw new ArgumentException($"{baseStrategy} requires BasePredictionSource={expectedSource}.");
            }
            strategyConfigurationJson = selector.ToJson();
        }
        else if (isBotG)
        {
            var stored = BotGConfiguration.FromJson(command.StrategyConfigurationJson);
            var publishEnabled = command.PublishEnabled ?? stored.PublishEnabled;
            strategyConfigurationJson = BotGConfiguration.Validate(stored with
            {
                BotKey = botKey,
                Name = displayName,
                Enabled = command.IsEnabled,
                PublishEnabled = publishEnabled,
                ShadowMode = !publishEnabled,
                Stake = command.StakeMultiplier ?? stored.Stake
            }).ToJson();
        }
        else
        {
            strategyConfigurationJson = RecommendationBotFootballIntelligencePolicy.NormalizeLegacyJson(
                command.StrategyConfigurationJson);
        }

        return _repository.UpsertAsync(
            command with
            {
                BotKey = botKey,
                DisplayName = displayName,
                Description = command.Description?.Trim() ?? string.Empty,
                BaseStrategy = baseStrategy,
                MarketFamilies = markets,
                PublishEnabled = RecommendationBotLifecycle.IsShadowOnly(botKey)
                    ? false
                    : command.PublishEnabled ?? (isBotG
                        ? BotGConfiguration.FromJson(strategyConfigurationJson).PublishEnabled
                        : true),
                LeagueFilters = leagueFilters,
                StrategyConfigurationJson = strategyConfigurationJson
            },
            cancellationToken);
    }

    public Task<bool> DeleteAsync(string botKey, CancellationToken cancellationToken) =>
        _repository.DeleteAsync(NormalizeBotKey(botKey), cancellationToken);

    public static string NormalizeBotKey(string value)
    {
        var botKey = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Bot key is required.")
            : value.Trim().ToUpperInvariant();
        botKey = botKey switch
        {
            "C" => "C2026",
            "D" => "D2026",
            "E" => "E2026",
            "F" => "F2026",
            "G" => "G2026",
            "H" => "H2026",
            _ => botKey
        };
        if (!BotKeyPattern.IsMatch(botKey))
        {
            throw new ArgumentException("Bot key must contain only A-Z, 0-9, underscore or dash, up to 50 characters.");
        }

        return botKey;
    }

    private static void ValidateRange(double? value, double minimum, double maximum, string name)
    {
        if (value is not null && (value < minimum || value > maximum))
        {
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
        }
    }
}
