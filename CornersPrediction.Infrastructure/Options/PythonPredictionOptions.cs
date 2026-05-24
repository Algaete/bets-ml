namespace CornersPrediction.Infrastructure.Options;

public sealed class PythonPredictionOptions
{
    public const string SectionName = "PythonPrediction";

    public string PythonExecutable { get; init; } = "python3";

    public string ScriptPath { get; init; } = "../predict.py";

    public int TimeoutSeconds { get; init; } = 30;
}
