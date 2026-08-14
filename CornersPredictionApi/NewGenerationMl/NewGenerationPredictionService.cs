using System.Diagnostics;

namespace CornersPredictionApi.NewGenerationMl;

public sealed class NewGenerationPredictionService
{
    private readonly NewGenerationModelPackage _packages;
    private readonly NewGenerationFeatureBuilder _featureBuilder;
    private readonly NewGenerationPythonRunner _python;
    private readonly ILogger<NewGenerationPredictionService> _logger;

    public NewGenerationPredictionService(
        NewGenerationModelPackage packages,
        NewGenerationFeatureBuilder featureBuilder,
        NewGenerationPythonRunner python,
        ILogger<NewGenerationPredictionService> logger)
    {
        _packages = packages;
        _featureBuilder = featureBuilder;
        _python = python;
        _logger = logger;
    }

    public NewGenerationModelInfo GetModelInfo() =>
        GetModelInfo(NewGenerationModelDefinitions.HomeCorners);

    public NewGenerationModelInfo GetModelInfo(string target) => _packages.GetInfo(target);

    public NewGenerationModelCatalogInfo GetCatalogInfo() => _packages.GetCatalogInfo();

    public async Task<NewGenerationModelInfo> GetHealthAsync(CancellationToken cancellationToken) =>
        await GetHealthAsync(NewGenerationModelDefinitions.HomeCorners, cancellationToken);

    public async Task<NewGenerationModelInfo> GetHealthAsync(
        string target,
        CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogHealthAsync(cancellationToken);
        return catalog.Models.Single(model => model.Target.Equals(target, StringComparison.Ordinal));
    }

    public async Task<NewGenerationModelCatalogInfo> GetCatalogHealthAsync(
        CancellationToken cancellationToken)
    {
        var snapshots = _packages.GetSnapshots();
        var ready = snapshots.Where(snapshot => snapshot.IsReady).ToArray();
        if (ready.Length == 0)
        {
            throw new NewGenerationModelNotReadyException(
                "No validated new-generation production model is active.");
        }

        var pythonHealth = await _python.HealthAsync(ready, cancellationToken);
        var loadedTargets = pythonHealth.Models
            .Where(model => model.Loaded)
            .Select(model => model.Target)
            .ToHashSet(StringComparer.Ordinal);
        var models = snapshots
            .Select(snapshot => NewGenerationModelPackage.ToInfo(
                snapshot,
                loadedTargets.Contains(snapshot.Target)))
            .ToArray();
        return NewGenerationModelPackage.BuildCatalog(
            models,
            loaded: ready.All(snapshot => loadedTargets.Contains(snapshot.Target)));
    }

    public async Task<NewGenerationPredictionResult> PredictAsync(
        NewGenerationPredictionRequest request,
        CancellationToken cancellationToken) =>
        await PredictAsync(request, NewGenerationModelDefinitions.HomeCorners, cancellationToken);

    public async Task<NewGenerationPredictionResult> PredictAsync(
        NewGenerationPredictionRequest request,
        string target,
        CancellationToken cancellationToken)
    {
        var selectedPackage = RequireReadyPackage(target);
        var allReadyPackages = GetReadyPackages();
        var started = Stopwatch.GetTimestamp();
        var built = await _featureBuilder.BuildAsync(request, cancellationToken);
        var payload = SelectFeatures(selectedPackage, built.Features);
        var payloads = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal)
        {
            [target] = payload
        };
        var predictions = await _python.PredictManyAsync(allReadyPackages, payloads, cancellationToken);
        var prediction = predictions.SingleOrDefault();
        if (prediction is null)
        {
            throw new InvalidOperationException("Python returned no prediction.");
        }
        var duration = (long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return MapPrediction(prediction, selectedPackage, built, payload, duration);
    }

    public async Task<NewGenerationBatchPredictionResult> PredictAllAsync(
        NewGenerationPredictionRequest request,
        CancellationToken cancellationToken)
    {
        var packages = GetReadyPackages();
        var started = Stopwatch.GetTimestamp();
        var built = await _featureBuilder.BuildAsync(request, cancellationToken);
        var payloads = packages.ToDictionary(
            package => package.Target,
            package => SelectFeatures(package, built.Features),
            StringComparer.Ordinal);
        var pythonPredictions = await _python.PredictManyAsync(packages, payloads, cancellationToken);
        var predictionsByTarget = pythonPredictions.ToDictionary(
            prediction => prediction.Target,
            StringComparer.Ordinal);
        var missingPredictions = packages
            .Where(package => !predictionsByTarget.ContainsKey(package.Target))
            .Select(package => package.Target)
            .ToArray();
        if (missingPredictions.Length > 0 || predictionsByTarget.Count != packages.Count)
        {
            throw new InvalidOperationException(
                "Python returned an incomplete or duplicated prediction set: " +
                string.Join(", ", missingPredictions));
        }

        var duration = (long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        var results = packages
            .Select(package => MapPrediction(
                predictionsByTarget[package.Target],
                package,
                built,
                payloads[package.Target],
                duration))
            .ToArray();
        var unavailable = _packages.GetSnapshots()
            .Where(package => !package.IsReady)
            .Select(package => $"{package.DisplayName}: {package.Error}")
            .ToArray();
        var warnings = built.Warnings
            .Concat(unavailable)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _logger.LogInformation(
            "New-generation batch prediction completed for {ModelCount} targets in {DurationMs} ms",
            results.Length,
            duration);
        return new NewGenerationBatchPredictionResult
        {
            Match = built.Match,
            Predictions = results,
            FeaturePayloads = payloads,
            Warnings = warnings,
            DurationMilliseconds = duration
        };
    }

    private NewGenerationPredictionResult MapPrediction(
        PythonPredictionEnvelope prediction,
        NewGenerationModelPackage.PackageSnapshot package,
        NewGenerationFeatureBuildResult built,
        IReadOnlyDictionary<string, object?> payload,
        long duration)
    {
        if (!prediction.Target.Equals(package.Target, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Python returned target '{prediction.Target}' while '{package.Target}' was requested.");
        }
        _logger.LogInformation(
            "New-generation prediction completed for target {Target}, version {Version}, in {DurationMs} ms",
            prediction.Target,
            prediction.ModelVersion,
            duration);
        return new NewGenerationPredictionResult
        {
            Target = prediction.Target,
            Market = package.Market,
            Scope = package.Scope,
            DisplayName = package.DisplayName,
            PredictionRaw = prediction.PredictionRaw,
            PredictionClipped = prediction.PredictionClipped,
            PredictionRounded = prediction.PredictionRounded,
            ModelVersion = prediction.ModelVersion,
            TrainedThrough = prediction.TrainedThrough,
            FeatureSet = prediction.FeatureSet,
            Warnings = built.Warnings
                .Concat(package.Warnings)
                .Concat(prediction.Warnings ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Match = built.Match,
            DurationMilliseconds = duration,
            FeaturePayload = payload
        };
    }

    private static IReadOnlyDictionary<string, object?> SelectFeatures(
        NewGenerationModelPackage.PackageSnapshot package,
        IReadOnlyDictionary<string, object?> builtFeatures)
    {
        var missing = package.Features
            .Where(feature => !builtFeatures.ContainsKey(feature))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"Model {package.Target} requires features that the webapp cannot construct: " +
                string.Join(", ", missing));
        }
        var selected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var feature in package.Features)
        {
            selected[feature] = builtFeatures[feature];
        }
        var leaked = selected.Keys
            .Where(key => key.StartsWith("Target", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (leaked.Length > 0)
        {
            throw new InvalidOperationException("Target features are forbidden at inference.");
        }
        return selected;
    }

    private IReadOnlyList<NewGenerationModelPackage.PackageSnapshot> GetReadyPackages()
    {
        var ready = _packages.GetSnapshots().Where(snapshot => snapshot.IsReady).ToArray();
        if (ready.Length == 0)
        {
            throw new NewGenerationModelNotReadyException(
                "No validated new-generation production model is active.");
        }
        return ready;
    }

    private NewGenerationModelPackage.PackageSnapshot RequireReadyPackage(string target)
    {
        var snapshot = _packages.GetSnapshot(target);
        if (!snapshot.IsReady)
        {
            throw new NewGenerationModelNotReadyException(
                snapshot.Error ?? $"No validated production model is active for {target}.");
        }
        return snapshot;
    }
}
