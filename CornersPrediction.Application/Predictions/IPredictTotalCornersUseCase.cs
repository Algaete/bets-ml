using System.Text.Json;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

/// <summary>
/// Application entry point for total-corners predictions.
/// </summary>
public interface IPredictTotalCornersUseCase
{
    /// <summary>
    /// Predicts total corners from a JSON object containing model features.
    /// </summary>
    Task<PredictionResult> PredictAsync(JsonElement features, CancellationToken cancellationToken);
}
