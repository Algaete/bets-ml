using System.Text.Json;
using CornersPrediction.Domain.Predictions;

namespace CornersPrediction.Application.Predictions;

public interface IShotsOnGoalPredictionUseCase
{
    Task<ShotsOnGoalPredictionResult> PredictAsync(JsonElement features, CancellationToken cancellationToken);
}
