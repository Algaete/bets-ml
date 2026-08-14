using System.Text.Json;
using CornersPrediction.Application.Automation.BotC;
using Microsoft.Extensions.Options;

namespace AutomatedCornersBot.Api;

public sealed class BotCMetaModelOptions
{
    public const string SectionName = "BotCMetaModel";
    public bool Enabled { get; init; } = true;
    public string ArtifactPath { get; init; } = "../models/bot-c-meta/active.json";
    public IReadOnlyDictionary<string, string> ArtifactPaths { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed class FileBotCMetaModelPredictor : IBotCMetaModelPredictor
{
    private readonly BotCMetaModelOptions _options;
    private readonly string _contentRootPath;
    private readonly ILogger<FileBotCMetaModelPredictor> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, ArtifactCacheEntry> _cache = new(StringComparer.Ordinal);

    public FileBotCMetaModelPredictor(
        IOptions<BotCMetaModelOptions> options,
        IHostEnvironment environment,
        ILogger<FileBotCMetaModelPredictor> logger)
    {
        _options = options.Value;
        _logger = logger;
        _contentRootPath = environment.ContentRootPath;
    }

    public BotCMetaModelPrediction Predict(BotCMetaModelInput input)
    {
        if (!_options.Enabled)
        {
            return BotCMetaModelPrediction.Unavailable("El meta-modelo está deshabilitado por configuración.");
        }

        var artifactPath = ResolveArtifactPath(input.FeatureSchemaVersion);
        var (artifact, loadFailure) = LoadArtifact(artifactPath);
        if (artifact is null)
        {
            return BotCMetaModelPrediction.Unavailable(loadFailure ?? "No existe un artefacto de meta-modelo activo.");
        }
        if (!artifact.FeatureSchemaVersion.Equals(input.FeatureSchemaVersion, StringComparison.Ordinal))
        {
            return BotCMetaModelPrediction.Unavailable(
                $"Feature schema mismatch: artefacto={artifact.FeatureSchemaVersion}, runtime={input.FeatureSchemaVersion}.");
        }

        var linear = artifact.Intercept;
        foreach (var feature in artifact.Features)
        {
            double rawValue;
            if (feature.Name.StartsWith("marketType=", StringComparison.Ordinal))
            {
                rawValue = input.MarketType.Equals(feature.Name[11..], StringComparison.Ordinal) ? 1d : 0d;
            }
            else if (feature.Name.StartsWith("selection=", StringComparison.Ordinal))
            {
                rawValue = input.Selection.Equals(feature.Name[10..], StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
            }
            else if (!input.NumericFeatures.TryGetValue(feature.Name, out rawValue) || !double.IsFinite(rawValue))
            {
                return BotCMetaModelPrediction.Unavailable($"Falta la feature requerida '{feature.Name}'.");
            }

            var scale = Math.Abs(feature.Scale) < 1e-12 ? 1d : feature.Scale;
            linear += ((rawValue - feature.Mean) / scale) * feature.Coefficient;
        }

        var probability = Sigmoid(linear);
        if (artifact.Calibration is not null)
        {
            var logit = Math.Log(Math.Clamp(probability, 1e-9, 1d - 1e-9)
                                 / (1d - Math.Clamp(probability, 1e-9, 1d - 1e-9)));
            probability = Sigmoid(artifact.Calibration.Intercept + artifact.Calibration.Slope * logit);
        }

        return new BotCMetaModelPrediction(
            true,
            probability,
            artifact.ModelName,
            artifact.ModelVersion,
            TrainedThroughUtc: artifact.TrainedThroughUtc);
    }

    private string ResolveArtifactPath(string featureSchemaVersion)
    {
        var configuredPath = _options.ArtifactPaths.TryGetValue(featureSchemaVersion, out var schemaPath)
            && !string.IsNullOrWhiteSpace(schemaPath)
                ? schemaPath
                : _options.ArtifactPath;
        return Path.GetFullPath(configuredPath, _contentRootPath);
    }

    private (BotCLinearModelArtifact? Artifact, string? Failure) LoadArtifact(string artifactPath)
    {
        lock (_gate)
        {
            var state = _cache.GetValueOrDefault(artifactPath) ?? new ArtifactCacheEntry();
            if (!File.Exists(artifactPath))
            {
                state.Artifact = null;
                state.LoadFailure = $"No existe el artefacto '{artifactPath}'.";
                _cache[artifactPath] = state;
                return (null, state.LoadFailure);
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(artifactPath);
            if (state.Artifact is not null && writeTimeUtc == state.LoadedWriteTimeUtc)
            {
                return (state.Artifact, null);
            }

            try
            {
                var artifact = JsonSerializer.Deserialize<BotCLinearModelArtifact>(
                    File.ReadAllText(artifactPath),
                    JsonOptions)
                    ?? throw new InvalidOperationException("El artefacto JSON está vacío.");
                Validate(artifact);
                state.Artifact = artifact;
                state.LoadedWriteTimeUtc = writeTimeUtc;
                state.LoadFailure = null;
                _cache[artifactPath] = state;
                _logger.LogInformation(
                    "Pick-selector meta-model loaded. Model={ModelName}, Version={ModelVersion}, FeatureSchema={FeatureSchema}, FeatureCount={FeatureCount}",
                    artifact.ModelName,
                    artifact.ModelVersion,
                    artifact.FeatureSchemaVersion,
                    artifact.Features.Count);
                return (artifact, null);
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
            {
                state.Artifact = null;
                state.LoadedWriteTimeUtc = writeTimeUtc;
                state.LoadFailure = $"El artefacto no pudo cargarse: {exception.Message}";
                _cache[artifactPath] = state;
                _logger.LogWarning(exception, "Pick-selector meta-model artifact could not be loaded. Path={ArtifactPath}", artifactPath);
                return (null, state.LoadFailure);
            }
        }
    }

    private sealed class ArtifactCacheEntry
    {
        public DateTime LoadedWriteTimeUtc { get; set; } = DateTime.MinValue;
        public BotCLinearModelArtifact? Artifact { get; set; }
        public string? LoadFailure { get; set; }
    }

    private static void Validate(BotCLinearModelArtifact artifact)
    {
        if (!artifact.ModelType.Equals("LogisticRegression", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(artifact.ModelName)
            || string.IsNullOrWhiteSpace(artifact.ModelVersion)
            || string.IsNullOrWhiteSpace(artifact.FeatureSchemaVersion)
            || artifact.Features.Count == 0
            || !double.IsFinite(artifact.Intercept)
            || artifact.Features.Any(feature => string.IsNullOrWhiteSpace(feature.Name)
                || !double.IsFinite(feature.Mean)
                || !double.IsFinite(feature.Scale)
                || !double.IsFinite(feature.Coefficient)))
        {
            throw new InvalidOperationException("El artefacto LogisticRegression no cumple el contrato Bot C.");
        }
    }

    private static double Sigmoid(double value) => value >= 0
        ? 1d / (1d + Math.Exp(-value))
        : Math.Exp(value) / (1d + Math.Exp(value));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record BotCLinearModelArtifact
{
    public string ModelType { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public DateTime TrainedThroughUtc { get; init; }
    public double Intercept { get; init; }
    public IReadOnlyList<BotCLinearFeature> Features { get; init; } = [];
    public BotCLinearCalibration? Calibration { get; init; }
    public IReadOnlyDictionary<string, double> ValidationMetrics { get; init; } =
        new Dictionary<string, double>();
}

public sealed record BotCLinearFeature
{
    public string Name { get; init; } = string.Empty;
    public double Mean { get; init; }
    public double Scale { get; init; } = 1d;
    public double Coefficient { get; init; }
}

public sealed record BotCLinearCalibration
{
    public string Method { get; init; } = "Platt";
    public double Intercept { get; init; }
    public double Slope { get; init; } = 1d;
}
