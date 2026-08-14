using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.NewGenerationMl;

public sealed class NewGenerationModelPackage
{
    public const string Target = NewGenerationModelDefinitions.HomeCorners;

    private readonly NewGenerationMlOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, PackageSnapshot>? _snapshots;

    public NewGenerationModelPackage(
        IOptions<NewGenerationMlOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public IReadOnlyList<PackageSnapshot> GetSnapshots()
    {
        lock (_gate)
        {
            _snapshots ??= LoadAll();
            return NewGenerationModelDefinitions.All
                .Select(definition => _snapshots[definition.Target])
                .ToArray();
        }
    }

    public PackageSnapshot GetSnapshot() => GetSnapshot(Target);

    public PackageSnapshot GetSnapshot(string target)
    {
        NewGenerationModelDefinitions.Get(target);
        return GetSnapshots().Single(snapshot => snapshot.Target.Equals(target, StringComparison.Ordinal));
    }

    public NewGenerationModelInfo GetInfo() => GetInfo(Target);

    public NewGenerationModelInfo GetInfo(string target) => ToInfo(GetSnapshot(target));

    public NewGenerationModelCatalogInfo GetCatalogInfo() =>
        BuildCatalog(GetSnapshots().Select(snapshot => ToInfo(snapshot)).ToArray());

    public static NewGenerationModelCatalogInfo BuildCatalog(
        IReadOnlyList<NewGenerationModelInfo> models,
        bool loaded = false,
        string? error = null)
    {
        var readyCount = models.Count(model => model.Ready);
        var totalCount = NewGenerationModelDefinitions.All.Count;
        var allReady = readyCount == totalCount;
        var available = readyCount > 0;
        var warnings = models.SelectMany(model => model.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        return new NewGenerationModelCatalogInfo
        {
            Status = error is not null
                ? "unhealthy"
                : allReady
                    ? loaded ? "healthy" : "ready"
                    : available ? "partial" : "pending_artifacts",
            Ready = allReady,
            Available = available,
            Loaded = loaded && allReady,
            TotalModels = totalCount,
            ReadyModels = readyCount,
            Models = models,
            Warnings = warnings,
            Error = error
        };
    }

    public static NewGenerationModelInfo ToInfo(
        PackageSnapshot snapshot,
        bool loaded = false,
        string? status = null) => snapshot.IsReady
        ? new NewGenerationModelInfo
        {
            Status = status ?? (loaded ? "healthy" : "ready"),
            Ready = true,
            Loaded = loaded,
            Target = snapshot.Target,
            Market = snapshot.Market,
            Scope = snapshot.Scope,
            DisplayName = snapshot.DisplayName,
            ModelVersion = snapshot.Version,
            TrainedThrough = snapshot.TrainedThrough,
            FeatureSet = snapshot.FeatureSet,
            Algorithm = snapshot.Algorithm,
            TrainedAt = snapshot.TrainedAt,
            DatasetSha256 = snapshot.DatasetSha256,
            TestMae = snapshot.TestMae,
            FeatureCount = snapshot.Features.Count,
            Features = snapshot.Features,
            CategoricalFeatures = snapshot.CategoricalFeatures,
            NumericFeatures = snapshot.NumericFeatures,
            Warnings = snapshot.Warnings
        }
        : new NewGenerationModelInfo
        {
            Status = "pending_artifacts",
            Ready = false,
            Loaded = false,
            Target = snapshot.Target,
            Market = snapshot.Market,
            Scope = snapshot.Scope,
            DisplayName = snapshot.DisplayName,
            ModelVersion = snapshot.Version,
            Error = snapshot.Error,
            Warnings = snapshot.Warnings.Count > 0
                ? snapshot.Warnings
                : [$"No validated production bundle is available for {snapshot.DisplayName}."]
        };

    private IReadOnlyDictionary<string, PackageSnapshot> LoadAll() =>
        NewGenerationModelDefinitions.All.ToDictionary(
            definition => definition.Target,
            Load,
            StringComparer.Ordinal);

    private PackageSnapshot Load(NewGenerationModelDefinition definition)
    {
        var version = ResolveVersion(definition);
        var modelsRoot = ResolvePath(_options.ModelsRoot);
        var versionRoot = EnsureInsideRoot(
            modelsRoot,
            Path.Combine(modelsRoot, version, definition.Market, definition.Scope));
        var manifestPath = EnsureInsideRoot(modelsRoot, Path.Combine(versionRoot, "deployment_manifest.json"));
        try
        {
            if (!File.Exists(manifestPath))
            {
                return PackageSnapshot.Pending(
                    definition,
                    version,
                    manifestPath,
                    $"Missing deployment_manifest.json for version '{version}'.");
            }

            VerifyChecksumFile(versionRoot);
            using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = manifestDocument.RootElement;
            RequireString(manifest, "target", definition.Target);
            var bundleStatus = ReadRequiredString(manifest, "status");
            if (bundleStatus is not ("active" or "active_candidate"))
            {
                throw new InvalidOperationException($"Deployment bundle status '{bundleStatus}' is not active.");
            }

            var manifestVersion = ReadRequiredString(manifest, "model_version");
            if (!manifestVersion.Equals(version, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Configured version '{version}' differs from manifest version '{manifestVersion}'.");
            }

            var modelPath = ResolveTrustedRelative(
                versionRoot, ReadRequiredString(manifest, "preferred_model_file"));
            var metadataPath = ResolveTrustedRelative(
                versionRoot, ReadRequiredString(manifest, "metadata_file"));
            var runtimePath = ResolveTrustedRelative(
                versionRoot, ReadRequiredString(manifest, "runtime_file"));
            if (!modelPath.EndsWith(".cbm", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The preferred inference artifact must be a native CatBoost .cbm file.");
            }
            if (!File.Exists(modelPath) || !File.Exists(metadataPath) || !File.Exists(runtimePath))
            {
                throw new FileNotFoundException("The active package is missing its CBM, metadata or official runtime.");
            }

            VerifyManifestHashes(versionRoot, manifest);
            using var metadataDocument = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var metadata = metadataDocument.RootElement;
            RequireString(metadata, "target", definition.Target);
            var features = ReadStringArray(metadata, "features");
            var categorical = ReadStringArray(metadata, "categorical_features");
            var numeric = ReadStringArray(metadata, "numeric_features");
            if (features.Count == 0)
            {
                throw new InvalidOperationException("Model metadata must contain at least one feature.");
            }
            var leaked = features
                .Where(feature => feature.StartsWith("Target", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (leaked.Length > 0)
            {
                throw new InvalidOperationException($"Target features are forbidden: {string.Join(", ", leaked)}");
            }
            if (features.Distinct(StringComparer.Ordinal).Count() != features.Count)
            {
                throw new InvalidOperationException("Model metadata contains duplicate features.");
            }
            var typed = categorical.Concat(numeric).ToArray();
            if (typed.Distinct(StringComparer.Ordinal).Count() != typed.Length ||
                !features.ToHashSet(StringComparer.Ordinal).SetEquals(typed))
            {
                throw new InvalidOperationException("Categorical and numeric metadata must partition the feature schema exactly.");
            }
            ValidateNumericMedians(metadata, numeric);

            double? testMae = null;
            var evaluationPath = ResolveTrustedRelative(versionRoot, "reference_evaluation.json");
            if (File.Exists(evaluationPath))
            {
                using var evaluationDocument = JsonDocument.Parse(File.ReadAllText(evaluationPath));
                var evaluation = evaluationDocument.RootElement;
                RequireString(evaluation, "target", definition.Target);
                testMae = ReadNestedNumber(evaluation, "test_metrics", "mae");
            }

            var warnings = new List<string>();
            if (testMae is null)
            {
                warnings.Add($"The signed reference evaluation has no test MAE for {definition.Target}.");
            }
            var declaredMarket = ReadOptionalString(manifest, "market");
            if (!string.IsNullOrWhiteSpace(declaredMarket) &&
                !declaredMarket.Equals(definition.Market, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"The signed manifest declares market '{declaredMarket}', but target {definition.Target} is registered under '{definition.Market}'.");
            }

            return new PackageSnapshot(
                true,
                definition.Target,
                definition.Market,
                definition.Scope,
                definition.DisplayName,
                manifestPath,
                null,
                manifestVersion,
                ReadOptionalString(manifest, "trained_through"),
                ReadOptionalString(metadata, "feature_set"),
                ReadOptionalString(metadata, "family"),
                ReadOptionalString(manifest, "trained_at"),
                ReadOptionalString(manifest, "dataset_sha256"),
                testMae,
                features,
                categorical,
                numeric,
                warnings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return PackageSnapshot.Pending(definition, version, manifestPath, exception.Message);
        }
    }

    private string ResolveVersion(NewGenerationModelDefinition definition)
    {
        if (definition.Target.Equals(Target, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(_options.ActiveVersion))
        {
            return _options.ActiveVersion.Trim();
        }
        if (_options.ActiveVersions.TryGetValue(definition.Target, out var version) &&
            !string.IsNullOrWhiteSpace(version))
        {
            return version.Trim();
        }
        throw new InvalidOperationException($"No active version is configured for {definition.Target}.");
    }

    private static void VerifyChecksumFile(string versionRoot)
    {
        var checksumPath = ResolveTrustedRelative(versionRoot, "checksums.sha256");
        if (!File.Exists(checksumPath))
        {
            throw new FileNotFoundException("The bundle is missing checksums.sha256.");
        }
        var lines = File.ReadAllLines(checksumPath);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("checksums.sha256 is empty.");
        }
        foreach (var line in lines)
        {
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("checksums.sha256 contains an invalid line.");
            }
            var path = ResolveTrustedRelative(versionRoot, parts[1].Trim());
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Checksummed bundle file is missing: {Path.GetFileName(path)}");
            }
            VerifyHash(path, parts[0]);
        }
    }

    private static void VerifyManifestHashes(string versionRoot, JsonElement manifest)
    {
        if (!manifest.TryGetProperty("sha256", out var hashes) || hashes.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("deployment_manifest.json must contain sha256 checksums.");
        }
        foreach (var property in hashes.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                throw new InvalidOperationException($"Manifest checksum for '{property.Name}' is invalid.");
            }
            VerifyHash(ResolveTrustedRelative(versionRoot, property.Name), property.Value.GetString()!);
        }
    }

    private static void ValidateNumericMedians(JsonElement metadata, IReadOnlyList<string> numeric)
    {
        if (!metadata.TryGetProperty("numeric_medians", out var medians) || medians.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Model metadata must define numeric_medians.");
        }
        var names = medians.EnumerateObject().Select(property => property.Name).ToArray();
        if (!numeric.ToHashSet(StringComparer.Ordinal).SetEquals(names))
        {
            throw new InvalidOperationException("numeric_medians must cover every numeric feature exactly.");
        }
        if (medians.EnumerateObject().Any(property => property.Value.ValueKind != JsonValueKind.Number))
        {
            throw new InvalidOperationException("Every numeric median must be a number.");
        }
    }

    private string ResolvePath(string path) => Path.GetFullPath(
        Path.IsPathRooted(path) ? path : Path.Combine(_environment.ContentRootPath, path));

    private static string ResolveTrustedRelative(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("Model artifact paths must be non-empty and relative.");
        }
        return EnsureInsideRoot(root, Path.Combine(root, relative));
    }

    private static string EnsureInsideRoot(string root, string candidate)
    {
        var resolvedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolvedCandidate = Path.GetFullPath(candidate);
        if (!resolvedCandidate.StartsWith(resolvedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Model package path escapes the configured trusted root.");
        }
        return resolvedCandidate;
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var actualBytes = Encoding.ASCII.GetBytes(actual);
        var expectedBytes = Encoding.ASCII.GetBytes(expected.Trim().ToLowerInvariant());
        if (actualBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
        {
            throw new InvalidOperationException($"Checksum mismatch for {Path.GetFileName(path)}.");
        }
    }

    private static void RequireString(JsonElement element, string property, string expected)
    {
        var actual = ReadRequiredString(element, property);
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {property} '{expected}', found '{actual}'.");
        }
    }

    private static string ReadRequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"Model package property '{property}' is required.");
        }
        return value.GetString()!;
    }

    private static string? ReadOptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadNestedNumber(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out var parentValue) &&
        parentValue.ValueKind == JsonValueKind.Object &&
        parentValue.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? number
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Model metadata property '{property}' must be an array.");
        }
        var values = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new InvalidOperationException($"Model metadata property '{property}' contains an invalid value.");
            }
            values.Add(item.GetString()!);
        }
        return values;
    }

    public sealed record PackageSnapshot(
        bool IsReady,
        string Target,
        string Market,
        string Scope,
        string DisplayName,
        string ManifestPath,
        string? Error,
        string? Version,
        string? TrainedThrough,
        string? FeatureSet,
        string? Algorithm,
        string? TrainedAt,
        string? DatasetSha256,
        double? TestMae,
        IReadOnlyList<string> Features,
        IReadOnlyList<string> CategoricalFeatures,
        IReadOnlyList<string> NumericFeatures,
        IReadOnlyList<string> Warnings)
    {
        public static PackageSnapshot Pending(
            NewGenerationModelDefinition definition,
            string version,
            string manifestPath,
            string error) => new(
                false,
                definition.Target,
                definition.Market,
                definition.Scope,
                definition.DisplayName,
                manifestPath,
                error,
                version,
                null,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
    }
}
