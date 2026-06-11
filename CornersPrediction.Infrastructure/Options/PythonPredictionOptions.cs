namespace CornersPrediction.Infrastructure.Options;

public sealed class PythonPredictionOptions
{
    public const string SectionName = "PythonPrediction";

    public string PythonExecutable { get; init; } = "python3";

    public string ScriptPath { get; init; } = "../predict.py";

    public string OverUnderScriptPath { get; init; } = "../predict_over_under.py";

    public string OverUnderModelPath { get; init; } = "../MLModels/OverUnder/model.pkl";

    public string ShotsOnGoalScriptPath { get; init; } = "../predict_shots_on_goal.py";

    public string ShotsOnGoalModelPath { get; init; } = "../newModelsML";

    public string ModelDebugScriptPath { get; init; } = "../predict_single_model.py";

    public string ModelDebugModelPath { get; init; } = "../newModelsML";

    public int TimeoutSeconds { get; init; } = 30;
}
