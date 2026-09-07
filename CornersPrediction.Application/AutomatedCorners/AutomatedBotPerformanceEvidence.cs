namespace CornersPrediction.Application.AutomatedCorners;

/// <summary>
/// A settled, pre-publication model decision that can be used to evaluate a
/// production segment. Implementations must select this evidence independently
/// of whether the candidate was eventually published.
/// </summary>
public sealed record AutomatedBotPerformanceEvidence
{
    public long EvidenceId { get; init; }
    public string EvidenceKey { get; init; } = string.Empty;
    public string BotKey { get; init; } = string.Empty;
    public string AutomationVersion { get; init; } = string.Empty;
    public long? ApiFootballFixtureId { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Bookmaker { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string SelectedSide { get; init; } = string.Empty;
    public decimal LineValue { get; init; }
    public decimal Odds { get; init; }
    public decimal StakeUnits { get; init; } = 1m;
    public string ModelDecision { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public decimal SettlementFactor { get; init; }
    public decimal ProfitLoss { get; init; }
    public decimal? ModelProbability { get; init; }
    public decimal? MarketProbability { get; init; }
    public decimal? ProbabilityEdge { get; init; }
    public DateTime DecisionAtUtc { get; init; }
    public DateTime OutcomeAvailableAtUtc { get; init; }
}

public interface IAutomatedBotPerformanceEvidenceRepository
{
    /// <summary>
    /// Returns safely settled scientific decisions in the fixture window. The
    /// query must not filter on publication status or published-selection id.
    /// </summary>
    Task<IReadOnlyList<AutomatedBotPerformanceEvidence>> GetSettledEvidenceAsync(
        DateTime fixtureFromUtc,
        DateTime fixtureToUtc,
        DateTime asOfUtc,
        CancellationToken cancellationToken);
}
