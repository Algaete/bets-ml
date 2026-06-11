using System.Text.Json;
using CornersPrediction.Application.Abstractions;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

public sealed class OverUnderPredictionUseCase : IOverUnderPredictionUseCase
{
    private readonly IPythonPredictionRunner _predictionRunner;

    public OverUnderPredictionUseCase(IPythonPredictionRunner predictionRunner)
    {
        _predictionRunner = predictionRunner;
    }

    public Task<OverUnderPredictionResult> PredictAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        if (features.ValueKind is not JsonValueKind.Object)
        {
            throw new ArgumentException("Over/Under prediction features payload must be a JSON object.");
        }

        return _predictionRunner.PredictOverUnderAsync(features, cancellationToken);
    }
}
