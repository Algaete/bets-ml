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

    /// <summary>
    /// Executes the Over/Under classifier using the supplied feature JSON.
    /// </summary>
    Task<OverUnderPredictionResult> PredictOverUnderAsync(JsonElement features, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the shots-on-goal regression model using the supplied feature JSON.
    /// </summary>
    Task<ShotsOnGoalPredictionResult> PredictShotsOnGoalAsync(JsonElement features, CancellationToken cancellationToken);

    /// <summary>
    /// Executes one raw model artifact for diagnostic testing.
    /// </summary>
    Task<DebugModelPredictionResult> PredictDebugModelAsync(
        string modelKey,
        JsonElement features,
        CancellationToken cancellationToken);
}
