namespace CornersPredictionApi.NewGenerationMl;

public sealed class NewGenerationMlOptions
{
    public const string SectionName = "NewGenerationMl";

    public string ModelsRoot { get; init; } = "../models/football";

    public string ActiveVersion { get; init; } = "home-corners-2026-08-09-trial-1840";

    public IDictionary<string, string> ActiveVersions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NewGenerationModelDefinitions.HomeCorners] = "home-corners-2026-08-09-trial-1840",
            [NewGenerationModelDefinitions.AwayCorners] = "targetawaycorners-2026-08-09-trial-53",
            [NewGenerationModelDefinitions.TotalCorners] = "targettotalcorners-2026-08-09-trial-56",
            [NewGenerationModelDefinitions.HomeShots] = "targethomeshots-2026-08-09-trial-56",
            [NewGenerationModelDefinitions.AwayShots] = "targetawayshots-2026-08-09-trial-59",
            [NewGenerationModelDefinitions.TotalShots] = "targettotalshots-2026-08-09-trial-56",
            [NewGenerationModelDefinitions.HomeShotsOnGoal] = "targethomeshotsongoal-2026-08-09-trial-18",
            [NewGenerationModelDefinitions.AwayShotsOnGoal] = "targetawayshotsongoal-2026-08-09-trial-33",
            [NewGenerationModelDefinitions.TotalShotsOnGoal] = "targettotalshotsongoal-2026-08-09-trial-54",
            [NewGenerationModelDefinitions.HomeGoals] = "targethomegoals-2026-08-09-trial-15",
            [NewGenerationModelDefinitions.AwayGoals] = "targetawaygoals-2026-08-09-trial-48",
            [NewGenerationModelDefinitions.TotalGoals] = "targettotalgoals-2026-08-09-trial-53"
        };

    public string PythonExecutable { get; init; } = "../.venv-new-generation/bin/python";

    public string ScriptPath { get; init; } = "../predict_new_generation.py";

    public int TimeoutSeconds { get; init; } = 60;
}
