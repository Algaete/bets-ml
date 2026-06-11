using System.Text.Json;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

public interface IModelDebugPredictionUseCase
{
    Task<DebugModelPredictionResult> PredictAsync(
        string modelKey,
        JsonElement features,
        CancellationToken cancellationToken);
}
