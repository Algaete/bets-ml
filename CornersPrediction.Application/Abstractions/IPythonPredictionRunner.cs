using System.Text.Json;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Abstractions;

/// <summary>
/// Port used by the Application layer to request a prediction from an external model runner.
/// </summary>
public interface IPythonPredictionRunner
{
    /// <summary>
    /// Executes a prediction using the supplied feature JSON.
    /// </summary>
    Task<PredictionResult> PredictAsync(JsonElement features, CancellationToken cancellationToken);
}
