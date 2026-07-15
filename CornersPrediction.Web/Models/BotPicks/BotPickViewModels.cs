namespace CornersPrediction.Web.Models.BotPicks;

public sealed class BotPicksIndexViewModel
{
    public BotPickFiltersViewModel Filters { get; init; } = new();
}

public sealed class BotPickFiltersViewModel
{
    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public string? Status { get; set; }

    public string? League { get; set; }

    public string? MarketType { get; set; }

    public bool OnlyPending { get; set; }
}

public sealed class BotPickSelectionViewModel
{
    public long AutomatedCornerBetSelectionId { get; init; }
    public Guid RunId { get; init; }
    public string AutomationVersion { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
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
    public decimal? ProfitLoss { get; init; }
    public decimal? YieldPct { get; init; }
    public string? DecisionReason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
}

public sealed class UpdateBotPickStatusViewModel
{
    public string Status { get; set; } = string.Empty;

    public int? ActualHomeCorners { get; set; }

    public int? ActualAwayCorners { get; set; }

    public int? ActualTotalCorners { get; set; }
}
