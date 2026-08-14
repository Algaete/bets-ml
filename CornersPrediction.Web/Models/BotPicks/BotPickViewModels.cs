namespace CornersPrediction.Web.Models.BotPicks;

public sealed class BotPicksIndexViewModel
{
    public BotPickFiltersViewModel Filters { get; init; } = new();

    public required BotPickMarketPageViewModel Market { get; init; }
}

public sealed record BotPickMarketPageViewModel(
    string Key,
    string Title,
    string Eyebrow,
    string Description,
    string UnitLabel,
    IReadOnlyList<BotPickMarketOptionViewModel> Options);

public sealed record BotPickMarketOptionViewModel(string Value, string Label);

public sealed class BotPickFiltersViewModel
{
    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public string? Status { get; set; }

    public string? League { get; set; }

    public string? Bookmaker { get; set; }

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

public sealed class UpdateBotPickStatusViewModel
{
    public string Status { get; set; } = string.Empty;

    public int? ActualHomeCorners { get; set; }

    public int? ActualAwayCorners { get; set; }

    public int? ActualTotalCorners { get; set; }
}

public sealed class ResolveBotPickViewModel
{
    public int ActualValue { get; set; }
}

public sealed class SettlePendingBotPicksViewModel
{
    public DateTime? MatchDateTo { get; set; }

    public int MaxRows { get; set; } = 20000;

    public string? BotKey { get; set; }

    public string? MarketFamily { get; set; }
}

public sealed class ReconcileAvailableBotPicksViewModel
{
    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public int MaxSelections { get; set; } = 20000;

    public bool DryRun { get; set; }
}

public sealed class ReconcileAvailableBotPicksResponseViewModel
{
    public DateTime? DateFrom { get; init; }
    public DateTime DateTo { get; init; }
    public bool DryRun { get; init; }
    public int InitialReviewed { get; init; }
    public int InitialSettled { get; init; }
    public int PendingAfterLocalSettlement { get; init; }
    public int FixtureDatesQueried { get; init; }
    public int FixturesDiscovered { get; init; }
    public int MatchedSelections { get; init; }
    public int UniqueMatchedFixtures { get; init; }
    public int SyncedFixtures { get; init; }
    public int LinkedSelections { get; init; }
    public int UnmatchedSelections { get; init; }
    public int AmbiguousSelections { get; init; }
    public int MissingMarketStatistics { get; init; }
    public int FinalReviewed { get; init; }
    public int FinalSettled { get; init; }
    public int FinalWon { get; init; }
    public int FinalLost { get; init; }
    public int FinalPush { get; init; }
    public int StillPending { get; init; }
    public string? DailyRemaining { get; init; }
    public string? MinuteRemaining { get; init; }
}

public sealed class BotPickSettlementResponseViewModel
{
    public DateTime? MatchDateTo { get; init; }
    public bool DryRun { get; init; }
    public int ReviewedRows { get; init; }
    public int SettledRows { get; init; }
    public int StillPendingRows { get; init; }
    public int WonRows { get; init; }
    public int LostRows { get; init; }
    public int PushRows { get; init; }
    public int AppliedRows { get; init; }
    public int ConcurrentlySkippedRows { get; init; }
    public string? BotKey { get; init; }
    public string? MarketFamily { get; init; }
    public IReadOnlyList<BotPickSettlementItemViewModel> Items { get; init; } = [];
}

public sealed class BotPickSettlementItemViewModel
{
    public long SelectionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class BotPickMonthlySummaryViewModel
{
    public DateTime Month { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string BotLabel { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Won { get; init; }
    public int Lost { get; init; }
    public int Push { get; init; }
    public int Void { get; init; }
    public decimal ProfitLoss { get; init; }
    public decimal SettledStake { get; init; }
    public decimal? YieldPct { get; init; }
}
