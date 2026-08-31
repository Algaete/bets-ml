namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface IPredictionReconciliationService
{
    PredictionReconciliationResult Reconcile(
        IReadOnlyCollection<PredictionComponent> components,
        IReadOnlyCollection<ComponentValidationEvidence> validation,
        PredictionReconciliationOptions options,
        string reconciliationVersion);
}

public sealed class PredictionReconciliationService : IPredictionReconciliationService
{
    public PredictionReconciliationResult Reconcile(
        IReadOnlyCollection<PredictionComponent> components,
        IReadOnlyCollection<ComponentValidationEvidence> validation,
        PredictionReconciliationOptions options,
        string reconciliationVersion)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationVersion);
        ValidateOptions(options);

        var usable = components
            .Where(component => component.IsUsable
                && component.DataQualityScore > 0m
                && string.IsNullOrWhiteSpace(component.ExclusionReason)
                && (!options.EvaluationAsOfUtc.HasValue
                    || (component.AsOfUtc.Kind == DateTimeKind.Utc
                        && component.AsOfUtc <= options.EvaluationAsOfUtc.Value)))
            .GroupBy(component => component.ComponentType)
            .Select(group => group.OrderByDescending(component => component.AsOfUtc).First())
            .ToArray();
        if (usable.Length == 0)
        {
            return new PredictionReconciliationResult(
                null,
                new Dictionary<PredictionComponentType, decimal>(),
                ReconciliationFallbackReason.NoUsableComponents,
                reconciliationVersion,
                0m);
        }

        var direct = usable.FirstOrDefault(component => component.ComponentType == PredictionComponentType.Direct);
        var evidenceByType = validation
            .GroupBy(item => item.ComponentType)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.EffectiveSampleSize).First());
        var validated = usable
            .Where(component => evidenceByType.TryGetValue(component.ComponentType, out var evidence)
                && evidence.EffectiveSampleSize >= options.MinimumValidationEffectiveN
                && evidence.ValidationError > options.Epsilon)
            .Select(component => (Component: component, Evidence: evidenceByType[component.ComponentType]))
            .ToArray();

        if (validated.Length == 0)
        {
            if (direct is not null)
            {
                return DirectFallback(
                    direct,
                    ReconciliationFallbackReason.InsufficientOutOfSampleValidation,
                    reconciliationVersion);
            }

            return new PredictionReconciliationResult(
                null,
                new Dictionary<PredictionComponentType, decimal>(),
                ReconciliationFallbackReason.DirectPredictionUnavailable,
                reconciliationVersion,
                0m);
        }

        // A context or scenario component without its own out-of-sample validation never receives weight.
        var rawWeights = validated.ToDictionary(
            item => item.Component.ComponentType,
            item => Math.Clamp(item.Component.DataQualityScore, 0m, 1m)
                / Math.Max(item.Evidence.ValidationError * item.Evidence.ValidationError, options.Epsilon));
        var normalized = Normalize(rawWeights);
        normalized = LimitMaximumWeight(normalized, options.MaximumSingleSourceWeight);

        var validationN = validated.Sum(item => item.Evidence.EffectiveSampleSize);
        if (direct is not null && validationN < options.TargetValidationEffectiveN)
        {
            var evidenceShare = Math.Clamp(validationN / options.TargetValidationEffectiveN, 0m, 1m);
            foreach (var key in normalized.Keys.ToArray())
            {
                normalized[key] *= evidenceShare;
            }

            normalized.TryGetValue(PredictionComponentType.Direct, out var directWeight);
            normalized[PredictionComponentType.Direct] = directWeight + (1m - evidenceShare);
            normalized = Normalize(normalized);
        }

        var values = usable.ToDictionary(component => component.ComponentType, component => component.PredictedValue);
        var reconciled = normalized.Sum(item => item.Value * values[item.Key]);
        return new PredictionReconciliationResult(
            reconciled,
            normalized,
            ReconciliationFallbackReason.None,
            reconciliationVersion,
            validationN);
    }

    private static PredictionReconciliationResult DirectFallback(
        PredictionComponent direct,
        ReconciliationFallbackReason reason,
        string version) => new(
            direct.PredictedValue,
            new Dictionary<PredictionComponentType, decimal>
            {
                [PredictionComponentType.Direct] = 1m
            },
            reason,
            version,
            0m);

    private static Dictionary<PredictionComponentType, decimal> Normalize(
        IReadOnlyDictionary<PredictionComponentType, decimal> weights)
    {
        var total = weights.Values.Sum();
        if (total <= 0m)
        {
            throw new InvalidOperationException("Reconciliation weights must have positive mass.");
        }

        return weights.ToDictionary(item => item.Key, item => item.Value / total);
    }

    private static Dictionary<PredictionComponentType, decimal> LimitMaximumWeight(
        Dictionary<PredictionComponentType, decimal> weights,
        decimal maximum)
    {
        if (weights.Count <= 1 || maximum >= 1m)
        {
            return weights;
        }

        // A cap below 1/N is mathematically impossible.
        var effectiveMaximum = Math.Max(maximum, 1m / weights.Count);
        var result = new Dictionary<PredictionComponentType, decimal>();
        var remaining = weights.ToDictionary(item => item.Key, item => item.Value);
        var remainingMass = 1m;

        while (remaining.Count > 0)
        {
            var rawMass = remaining.Values.Sum();
            var capped = remaining
                .Where(item => remainingMass * item.Value / rawMass > effectiveMaximum)
                .Select(item => item.Key)
                .ToArray();
            if (capped.Length == 0)
            {
                foreach (var item in remaining)
                {
                    result[item.Key] = remainingMass * item.Value / rawMass;
                }
                break;
            }

            foreach (var key in capped)
            {
                result[key] = effectiveMaximum;
                remaining.Remove(key);
                remainingMass -= effectiveMaximum;
            }
        }

        return result;
    }

    private static void ValidateOptions(PredictionReconciliationOptions options)
    {
        if (options.Epsilon <= 0m
            || options.MinimumValidationEffectiveN < 0m
            || options.TargetValidationEffectiveN <= 0m
            || options.MaximumSingleSourceWeight <= 0m
            || options.MaximumSingleSourceWeight > 1m
            || (options.EvaluationAsOfUtc.HasValue
                && options.EvaluationAsOfUtc.Value.Kind != DateTimeKind.Utc))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid reconciliation options.");
        }
    }
}
