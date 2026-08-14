using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPredictionApi.NewGenerationMl;

public sealed record NewGenerationPredictionRequest(
    string League,
    string? Season,
    DateOnly MatchDate,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation = null,
    string? AwayFormation = null,
    bool IsKnockout = false)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public sealed record NewGenerationMatchSummary(
    string League,
    string? Season,
    DateOnly MatchDate,
    string HomeTeam,
    string AwayTeam,
    string HomeFormationStyle,
    string AwayFormationStyle);

public sealed class NewGenerationModelInfo
{
    public string Status { get; init; } = "pending_artifacts";
    public bool Ready { get; init; }
    public bool Loaded { get; init; }
    public string Target { get; init; } = NewGenerationModelDefinitions.HomeCorners;
    public string? Market { get; init; }
    public string? Scope { get; init; }
    public string? DisplayName { get; init; }
    public string? ModelVersion { get; init; }
    public string? TrainedThrough { get; init; }
    public string? FeatureSet { get; init; }
    public string? Algorithm { get; init; }
    public string? TrainedAt { get; init; }
    public string? DatasetSha256 { get; init; }
    public double? TestMae { get; init; }
    public int FeatureCount { get; init; }
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CategoricalFeatures { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NumericFeatures { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
}

public sealed class NewGenerationModelCatalogInfo
{
    public string Status { get; init; } = "pending_artifacts";
    public bool Ready { get; init; }
    public bool Available { get; init; }
    public bool Loaded { get; init; }
    public int TotalModels { get; init; }
    public int ReadyModels { get; init; }
    public IReadOnlyList<NewGenerationModelInfo> Models { get; init; } = Array.Empty<NewGenerationModelInfo>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
}

public sealed class NewGenerationPredictionResult
{
    [JsonPropertyName("target")]
    public string Target { get; init; } = NewGenerationModelDefinitions.HomeCorners;

    [JsonPropertyName("market")]
    public string? Market { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("predictionRaw")]
    public double PredictionRaw { get; init; }

    [JsonPropertyName("predictionClipped")]
    public double PredictionClipped { get; init; }

    [JsonPropertyName("predictionRounded")]
    public int PredictionRounded { get; init; }

    [JsonPropertyName("modelVersion")]
    public string? ModelVersion { get; init; }

    [JsonPropertyName("trainedThrough")]
    public string? TrainedThrough { get; init; }

    [JsonPropertyName("featureSet")]
    public string? FeatureSet { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("match")]
    public NewGenerationMatchSummary? Match { get; init; }

    [JsonPropertyName("durationMilliseconds")]
    public long DurationMilliseconds { get; init; }

    [JsonPropertyName("featurePayload")]
    public IReadOnlyDictionary<string, object?> FeaturePayload { get; init; } =
        new Dictionary<string, object?>();
}

public sealed class NewGenerationBatchPredictionResult
{
    public NewGenerationMatchSummary? Match { get; init; }
    public IReadOnlyList<NewGenerationPredictionResult> Predictions { get; init; } =
        Array.Empty<NewGenerationPredictionResult>();
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> FeaturePayloads { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, object?>>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public long DurationMilliseconds { get; init; }
}

internal sealed class PythonPredictionEnvelope
{
    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty;

    [JsonPropertyName("predictionRaw")]
    public double PredictionRaw { get; init; }

    [JsonPropertyName("predictionClipped")]
    public double PredictionClipped { get; init; }

    [JsonPropertyName("predictionRounded")]
    public int PredictionRounded { get; init; }

    [JsonPropertyName("modelVersion")]
    public string? ModelVersion { get; init; }

    [JsonPropertyName("trainedThrough")]
    public string? TrainedThrough { get; init; }

    [JsonPropertyName("featureSet")]
    public string? FeatureSet { get; init; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string>? Warnings { get; init; }
}

internal sealed class PythonBatchPredictionEnvelope
{
    [JsonPropertyName("predictions")]
    public IReadOnlyList<PythonPredictionEnvelope> Predictions { get; init; } =
        Array.Empty<PythonPredictionEnvelope>();
}

public sealed class NewGenerationModelNotReadyException : InvalidOperationException
{
    public NewGenerationModelNotReadyException(string message) : base(message)
    {
    }
}
