using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornersPrediction.Application.Automation.BotG;
using CornersPrediction.Domain.Automation.BotG;
using Microsoft.Extensions.Options;

namespace AutomatedCornersBot.Api;

public sealed class BotGArtifactOptions
{
    public const string SectionName = "BotG";

    public bool Enabled { get; set; } = true;
    public string ArtifactPath { get; set; } = "../models/bot-g/active.json";
}

public sealed record BotGArtifactStatus(
    bool Enabled,
    bool Available,
    string State,
    string Message,
    string? ModelVersion = null,
    string? ConfigurationVersion = null,
    string? FeatureSchemaVersion = null,
    DateTime? TrainedThroughUtc = null);

/// <summary>
/// Loads one immutable Bot G artifact for the lifetime of the process. A missing or invalid
/// artifact deliberately degrades to ModelUnavailable so shadow collection can continue without
/// manufacturing a probability or falling back to a rule-based selector.
/// </summary>
public sealed class BotGArtifactRuntime : IBotGMetaModelService, IBotGArtifactEvidenceProvider
{
    private readonly IBotGMetaModelService _model;
    private readonly IBotGArtifactEvidenceProvider? _evidence;

    public BotGArtifactStatus Status { get; }

    public BotGArtifactRuntime(
        IOptions<BotGArtifactOptions> options,
        IWebHostEnvironment environment,
        ILogger<BotGArtifactRuntime> logger)
    {
        var configured = options.Value;
        if (!configured.Enabled)
        {
            const string reason = "La carga del artefacto G está deshabilitada por configuración.";
            _model = new UnavailableBotGMetaModelService(reason);
            Status = new BotGArtifactStatus(false, false, "Disabled", reason);
            logger.LogWarning("Bot G meta-model is disabled; G2026 will remain abstention-only.");
            return;
        }

        try
        {
            var path = ResolvePath(environment.ContentRootPath, configured.ArtifactPath);
            if (!File.Exists(path))
            {
                const string reason = "No hay un artefacto G desplegable. El laboratorio sólo recolectará candidatos y se abstendrá.";
                _model = new UnavailableBotGMetaModelService(reason);
                Status = new BotGArtifactStatus(true, false, "Missing", reason);
                logger.LogWarning(
                    "Bot G artifact not found. Path={ArtifactPath}. G2026 remains shadow and abstention-only until an honest OOF artifact is installed.",
                    path);
                return;
            }

            var bytes = File.ReadAllBytes(path);
            var artifact = JsonSerializer.Deserialize<BotGModelArtifact>(bytes, SerializerOptions)
                ?? throw new InvalidDataException("The Bot G artifact JSON was empty.");
            var loaded = new InMemoryBotGMetaModelService(artifact);
            _model = loaded;
            _evidence = loaded;
            Status = new BotGArtifactStatus(
                true,
                true,
                "Ready",
                "Artefacto G validado y listo para inferencia shadow.",
                artifact.ModelVersion,
                artifact.ConfigurationVersion,
                artifact.FeatureSchemaVersion,
                artifact.TrainedThroughUtc);
            logger.LogInformation(
                "Bot G artifact loaded. Path={ArtifactPath}, Sha256={ArtifactSha256}, ModelVersion={ModelVersion}, FeatureSchemaVersion={FeatureSchemaVersion}, TrainedThroughUtc={TrainedThroughUtc}",
                path,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                artifact.ModelVersion,
                artifact.FeatureSchemaVersion,
                artifact.TrainedThroughUtc);
        }
        catch (Exception exception)
        {
            var reason = $"El artefacto G no superó la validación fail-closed: {exception.Message}";
            _model = new UnavailableBotGMetaModelService(reason);
            Status = new BotGArtifactStatus(true, false, "Invalid", reason);
            logger.LogError(
                exception,
                "Bot G artifact validation failed. G2026 remains shadow and abstention-only; no fallback probability will be used.");
        }
    }

    public IReadOnlyList<BotGCalibrationProfile> CalibrationProfiles =>
        _evidence?.CalibrationProfiles ?? [];

    public IReadOnlyList<BotGOodFeatureReference> OodReferenceFeatures =>
        _evidence?.OodReferenceFeatures ?? [];

    public BotGMetaModelPrediction Predict(BotGMetaModelInput input) => _model.Predict(input);

    private static string ResolvePath(string contentRootPath, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(Path.Combine(contentRootPath, "..", "models", "bot-g", "active.json"));
        return Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
