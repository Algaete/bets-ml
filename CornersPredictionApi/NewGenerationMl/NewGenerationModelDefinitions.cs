namespace CornersPredictionApi.NewGenerationMl;

public sealed record NewGenerationModelDefinition(
    string Target,
    string Market,
    string Scope,
    string DisplayName);

public static class NewGenerationModelDefinitions
{
    public const string HomeCorners = "TargetHomeCorners";
    public const string AwayCorners = "TargetAwayCorners";
    public const string TotalCorners = "TargetTotalCorners";
    public const string HomeShots = "TargetHomeShots";
    public const string AwayShots = "TargetAwayShots";
    public const string TotalShots = "TargetTotalShots";
    public const string HomeShotsOnGoal = "TargetHomeShotsOnGoal";
    public const string AwayShotsOnGoal = "TargetAwayShotsOnGoal";
    public const string TotalShotsOnGoal = "TargetTotalShotsOnGoal";
    public const string HomeGoals = "TargetHomeGoals";
    public const string AwayGoals = "TargetAwayGoals";
    public const string TotalGoals = "TargetTotalGoals";

    public static IReadOnlyList<NewGenerationModelDefinition> All { get; } =
    [
        new(HomeCorners, "corners", "home", "Corners local"),
        new(AwayCorners, "corners", "away", "Corners visita"),
        new(TotalCorners, "corners", "total", "Corners totales"),
        new(HomeShots, "shots", "home", "Tiros local"),
        new(AwayShots, "shots", "away", "Tiros visita"),
        new(TotalShots, "shots", "total", "Tiros totales"),
        new(HomeShotsOnGoal, "shots_on_goal", "home", "Tiros al arco local"),
        new(AwayShotsOnGoal, "shots_on_goal", "away", "Tiros al arco visita"),
        new(TotalShotsOnGoal, "shots_on_goal", "total", "Tiros al arco totales"),
        new(HomeGoals, "goals", "home", "Goles local"),
        new(AwayGoals, "goals", "away", "Goles visita"),
        new(TotalGoals, "goals", "total", "Goles totales")
    ];

    public static NewGenerationModelDefinition Get(string target) =>
        All.FirstOrDefault(item => item.Target.Equals(target, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Unsupported new-generation target '{target}'.", nameof(target));
}
