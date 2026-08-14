using System.Text.RegularExpressions;
using CornersPrediction.Application.Automation.BotC;

namespace CornersPrediction.Application.Automation;

public static class RecommendationBotBaseStrategies
{
    public const string LegacyCurrent = "LEGACY_A";
    public const string LegacyConservative = "LEGACY_B";
    public const string LegacyEmpirical = "LEGACY_EMPIRICAL";
    public const string Models2026 = "MODELS_2026";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [LegacyCurrent, LegacyConservative, LegacyEmpirical, Models2026],
        StringComparer.OrdinalIgnoreCase);
}

public sealed record RecommendationBotDefinitionDto(
    string BotKey,
    string DisplayName,
    string Description,
    string BaseStrategy,
    bool IsEnabled,
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
    public bool UsesPickSelector => BaseStrategy.Equals(
            RecommendationBotBaseStrategies.Models2026,
            StringComparison.OrdinalIgnoreCase)
        || BaseStrategy.Equals(
            RecommendationBotBaseStrategies.LegacyEmpirical,
            StringComparison.OrdinalIgnoreCase);

    public bool UsesNewGenerationModels => BaseStrategy.Equals(
        RecommendationBotBaseStrategies.Models2026,
        StringComparison.OrdinalIgnoreCase);

    public bool UsesMachineLearning => UsesPickSelector;

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
    string? StrategyConfigurationJson = null);

public interface IRecommendationBotDefinitionRepository
{
    Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetByKeysAsync(
        IReadOnlyCollection<string> botKeys,
        CancellationToken cancellationToken);

    Task<RecommendationBotDefinitionDto?> GetAsync(string botKey, CancellationToken cancellationToken);

    Task<RecommendationBotDefinitionDto> UpsertAsync(
        SaveRecommendationBotDefinitionCommand command,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string botKey, CancellationToken cancellationToken);
}

public interface IRecommendationBotDefinitionsUseCase
{
    Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<RecommendationBotDefinitionDto?> GetAsync(string botKey, CancellationToken cancellationToken);

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

    public Task<RecommendationBotDefinitionDto> SaveAsync(
        SaveRecommendationBotDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        var botKey = NormalizeBotKey(command.BotKey);
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
            throw new ArgumentException("Base strategy must be LEGACY_A, LEGACY_B, LEGACY_EMPIRICAL or MODELS_2026.");
        }

        var markets = (command.MarketFamilies is null || command.MarketFamilies.Count == 0
                ? DefaultMarketFamilies
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

        return _repository.UpsertAsync(
            command with
            {
                BotKey = botKey,
                DisplayName = displayName,
                Description = command.Description?.Trim() ?? string.Empty,
                BaseStrategy = baseStrategy,
                MarketFamilies = markets,
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
