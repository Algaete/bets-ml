using System.Text.Json;
using CornersPrediction.Application.Abstractions;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

public sealed class ShotsOnGoalPredictionUseCase : IShotsOnGoalPredictionUseCase
{
    private readonly IPythonPredictionRunner _predictionRunner;

    public ShotsOnGoalPredictionUseCase(IPythonPredictionRunner predictionRunner)
    {
        _predictionRunner = predictionRunner;
    }

    public Task<ShotsOnGoalPredictionResult> PredictAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        if (features.ValueKind is not JsonValueKind.Object)
        {
            throw new ArgumentException("Shots-on-goal prediction features payload must be a JSON object.");
        }

        return _predictionRunner.PredictShotsOnGoalAsync(features, cancellationToken);
    }
}
