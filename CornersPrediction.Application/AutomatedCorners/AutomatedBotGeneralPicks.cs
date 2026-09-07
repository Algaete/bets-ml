namespace CornersPrediction.Application.AutomatedCorners;

public sealed record GeneralPickEvidence(
    string FeatureSnapshotJson,
    string ModelDecisionReasonsJson,
    string? ModelExplanation);

public interface IGeneralPickEvidenceRepository
{
    Task<GeneralPickEvidence?> GetEvidenceAsync(long evaluationId, CancellationToken cancellationToken);
}

public static class AutomatedBotGeneralPickSorting
{
    public static readonly IReadOnlyList<string> Columns = ["MatchDate", "BotKey", "MarketType",
        "ModelDecision", "PublicationStatus", "FinalProbability", "FinalEdge", "FinalExpectedValue",
        "SelectionScore", "SelectedOdds", "LineValue", "OutcomeStatus", "EvaluationId"];

    public static string NormalizeColumn(string? value) => string.IsNullOrWhiteSpace(value)
        ? "MatchDate"
        : Columns.FirstOrDefault(column => column.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("SortBy is not a supported column.");
}

public interface IAutomatedBotGeneralPicksRepository
{
    Task<AutomatedBotResearchPage> GetGeneralPicksAsync(
        AutomatedBotResearchQuery query,
        CancellationToken cancellationToken);
}

public interface IGetAutomatedBotGeneralPicksUseCase
{
    Task<AutomatedBotResearchPage> GetAsync(
        AutomatedBotResearchFilterRequest filters,
        CancellationToken cancellationToken);
}

public sealed record GeneralPickLabSummary
{
    public long ApprovedEvaluations { get; init; }
    public int IndependentSignals { get; init; }
    public int UniqueFixtures { get; init; }
    public int ResolvedSignals { get; init; }
    public int PendingSignals { get; init; }
    public int UnavailableSignals { get; init; }
    public int VoidSignals { get; init; }
    public decimal ProfitLossUnits { get; init; }
    public decimal? Yield { get; init; }
    public double? ObservedWinRate { get; init; }
    public double? AverageModelProbability { get; init; }
    public double? CalibrationGap { get; init; }
    public double? BrierScore { get; init; }
}

public sealed record GeneralPickLabDailyPoint(
    DateTime Date,
    int ResolvedSignals,
    decimal DailyProfitLossUnits,
    decimal CumulativeProfitLossUnits);

public sealed record GeneralPickLabCalibrationBin(
    decimal ProbabilityFrom,
    decimal ProbabilityTo,
    int Signals,
    double AverageModelProbability,
    double ObservedWinRate);

public sealed record GeneralPickLabSegment(
    string BotKey,
    string MarketType,
    int ResolvedSignals,
    decimal ProfitLossUnits,
    decimal? Yield,
    double? ObservedWinRate);

public sealed record GeneralPickLab(
    GeneralPickLabSummary Summary,
    IReadOnlyList<GeneralPickLabDailyPoint> Timeline,
    IReadOnlyList<GeneralPickLabCalibrationBin> Calibration,
    IReadOnlyList<GeneralPickLabSegment> Segments,
    DateTime GeneratedAtUtc);

public interface IGeneralPickLabRepository
{
    Task<GeneralPickLab> GetLabAsync(
        AutomatedBotResearchQuery query,
        CancellationToken cancellationToken);
}

public interface IGetGeneralPickLabUseCase
{
    Task<GeneralPickLab> GetAsync(
        AutomatedBotResearchFilterRequest filters,
        CancellationToken cancellationToken);
}

/// <summary>
/// Builds descriptive diagnostics from model-approved observations. Production
/// state remains an optional slice and never changes the scientific decision.
/// </summary>
public sealed class GetGeneralPickLabUseCase(IGeneralPickLabRepository repository)
    : IGetGeneralPickLabUseCase
{
    public Task<GeneralPickLab> GetAsync(
        AutomatedBotResearchFilterRequest filters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var query = GetAutomatedBotGeneralPicksUseCase.Normalize(filters with
        {
            ModelDecision = "Approved",
            Page = 1,
            PageSize = 1,
            SortBy = "MatchDate",
            SortDirection = "desc"
        });
        return repository.GetLabAsync(query, cancellationToken);
    }
}

/// <summary>
/// A read-only union of the candidate audit and published picks without an audit
/// entry. Production eligibility never controls visibility in this view.
/// </summary>
public sealed class GetAutomatedBotGeneralPicksUseCase(IAutomatedBotGeneralPicksRepository repository)
    : IGetAutomatedBotGeneralPicksUseCase
{
    public Task<AutomatedBotResearchPage> GetAsync(
        AutomatedBotResearchFilterRequest filters,
        CancellationToken cancellationToken) =>
        repository.GetGeneralPicksAsync(Normalize(filters), cancellationToken);

    public static AutomatedBotResearchQuery Normalize(AutomatedBotResearchFilterRequest filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var bot = string.IsNullOrWhiteSpace(filters.BotKey)
            ? null : filters.BotKey.Trim().ToUpperInvariant();
        if (bot is "C" or "D" or "E" or "F" or "H")
            bot += "2026";
        if (bot is not null && (bot.Length > 50
            || bot.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-' and not '.')))
            throw new ArgumentException("BotKey must contain at most 50 letters, digits, underscores, hyphens or dots.", nameof(filters));
        if (bot is "G" or "G2026" or "I" or "I2026")
            throw new ArgumentException("Bots G and I have independent laboratory views.", nameof(filters));

        var notRecorded = string.Equals(filters.ModelDecision?.Trim(),
            "NotRecorded", StringComparison.OrdinalIgnoreCase);
        var normalized = GetAutomatedBotResearchEvaluationsUseCase.Normalize(filters with
        {
            BotKey = null,
            ModelDecision = notRecorded ? null : filters.ModelDecision
        });
        return normalized with
        {
            BotKey = bot,
            ModelDecision = notRecorded ? "NotRecorded" : normalized.ModelDecision
        };
    }
}
