using System.Text.Json.Serialization;

namespace CornersPrediction.Domain.Predictions;

public sealed class DebugModelPredictionResult
{
    [JsonPropertyName("modelKey")]
    public string ModelKey { get; init; } = string.Empty;

    [JsonPropertyName("market")]
    public string Market { get; init; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty;

    [JsonPropertyName("modelFile")]
    public string ModelFile { get; init; } = string.Empty;

    [JsonPropertyName("columnsFile")]
    public string ColumnsFile { get; init; } = string.Empty;

    [JsonPropertyName("prediction")]
    public double Prediction { get; init; }

    [JsonPropertyName("featureCount")]
    public int FeatureCount { get; init; }

    [JsonPropertyName("missingFeatureCount")]
    public int MissingFeatureCount { get; init; }

    [JsonPropertyName("missingFeatures")]
    public IReadOnlyList<string> MissingFeatures { get; init; } = Array.Empty<string>();

    [JsonPropertyName("debugValues")]
    public Dictionary<string, double?> DebugValues { get; init; } = new();
}
