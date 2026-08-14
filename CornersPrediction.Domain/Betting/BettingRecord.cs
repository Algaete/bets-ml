namespace CornersPrediction.Domain.Betting;

public sealed class BettingRecord
{
    public long Id { get; set; }
    public required string UserId { get; set; } = BettingUsers.DefaultUserId;
    public required string CurrencyCode { get; set; } = BettingCurrencies.Clp;
    public required string League { get; set; }
    public required string Season { get; set; }
    public DateTime MatchDate { get; set; }
    public required string HomeTeam { get; set; }
    public required string AwayTeam { get; set; }
    public string? Bookmaker { get; set; }
    public required string MarketType { get; set; }
    public required string BetSelection { get; set; }
    public decimal Line { get; set; }
    public decimal Odds { get; set; }
    public decimal Stake { get; set; }
    public required string Status { get; set; }
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
    public string PredictionModel { get; set; } = BetPredictionModels.Manual;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    public void CalculateFinancials(bool autoResolveStatus = false)
    {
        if (ActualHomeCorners.HasValue && ActualAwayCorners.HasValue)
        {
            ActualTotalCorners = ActualHomeCorners.Value + ActualAwayCorners.Value;
        }

        if (ActualHomeShots.HasValue && ActualAwayShots.HasValue)
        {
            ActualTotalShots = ActualHomeShots.Value + ActualAwayShots.Value;
        }

        if (ActualHomeShotsOnGoal.HasValue && ActualAwayShotsOnGoal.HasValue)
        {
            ActualTotalShotsOnGoal = ActualHomeShotsOnGoal.Value + ActualAwayShotsOnGoal.Value;
        }

        if (autoResolveStatus)
        {
            ResolveStatusFromMarketResult();
        }

        PotentialReturn = Stake * Odds;

        switch (Status)
        {
            case BetStatuses.Won:
                NetReturn = Stake * Odds;
                ProfitLoss = NetReturn - Stake;
                break;
            case BetStatuses.Lost:
                NetReturn = 0;
                ProfitLoss = -Stake;
                break;
            case BetStatuses.Void:
                NetReturn = Stake;
                ProfitLoss = 0;
                break;
            case BetStatuses.Cashout:
                NetReturn = CashoutAmount ?? 0;
                ProfitLoss = NetReturn - Stake;
                break;
            default:
                NetReturn = 0;
                ProfitLoss = 0;
                break;
        }

        RoiPercent = Stake > 0 ? ProfitLoss / Stake * 100 : 0;
        BankrollAfter = BankrollBefore.HasValue ? BankrollBefore.Value + ProfitLoss : null;
    }

    private void ResolveStatusFromMarketResult()
    {
        if (BetSelection is not (BetSelections.Over or BetSelections.Under))
        {
            return;
        }

        var actual = MarketType switch
        {
            BetMarketTypes.TotalCorners => ActualTotalCorners,
            BetMarketTypes.TotalShots => ActualTotalShots,
            BetMarketTypes.TotalShotsOnGoal => ActualTotalShotsOnGoal,
            _ => null
        };

        if (actual is null)
        {
            return;
        }

        Status = BetSelection switch
        {
            BetSelections.Over when actual.Value > Line => BetStatuses.Won,
            BetSelections.Over when actual.Value < Line => BetStatuses.Lost,
            BetSelections.Under when actual.Value < Line => BetStatuses.Won,
            BetSelections.Under when actual.Value > Line => BetStatuses.Lost,
            _ => BetStatuses.Void
        };
    }
}

public static class BetStatuses
{
    public const string Pending = "Pending";
    public const string Won = "Won";
    public const string Lost = "Lost";
    public const string Void = "Void";
    public const string Cashout = "Cashout";

    public static readonly string[] All = [Pending, Won, Lost, Void, Cashout];
}

public static class BetMarketTypes
{
    public const string TotalCorners = "TotalCorners";
    public const string HomeCorners = "HomeCorners";
    public const string AwayCorners = "AwayCorners";
    public const string FirstHalfCorners = "FirstHalfCorners";
    public const string TotalShots = "TotalShots";
    public const string HomeShots = "HomeShots";
    public const string AwayShots = "AwayShots";
    public const string TotalShotsOnGoal = "TotalShotsOnGoal";
    public const string HomeShotsOnGoal = "HomeShotsOnGoal";
    public const string AwayShotsOnGoal = "AwayShotsOnGoal";
    public const string TotalGoals = "TotalGoals";
    public const string HomeGoals = "HomeGoals";
    public const string AwayGoals = "AwayGoals";
    public const string Other = "Other";

    public static readonly string[] All =
    [
        TotalCorners,
        HomeCorners,
        AwayCorners,
        FirstHalfCorners,
        TotalShots,
        HomeShots,
        AwayShots,
        TotalShotsOnGoal,
        HomeShotsOnGoal,
        AwayShotsOnGoal,
        TotalGoals,
        HomeGoals,
        AwayGoals,
        Other
    ];
}

public static class BetSelections
{
    public const string Over = "Over";
    public const string Under = "Under";
    public const string Home = "Home";
    public const string Away = "Away";
    public const string Other = "Other";

    public static readonly string[] All = [Over, Under, Home, Away, Other];
}

public static class BetConfidenceLevels
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string VeryHigh = "VeryHigh";

    public static readonly string[] All = [Low, Medium, High, VeryHigh];
}

public static class BetPredictionModels
{
    public const string Manual = "Manual";
    public const string TotalCornersModel = "TotalCornersModel";
    public const string OverUnderLineModel = "OverUnderLineModel";
    public const string ShotsOnGoalModel = "ShotsOnGoalModel";
    public const string GoalsModel = "GoalsModel";
    public const string AutomatedCornersBot = "AutomatedCornersBot";
    public const string AutomatedGoalsBot = "AutomatedGoalsBot";
    public const string AutomatedSogBot = "AutomatedSogBot";
    public const string AutomatedShotsBot = "AutomatedShotsBot";
    public const string AutomatedModels2026Bot = "AutomatedModels2026Bot";

    public static readonly string[] All =
    [
        Manual,
        TotalCornersModel,
        OverUnderLineModel,
        ShotsOnGoalModel,
        GoalsModel,
        AutomatedCornersBot,
        AutomatedGoalsBot,
        AutomatedSogBot,
        AutomatedShotsBot,
        AutomatedModels2026Bot
    ];
}

public static class BettingCurrencies
{
    public const string Clp = "CLP";
    public const string Usd = "USD";
    public const string Aud = "AUD";

    public static readonly string[] All = [Clp, Usd, Aud];
}

public static class BettingUsers
{
    public const string DefaultUserId = "local-user";
}

public sealed class BankrollTransaction
{
    public long Id { get; set; }
    public required string UserId { get; set; } = BettingUsers.DefaultUserId;
    public required string CurrencyCode { get; set; } = BettingCurrencies.Clp;
    public DateTime TransactionDate { get; set; }
    public required string Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public long? BettingRecordId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}

public static class BankrollTransactionTypes
{
    public const string Deposit = "Deposit";
    public const string Withdrawal = "Withdrawal";
    public const string BetSettlement = "BetSettlement";
    public const string ManualAdjustment = "ManualAdjustment";

    public static readonly string[] All = [Deposit, Withdrawal, BetSettlement, ManualAdjustment];
}
