using System.Text.Json;

namespace CornersPrediction.Web.Models.NewGeneration;

public sealed class NewGenerationIndexViewModel
{
    public IReadOnlyList<string> LeagueOptions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FormationOptions { get; init; } = Array.Empty<string>();
    public NewGenerationModelCatalogViewModel ModelCatalog { get; init; } = new();
    public string? League { get; init; }
    public string? Season { get; init; }
    public DateTime? MatchDate { get; init; }
    public string? HomeTeam { get; init; }
    public string? AwayTeam { get; init; }
    public bool IsKnockout { get; init; }
}

public sealed class NewGenerationModelInfoViewModel
{
    public string Status { get; init; } = "pending_artifacts";
    public bool Ready { get; init; }
    public bool Loaded { get; init; }
    public string Target { get; init; } = "TargetHomeCorners";
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
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
}

public sealed class NewGenerationModelCatalogViewModel
{
    public string Status { get; init; } = "pending_artifacts";
    public bool Ready { get; init; }
    public bool Available { get; init; }
    public bool Loaded { get; init; }
    public int TotalModels { get; init; }
    public int ReadyModels { get; init; }
    public IReadOnlyList<NewGenerationModelInfoViewModel> Models { get; init; } =
        Array.Empty<NewGenerationModelInfoViewModel>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
}

public sealed record NewGenerationPredictViewModel(
    string League,
    string? Season,
    DateOnly MatchDate,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation,
    string? AwayFormation,
    bool IsKnockout);

public sealed class NewGenerationPredictionResultViewModel
{
    public string Target { get; init; } = string.Empty;
    public string? Market { get; init; }
    public string? Scope { get; init; }
    public string? DisplayName { get; init; }
    public double PredictionRaw { get; init; }
    public double PredictionClipped { get; init; }
    public int PredictionRounded { get; init; }
    public string? ModelVersion { get; init; }
    public string? TrainedThrough { get; init; }
    public string? FeatureSet { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public NewGenerationMatchViewModel? Match { get; init; }
    public long DurationMilliseconds { get; init; }
    public IReadOnlyDictionary<string, JsonElement> FeaturePayload { get; init; } =
        new Dictionary<string, JsonElement>();
}

public sealed class NewGenerationBatchPredictionResultViewModel
{
    public NewGenerationMatchViewModel? Match { get; init; }
    public IReadOnlyList<NewGenerationPredictionResultViewModel> Predictions { get; init; } =
        Array.Empty<NewGenerationPredictionResultViewModel>();
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> FeaturePayloads { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public long DurationMilliseconds { get; init; }
}

public sealed class NewGenerationMatchViewModel
{
    public string League { get; init; } = string.Empty;
    public string? Season { get; init; }
    public DateOnly MatchDate { get; init; }
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string HomeFormationStyle { get; init; } = string.Empty;
    public string AwayFormationStyle { get; init; } = string.Empty;
}
