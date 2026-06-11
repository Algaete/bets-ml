using System.Text.Json;
using CornersPrediction.Application.Abstractions;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

public sealed class ModelDebugPredictionUseCase : IModelDebugPredictionUseCase
{
    private static readonly HashSet<string> AllowedModelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "corners-total",
        "corners-home",
        "corners-away",
        "shots-total",
        "shots-home",
        "shots-away",
        "sog-total",
        "sog-home",
        "sog-away"
    };

    private readonly IPythonPredictionRunner _predictionRunner;

    public ModelDebugPredictionUseCase(IPythonPredictionRunner predictionRunner)
    {
        _predictionRunner = predictionRunner;
    }

    public Task<DebugModelPredictionResult> PredictAsync(
        string modelKey,
        JsonElement features,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelKey) || !AllowedModelKeys.Contains(modelKey))
        {
            var validKeys = string.Join(", ", AllowedModelKeys.OrderBy(key => key));
            throw new ArgumentException($"Unknown debug model key '{modelKey}'. Valid values: {validKeys}");
        }

        if (features.ValueKind is not JsonValueKind.Object)
        {
            throw new ArgumentException("Debug model features payload must be a JSON object.");
        }

        return _predictionRunner.PredictDebugModelAsync(modelKey, features, cancellationToken);
    }
}
