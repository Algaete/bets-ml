using System.Text.Json;
using CornersPrediction.Application.Abstractions;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

/// <summary>
/// Application use case that validates the prediction payload and delegates model execution.
/// </summary>
public sealed class PredictTotalCornersUseCase : IPredictTotalCornersUseCase
{
    private readonly IPythonPredictionRunner _predictionRunner;

    public PredictTotalCornersUseCase(IPythonPredictionRunner predictionRunner)
    {
        _predictionRunner = predictionRunner;
    }

    /// <summary>
    /// Validates that features are a JSON object and sends them to the configured prediction runner.
    /// </summary>
    public Task<PredictionResult> PredictAsync(JsonElement features, CancellationToken cancellationToken)
    {
        if (features.ValueKind is not JsonValueKind.Object)
        {
            throw new ArgumentException("The prediction payload must be a JSON object.", nameof(features));
        }

        return _predictionRunner.PredictAsync(features, cancellationToken);
    }
}
