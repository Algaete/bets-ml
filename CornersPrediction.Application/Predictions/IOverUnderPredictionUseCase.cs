using System.Text.Json;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

public interface IOverUnderPredictionUseCase
{
    Task<OverUnderPredictionResult> PredictAsync(JsonElement features, CancellationToken cancellationToken);
}
