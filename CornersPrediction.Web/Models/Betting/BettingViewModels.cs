using System.ComponentModel.DataAnnotations;

namespace CornersPrediction.Web.Models.Betting;

public sealed class BettingIndexViewModel
{
    public BettingFiltersViewModel Filters { get; init; } = new();
    public string WorkingCurrencyCode { get; init; } = "CLP";
    public IReadOnlyList<BettingRecordViewModel> Records { get; init; } = Array.Empty<BettingRecordViewModel>();
    public IReadOnlyList<BankrollTransactionViewModel> BankrollTransactions { get; init; } =
        Array.Empty<BankrollTransactionViewModel>();
    public BettingSummaryViewModel Summary { get; init; } = new();
    public BankrollTransactionFormViewModel BankrollForm { get; init; } = new();
    public decimal CurrentBankroll { get; init; }
    public decimal PendingStake { get; init; }
    public decimal AvailableBankroll { get; init; }
}

public sealed class BettingFiltersViewModel
{
    public string? CurrencyCode { get; set; }
    public string? League { get; set; }
    public string? Season { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public string? Status { get; set; }
    public string? MarketType { get; set; }
    public string? Bookmaker { get; set; }
    [DataType(DataType.Date)]
    public DateTime? DateFrom { get; set; }
    [DataType(DataType.Date)]
    public DateTime? DateTo { get; set; }
}

public sealed class BettingRecordFormViewModel
{
    public long Id { get; set; }
    [Required]
    public string CurrencyCode { get; set; } = "CLP";
    [Required]
    public string League { get; set; } = string.Empty;
    [Required]
    public string Season { get; set; } = "2025-2026";
    [Required]
    [DataType(DataType.Date)]
    public DateTime MatchDate { get; set; } = DateTime.Today;
    [Required]
    public string HomeTeam { get; set; } = string.Empty;
    [Required]
    public string AwayTeam { get; set; } = string.Empty;
    public string? Bookmaker { get; set; }
    [Required]
    public string MarketType { get; set; } = "TotalCorners";
    [Required]
    public string BetSelection { get; set; } = "Over";
    [Range(0, 1000)]
    public decimal Line { get; set; }
    [Range(1.01, 1000)]
    public decimal Odds { get; set; }
    [Range(0.01, double.MaxValue)]
    public decimal Stake { get; set; }
    [Required]
    public string Status { get; set; } = "Pending";
    public int? ActualHomeCorners { get; set; }
    public int? ActualAwayCorners { get; set; }
    public int? ActualTotalCorners { get; set; }
    public int? ActualHomeShots { get; set; }
    public int? ActualAwayShots { get; set; }
    public int? ActualTotalShots { get; set; }
    public int? ActualHomeShotsOnGoal { get; set; }
    public int? ActualAwayShotsOnGoal { get; set; }
    public int? ActualTotalShotsOnGoal { get; set; }
    public decimal? CashoutAmount { get; set; }
    public decimal? BankrollBefore { get; set; }
    public decimal? ClosingOdds { get; set; }
    public string? ConfidenceLevel { get; set; }
    public string PredictionModel { get; set; } = "Manual";
    public string? Notes { get; set; }
    public bool AutoResolveStatus { get; set; }
    [Range(0, 100)]
    public decimal? EstimatedProbabilityPercent { get; set; }
    public string KellyStrategy { get; set; } = "None";
}

public sealed class BettingRecordViewModel
{
    public long Id { get; set; }
    public string CurrencyCode { get; set; } = "CLP";
    public string League { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public DateTime MatchDate { get; set; }
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string? Bookmaker { get; set; }
    public string MarketType { get; set; } = string.Empty;
    public string BetSelection { get; set; } = string.Empty;
    public decimal Line { get; set; }
    public decimal Odds { get; set; }
    public decimal Stake { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ActualHomeCorners { get; set; }
    public int? ActualAwayCorners { get; set; }
    public int? ActualTotalCorners { get; set; }
    public int? ActualHomeShots { get; set; }
    public int? ActualAwayShots { get; set; }
    public int? ActualTotalShots { get; set; }
    public int? ActualHomeShotsOnGoal { get; set; }
    public int? ActualAwayShotsOnGoal { get; set; }
    public int? ActualTotalShotsOnGoal { get; set; }
    public decimal? CashoutAmount { get; set; }
    public decimal PotentialReturn { get; set; }
    public decimal NetReturn { get; set; }
    public decimal ProfitLoss { get; set; }
    public decimal RoiPercent { get; set; }
    public decimal? BankrollBefore { get; set; }
    public decimal? BankrollAfter { get; set; }
    public decimal? ClosingOdds { get; set; }
    public string? ConfidenceLevel { get; set; }
    public string PredictionModel { get; set; } = "Manual";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class BettingSummaryViewModel
{
    public int TotalBets { get; set; }
    public int PendingBets { get; set; }
    public int WonBets { get; set; }
    public int LostBets { get; set; }
    public int VoidBets { get; set; }
    public int CashoutBets { get; set; }
    public decimal TotalStake { get; set; }
    public decimal TotalPotentialReturn { get; set; }
    public decimal TotalNetReturn { get; set; }
    public decimal TotalProfitLoss { get; set; }
    public decimal RoiPercent { get; set; }
    public decimal WinRatePercent { get; set; }
    public decimal AverageOdds { get; set; }
    public decimal AverageStake { get; set; }
    public decimal BestProfit { get; set; }
    public decimal WorstLoss { get; set; }
}

public sealed class BankrollTransactionFormViewModel
{
    [Required]
    public string CurrencyCode { get; set; } = "CLP";
    [Required]
    [DataType(DataType.Date)]
    public DateTime TransactionDate { get; set; } = DateTime.Today;
    [Required]
    public string Type { get; set; } = "Deposit";
    [Required]
    public decimal Amount { get; set; }
    public long? BettingRecordId { get; set; }
    public string? Notes { get; set; }
}

public sealed class BankrollTransactionViewModel
{
    public long Id { get; set; }
    public string CurrencyCode { get; set; } = "CLP";
    public DateTime TransactionDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public long? BettingRecordId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class BettingOptions
{
    public static readonly string[] Statuses = ["Pending", "Won", "Lost", "Void", "Cashout"];
    public static readonly string[] MarketTypes =
    [
        "TotalCorners",
        "HomeCorners",
        "AwayCorners",
        "FirstHalfCorners",
        "TotalShots",
        "HomeShots",
        "AwayShots",
        "TotalShotsOnGoal",
        "HomeShotsOnGoal",
        "AwayShotsOnGoal",
        "TotalGoals",
        "HomeGoals",
        "AwayGoals",
        "Other"
    ];
    public static readonly string[] BetSelections = ["Over", "Under", "Home", "Away", "Other"];
    public static readonly string[] ConfidenceLevels = ["Low", "Medium", "High", "VeryHigh"];
    public static readonly string[] PredictionModels =
    [
        "Manual",
        "TotalCornersModel",
        "OverUnderLineModel",
        "ShotsOnGoalModel",
        "GoalsModel",
        "AutomatedCornersBot",
        "AutomatedGoalsBot",
        "AutomatedSogBot",
        "AutomatedShotsBot",
        "AutomatedModels2026Bot"
    ];
    public static readonly string[] BankrollTransactionTypes = ["Deposit", "Withdrawal", "BetSettlement", "ManualAdjustment"];
    public static readonly string[] CurrencyCodes = ["CLP", "USD", "AUD"];
    public static readonly string[] KellyStrategies = ["None", "Kelly", "HalfKelly", "QuarterKelly"];

    public static string MarketTypeLabel(string marketType) => marketType switch
    {
        "TotalCorners" => "Total corners",
        "HomeCorners" => "Home corners",
        "AwayCorners" => "Away corners",
        "FirstHalfCorners" => "First half corners",
        "TotalShots" => "Total shots",
        "HomeShots" => "Home shots",
        "AwayShots" => "Away shots",
        "TotalShotsOnGoal" => "Total shots on goal (SOG)",
        "HomeShotsOnGoal" => "Home shots on goal (SOG)",
        "AwayShotsOnGoal" => "Away shots on goal (SOG)",
        "TotalGoals" => "Total goals",
        "HomeGoals" => "Home goals",
        "AwayGoals" => "Away goals",
        "Other" => "Other",
        _ => marketType
    };
}
