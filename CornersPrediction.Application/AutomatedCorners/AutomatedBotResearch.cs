using System.Text.Json;

namespace CornersPrediction.Application.AutomatedCorners;

/// <summary>
/// Filters the complete candidate audit. Dates are inclusive calendar dates;
/// publication filters never change which candidates are persisted.
/// </summary>
public sealed record AutomatedBotResearchFilterRequest(
    DateTime? DateFrom,
    DateTime? DateTo,
    string? MarketFamily,
    string? MarketType,
    string? BotKey,
    string? ModelDecision,
    string? PublicationStatus,
    int Page = 1,
    int PageSize = 100,
    string? SortBy = null,
    string? SortDirection = null);

/// <summary>
/// Validated repository query. DateToExclusiveUtc makes the public dateTo filter
/// inclusive without applying functions to MatchDate in SQL Server.
/// </summary>
public sealed record AutomatedBotResearchQuery(
    DateTime? DateFromUtc,
    DateTime? DateToExclusiveUtc,
    string? MarketFamily,
    string? MarketType,
    string? BotKey,
    string? ModelDecision,
    string? PublicationStatus,
    int Page,
    int PageSize,
    string SortBy = "MatchDate",
    string SortDirection = "desc");

public sealed record AutomatedBotResearchEvaluationDto
{
    public string RecordKind { get; init; } = "Evaluation";
    public long EvaluationId { get; init; }
    public Guid RunId { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string AutomationVersion { get; init; } = string.Empty;
    public DateTime MatchDate { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string MarketFamily { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public decimal LineValue { get; init; }
    public string? SelectedSide { get; init; }
    public decimal? SelectedOdds { get; init; }

    // The model decision and the production decision are intentionally separate.
    public string ModelDecision { get; init; } = string.Empty;
    public IReadOnlyList<string> ModelDecisionReasons { get; init; } = [];
    public string? ModelExplanation { get; init; }
    public string PublicationStatus { get; init; } = string.Empty;
    public string ProductionDecision { get; init; } = string.Empty;
    public string? ProductionReason { get; init; }
    public bool IsResearchWinner { get; init; }
    public long? PublishedSelectionId { get; init; }

    public decimal? FinalProbability { get; init; }
    public decimal? MarketProbability { get; init; }
    public decimal? FinalEdge { get; init; }
    public decimal? FinalExpectedValue { get; init; }
    public decimal? SelectionScore { get; init; }
    public decimal? DataQualityScore { get; init; }
    public decimal? ContextAgreementScore { get; init; }
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public string FeatureSnapshotJson { get; init; } = "{}";

    /// <summary>
    /// Pending/Unavailable when official data cannot be used; otherwise Official
    /// (no selectable side) or the Asian result Win/HalfWin/Push/HalfLoss/Loss.
    /// ProfitLoss is the comparable one-unit return for every candidate.
    /// </summary>
    public string OutcomeStatus { get; init; } = "Pending";
    public decimal? ActualValue { get; init; }
    public decimal? ProfitLoss { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public string? OutcomeSource { get; init; }
    public string? ManualSettlementReason { get; init; }
    public string? ManualSettledBy { get; init; }
}

public sealed record AutomatedBotResearchPage(
    IReadOnlyList<AutomatedBotResearchEvaluationDto> Items,
    long TotalCount,
    int Page,
    int PageSize,
    int TotalPages)
{
    public IReadOnlyList<AutomatedBotGeneralPickBotDto> AvailableBots { get; init; } = [];
}

public sealed record AutomatedBotGeneralPickBotDto(string BotKey, string DisplayName);

public interface IAutomatedBotResearchRepository
{
    Task<AutomatedBotResearchPage> GetEvaluationsAsync(
        AutomatedBotResearchQuery query,
        CancellationToken cancellationToken);
}

public interface IGetAutomatedBotResearchEvaluationsUseCase
{
    Task<AutomatedBotResearchPage> GetAsync(
        AutomatedBotResearchFilterRequest filters,
        CancellationToken cancellationToken);
}

public sealed class GetAutomatedBotResearchEvaluationsUseCase
    : IGetAutomatedBotResearchEvaluationsUseCase
{
    public const int MaximumPageSize = 500;

    private static readonly IReadOnlyDictionary<string, string[]> MarketsByFamily =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CORNERS"] = ["TotalCorners", "HomeTeamCorners", "AwayTeamCorners"],
            ["GOALS"] = ["TotalGoals", "HomeTeamGoals", "AwayTeamGoals"],
            ["SHOTS"] = ["TotalShots", "HomeTeamShots", "AwayTeamShots"],
            ["SOG"] = ["TotalShotsOnGoal", "HomeTeamShotsOnGoal", "AwayTeamShotsOnGoal"]
        };

    private static readonly HashSet<string> ModelDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Approved", "Rejected", "Abstain", "PendingData", "Invalid"
    };

    private static readonly HashSet<string> PublicationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Published", "ModelRejected", "PendingData", "Eligible", "ProductionBlocked",
        "Shadow", "NotSelected", "NotPublished"
    };

    private static readonly IReadOnlyDictionary<string, string> Bots =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["C"] = "C2026",
            ["C2026"] = "C2026",
            ["D"] = "D2026",
            ["D2026"] = "D2026",
            ["E"] = "E2026",
            ["E2026"] = "E2026",
            ["F"] = "F2026",
            ["F2026"] = "F2026",
            ["H"] = "H2026",
            ["H2026"] = "H2026"
        };

    private readonly IAutomatedBotResearchRepository _repository;

    public GetAutomatedBotResearchEvaluationsUseCase(
        IAutomatedBotResearchRepository repository)
    {
        _repository = repository;
    }

    public Task<AutomatedBotResearchPage> GetAsync(
        AutomatedBotResearchFilterRequest filters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return _repository.GetEvaluationsAsync(Normalize(filters), cancellationToken);
    }

    public static AutomatedBotResearchQuery Normalize(
        AutomatedBotResearchFilterRequest filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (filters.Page < 1)
            throw new ArgumentException("Page must be greater than zero.", nameof(filters));
        if (filters.PageSize is < 1 or > MaximumPageSize)
            throw new ArgumentException(
                $"PageSize must be between 1 and {MaximumPageSize}.",
                nameof(filters));

        DateTime? from = filters.DateFrom.HasValue
            ? AsUtcDate(filters.DateFrom.Value)
            : null;
        DateTime? inclusiveTo = filters.DateTo.HasValue
            ? AsUtcDate(filters.DateTo.Value)
            : null;
        if (from.HasValue && inclusiveTo.HasValue && from > inclusiveTo)
            throw new ArgumentException("DateFrom cannot be after DateTo.", nameof(filters));
        if (inclusiveTo == DateTime.SpecifyKind(DateTime.MaxValue.Date, DateTimeKind.Utc))
            throw new ArgumentException("DateTo is outside the supported range.", nameof(filters));
        var toExclusive = inclusiveTo?.AddDays(1);

        var family = NormalizeOptional(filters.MarketFamily)?.ToUpperInvariant();
        if (family is not null && !MarketsByFamily.ContainsKey(family))
            throw new ArgumentException(
                "MarketFamily must be CORNERS, GOALS, SHOTS or SOG.",
                nameof(filters));

        var market = NormalizeOptional(filters.MarketType);
        if (market is not null)
        {
            var canonicalMarket = MarketsByFamily.Values
                .SelectMany(value => value)
                .FirstOrDefault(value => value.Equals(market, StringComparison.OrdinalIgnoreCase));
            if (canonicalMarket is null)
                throw new ArgumentException("MarketType is not supported.", nameof(filters));
            market = canonicalMarket;

            var inferredFamily = MarketsByFamily.Single(pair => pair.Value.Contains(market)).Key;
            if (family is not null && !family.Equals(inferredFamily, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"MarketType '{market}' does not belong to {family}.",
                    nameof(filters));
        }

        var bot = NormalizeOptional(filters.BotKey);
        if (bot is not null)
        {
            if (!Bots.TryGetValue(bot, out var canonicalBot))
                throw new ArgumentException("BotKey must be C, D, E, F or H (2026 aliases are accepted).", nameof(filters));
            bot = canonicalBot;
        }

        var decision = Canonical(filters.ModelDecision, ModelDecisions, "ModelDecision");
        var publication = Canonical(
            filters.PublicationStatus,
            PublicationStatuses,
            "PublicationStatus");

        var sortBy = AutomatedBotGeneralPickSorting.NormalizeColumn(filters.SortBy);
        var direction = string.IsNullOrWhiteSpace(filters.SortDirection) ? "desc" : filters.SortDirection.Trim().ToLowerInvariant();
        if (direction is not ("asc" or "desc"))
            throw new ArgumentException("SortDirection must be asc or desc.", nameof(filters));
        return new AutomatedBotResearchQuery(
            from,
            toExclusive,
            family,
            market,
            bot,
            decision,
            publication,
            filters.Page,
            filters.PageSize,
            sortBy,
            direction);
    }

    public static IReadOnlyList<string> ParseDecisionReasons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return document.RootElement
                .EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.GetRawText())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }
        catch (JsonException)
        {
            // A legacy malformed audit payload must not break the whole page.
            return [];
        }
    }

    private static string? Canonical(
        string? value,
        IEnumerable<string> allowed,
        string parameterName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
            return null;
        var canonical = allowed.FirstOrDefault(item =>
            item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return canonical ?? throw new ArgumentException(
            $"{parameterName} '{normalized}' is not supported.",
            parameterName);
    }

    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
