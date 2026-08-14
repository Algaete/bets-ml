namespace CornersPrediction.Application.AutomatedCorners;

public sealed class AutomatedCornerSelectionDto
{
    public long AutomatedCornerBetSelectionId { get; init; }
    public Guid RunId { get; init; }
    public string AutomationVersion { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public long? MatchHistoryId { get; init; }
    public string? SourceUrl { get; init; }
    public DateTime MatchDate { get; init; }
    public DateTime? MatchDay { get; init; }
    public string League { get; init; } = string.Empty;
    public string? StandardizedLeague { get; init; }
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string? StandardizedHomeTeam { get; init; }
    public string? StandardizedAwayTeam { get; init; }
    public string SourceMarketType { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string SelectedSide { get; init; } = string.Empty;
    public decimal LineValue { get; init; }
    public decimal Odds { get; init; }
    public decimal Stake { get; init; }
    public decimal? FlatStake { get; init; }
    public decimal? KellyFraction { get; init; }
    public decimal? ImpliedProbability { get; init; }
    public decimal? ModelProbability { get; init; }
    public decimal? ProbabilityEdge { get; init; }
    public decimal? ExpectedValue { get; init; }
    public decimal? SelectionScore { get; init; }
    public decimal? PredictedTotalCorners { get; init; }
    public decimal? PredTotalDirect { get; init; }
    public decimal? PredHomeCorners { get; init; }
    public decimal? PredAwayCorners { get; init; }
    public decimal? PredTotalCombined { get; init; }
    public decimal? DistanceToLine { get; init; }
    public string? ConfidenceLevel { get; init; }
    public string? OverUnderConfidenceLevel { get; init; }
    public string? ModelConsensus { get; init; }
    public decimal? ContextTotalCorners { get; init; }
    public decimal? ContextDifference { get; init; }
    public string? RecommendedSide { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? ActualHomeCorners { get; init; }
    public int? ActualAwayCorners { get; init; }
    public int? ActualTotalCorners { get; init; }
    public int? SettlementActualValue { get; init; }
    public decimal? SettlementFactor { get; init; }
    public string? SettlementReason { get; init; }
    public string? SettlementSource { get; init; }
    public string? SettlementMatchStatus { get; init; }
    public string? LastSettlementCheckReason { get; init; }
    public DateTime? LastSettlementCheckAtUtc { get; init; }
    public decimal? ProfitLoss { get; init; }
    public decimal? YieldPct { get; init; }
    public string? DecisionReason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
}

public sealed record AutomatedCornerSelectionsFilterRequest(
    DateTime? DateFrom,
    DateTime? DateTo,
    string? Status,
    string? League,
    string? Source,
    string? MarketType,
    bool OnlyPending);

public sealed record UpdateAutomatedCornerSelectionStatusRequest(
    string Status,
    int? ActualHomeCorners,
    int? ActualAwayCorners,
    int? ActualTotalCorners);

public sealed record ResolveAutomatedCornerSelectionRequest(int ActualValue);

public sealed record LinkAutomatedCornerSelectionMatchRequest(
    long MatchHistoryId,
    long ApiFootballFixtureId);

public interface IAutomatedCornerSelectionsRepository
{
    Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync(
        AutomatedCornerSelectionsFilterRequest filters,
        CancellationToken cancellationToken);

    Task<AutomatedCornerSelectionDto> UpdateStatusAsync(
        long id,
        UpdateAutomatedCornerSelectionStatusRequest request,
        CancellationToken cancellationToken);

    Task<AutomatedCornerSelectionDto> ResolveAsync(
        long id,
        int actualValue,
        CancellationToken cancellationToken);

    Task<AutomatedCornerSelectionDto> LinkMatchAsync(
        long id,
        long matchHistoryId,
        long apiFootballFixtureId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
}

public interface IGetAutomatedCornerSelectionsUseCase
{
    Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetAsync(
        AutomatedCornerSelectionsFilterRequest filters,
        CancellationToken cancellationToken);
}

public interface IUpdateAutomatedCornerSelectionStatusUseCase
{
    Task<AutomatedCornerSelectionDto> UpdateAsync(
        long id,
        UpdateAutomatedCornerSelectionStatusRequest request,
        CancellationToken cancellationToken);
}

public interface IResolveAutomatedCornerSelectionUseCase
{
    Task<AutomatedCornerSelectionDto> ResolveAsync(
        long id,
        ResolveAutomatedCornerSelectionRequest request,
        CancellationToken cancellationToken);
}

public interface ILinkAutomatedCornerSelectionMatchUseCase
{
    Task<AutomatedCornerSelectionDto> LinkAsync(
        long id,
        LinkAutomatedCornerSelectionMatchRequest request,
        CancellationToken cancellationToken);
}

public interface IDeleteAutomatedCornerSelectionUseCase
{
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
}

public sealed class GetAutomatedCornerSelectionsUseCase : IGetAutomatedCornerSelectionsUseCase
{
    private static readonly HashSet<string> AllowedMarketTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TotalCorners",
        "HomeTeamCorners",
        "AwayTeamCorners",
        "TotalGoals",
        "HomeTeamGoals",
        "AwayTeamGoals",
        "TotalShots",
        "HomeTeamShots",
        "AwayTeamShots",
        "TotalShotsOnGoal",
        "HomeTeamShotsOnGoal",
        "AwayTeamShotsOnGoal"
    };

    private readonly IAutomatedCornerSelectionsRepository _repository;

    public GetAutomatedCornerSelectionsUseCase(IAutomatedCornerSelectionsRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetAsync(
        AutomatedCornerSelectionsFilterRequest filters,
        CancellationToken cancellationToken)
    {
        var normalizedFilters = Normalize(filters);
        return await _repository.GetSelectionsAsync(normalizedFilters, cancellationToken);
    }

    private static AutomatedCornerSelectionsFilterRequest Normalize(AutomatedCornerSelectionsFilterRequest filters)
    {
        var status = string.IsNullOrWhiteSpace(filters.Status)
            ? null
            : filters.Status.Trim();

        if (!string.IsNullOrWhiteSpace(status) && !AutomatedCornerSelectionStatusValidator.IsAllowed(status))
        {
            throw new ArgumentException("Status must be Pending, Won, Lost, Push or Void.");
        }

        var marketType = string.IsNullOrWhiteSpace(filters.MarketType)
            ? null
            : filters.MarketType.Trim();

        if (!string.IsNullOrWhiteSpace(marketType) && !AllowedMarketTypes.Contains(marketType))
        {
            throw new ArgumentException("Market type is not supported by the automated bot.");
        }

        var dateFrom = filters.DateFrom?.Date;
        var dateTo = filters.DateTo?.Date;
        if (dateFrom is not null && dateTo is not null && dateFrom > dateTo)
        {
            throw new ArgumentException("Date from cannot be greater than date to.");
        }

        return new AutomatedCornerSelectionsFilterRequest(
            dateFrom,
            dateTo,
            status,
            string.IsNullOrWhiteSpace(filters.League) ? null : filters.League.Trim(),
            string.IsNullOrWhiteSpace(filters.Source) ? null : filters.Source.Trim(),
            marketType,
            filters.OnlyPending);
    }
}

public sealed class UpdateAutomatedCornerSelectionStatusUseCase : IUpdateAutomatedCornerSelectionStatusUseCase
{
    private readonly IAutomatedCornerSelectionsRepository _repository;

    public UpdateAutomatedCornerSelectionStatusUseCase(IAutomatedCornerSelectionsRepository repository)
    {
        _repository = repository;
    }

    public async Task<AutomatedCornerSelectionDto> UpdateAsync(
        long id,
        UpdateAutomatedCornerSelectionStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Selection id must be greater than zero.");
        }

        var status = string.IsNullOrWhiteSpace(request.Status)
            ? throw new ArgumentException("Status is required.")
            : request.Status.Trim();

        if (!AutomatedCornerSelectionStatusValidator.IsAllowed(status))
        {
            throw new ArgumentException("Status must be Pending, Won, Lost, Push or Void.");
        }

        var actualTotalCorners = request.ActualTotalCorners;
        if (actualTotalCorners is null &&
            request.ActualHomeCorners is not null &&
            request.ActualAwayCorners is not null)
        {
            actualTotalCorners = request.ActualHomeCorners.Value + request.ActualAwayCorners.Value;
        }

        var normalizedRequest = new UpdateAutomatedCornerSelectionStatusRequest(
            status,
            request.ActualHomeCorners,
            request.ActualAwayCorners,
            actualTotalCorners);

        return await _repository.UpdateStatusAsync(id, normalizedRequest, cancellationToken);
    }
}

public sealed class ResolveAutomatedCornerSelectionUseCase : IResolveAutomatedCornerSelectionUseCase
{
    private readonly IAutomatedCornerSelectionsRepository _repository;

    public ResolveAutomatedCornerSelectionUseCase(IAutomatedCornerSelectionsRepository repository)
    {
        _repository = repository;
    }

    public Task<AutomatedCornerSelectionDto> ResolveAsync(
        long id,
        ResolveAutomatedCornerSelectionRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Selection id must be greater than zero.");
        }

        if (request.ActualValue < 0)
        {
            throw new ArgumentException("Actual result must be zero or greater.");
        }

        return _repository.ResolveAsync(id, request.ActualValue, cancellationToken);
    }
}

public sealed class LinkAutomatedCornerSelectionMatchUseCase : ILinkAutomatedCornerSelectionMatchUseCase
{
    private readonly IAutomatedCornerSelectionsRepository _repository;

    public LinkAutomatedCornerSelectionMatchUseCase(IAutomatedCornerSelectionsRepository repository)
    {
        _repository = repository;
    }

    public Task<AutomatedCornerSelectionDto> LinkAsync(
        long id,
        LinkAutomatedCornerSelectionMatchRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Selection id must be greater than zero.");
        }
        if (request.MatchHistoryId <= 0)
        {
            throw new ArgumentException("MatchHistoryId must be greater than zero.");
        }
        if (request.ApiFootballFixtureId <= 0)
        {
            throw new ArgumentException("ApiFootballFixtureId must be greater than zero.");
        }

        return _repository.LinkMatchAsync(
            id,
            request.MatchHistoryId,
            request.ApiFootballFixtureId,
            cancellationToken);
    }
}

public sealed class DeleteAutomatedCornerSelectionUseCase : IDeleteAutomatedCornerSelectionUseCase
{
    private readonly IAutomatedCornerSelectionsRepository _repository;

    public DeleteAutomatedCornerSelectionUseCase(IAutomatedCornerSelectionsRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Selection id must be greater than zero.");
        }

        return _repository.DeleteAsync(id, cancellationToken);
    }
}

internal static class AutomatedCornerSelectionStatusValidator
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pending",
        "Won",
        "Lost",
        "Push",
        "Void"
    };

    public static bool IsAllowed(string status)
    {
        return AllowedStatuses.Contains(status);
    }
}
