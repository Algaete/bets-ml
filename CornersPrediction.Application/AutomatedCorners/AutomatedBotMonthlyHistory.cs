namespace CornersPrediction.Application.AutomatedCorners;

public static class AutomatedBotMarketScope
{
    public static string? Normalize(string? family) => family?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "CORNERS" => "CORNERS",
        "GOALS" => "GOALS",
        "SHOTS" => "SHOTS",
        "SOG" or "SHOTS_ON_GOAL" => "SOG",
        _ => throw new ArgumentException("Unsupported bot market family.", nameof(family))
    };

    public static string[] MarketTypes(string family) => Normalize(family) switch
    {
        "CORNERS" => ["TotalCorners", "HomeTeamCorners", "AwayTeamCorners"],
        "GOALS" => ["TotalGoals", "HomeTeamGoals", "AwayTeamGoals"],
        "SHOTS" => ["TotalShots", "HomeTeamShots", "AwayTeamShots"],
        "SOG" => ["TotalShotsOnGoal", "HomeTeamShotsOnGoal", "AwayTeamShotsOnGoal"],
        _ => throw new ArgumentException("A market family is required.", nameof(family))
    };
}

public sealed record AutomatedBotMonthlySummary
{
    public DateTime Month { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string BotLabel => BotKey switch
    {
        "A" => "Bot A Actual",
        "B" => "Bot B · Retirado",
        "C" => "Bot C · Modelos 2026",
        "D" => "Bot D · Team Strength Gap",
        "E" => "Bot E · Calibración empírica",
        "F" => "Bot F · Legacy ML calibrado",
        _ => "Bots personalizados"
    };
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Won { get; init; }
    public int Lost { get; init; }
    public int Push { get; init; }
    public int Void { get; init; }
    public decimal ProfitLoss { get; init; }
    public decimal SettledStake { get; init; }
    public decimal? YieldPct => SettledStake > 0 ? ProfitLoss / SettledStake * 100m : null;
}

public interface IAutomatedBotMonthlyHistoryRepository
{
    Task<IReadOnlyList<AutomatedBotMonthlySummary>> GetMonthlyHistoryAsync(
        DateTime dateFrom, DateTime dateTo, string marketFamily, CancellationToken cancellationToken);
}
