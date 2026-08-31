namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface IPredictionConsensusService
{
    PredictionConsensusResult Evaluate(PredictionConsensusRequest request);
}

public sealed class PredictionConsensusService : IPredictionConsensusService
{
    public PredictionConsensusResult Evaluate(PredictionConsensusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NormalizationEpsilon <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.NormalizationEpsilon));
        }

        var components = new List<PredictionComponent>();
        Add(components, PredictionComponentType.Direct, request.DirectPrediction);

        decimal? componentPrediction = request.HomePrediction.HasValue && request.AwayPrediction.HasValue
            ? request.HomePrediction.Value + request.AwayPrediction.Value
            : null;
        Add(components, PredictionComponentType.HomeAwaySum, componentPrediction);
        Add(components, PredictionComponentType.Context, request.ContextPrediction);
        Add(components, PredictionComponentType.Reconciled, request.ReconciledPrediction);

        components.AddRange(request.AdditionalComponents.Where(component =>
            component.IsUsable
            && string.IsNullOrWhiteSpace(component.ExclusionReason)
            && (!request.EvaluationAsOfUtc.HasValue || component.AsOfUtc <= request.EvaluationAsOfUtc.Value)));

        if (components.Count == 0)
        {
            throw new InvalidOperationException("At least one usable prediction is required for consensus.");
        }

        var values = components.Select(component => component.PredictedValue).ToArray();
        var minimum = values.Min();
        var maximum = values.Max();
        var range = maximum - minimum;
        var worst = request.Side == SelectionSide.Under ? maximum : minimum;
        var worstDistance = Distance(request.Side, request.Line, worst);
        var denominator = Math.Max(Math.Abs(request.ErrorScale), request.NormalizationEpsilon);
        var normalizedRange = range / denominator;
        decimal? coherenceGap = request.DirectPrediction.HasValue && componentPrediction.HasValue
            ? Math.Abs(request.DirectPrediction.Value - componentPrediction.Value)
            : null;
        var normalizedCoherence = coherenceGap / denominator;

        var probabilityAgreement = CalculateProbabilityAgreement(components);
        var sideAgreement = components.All(component =>
            Distance(request.Side, request.Line, component.PredictedValue) >= 0m);

        return new PredictionConsensusResult(
            request.DirectPrediction,
            componentPrediction,
            request.ContextPrediction,
            request.ReconciledPrediction,
            DistanceOrNull(request.Side, request.Line, request.DirectPrediction),
            DistanceOrNull(request.Side, request.Line, componentPrediction),
            DistanceOrNull(request.Side, request.Line, request.ContextPrediction),
            DistanceOrNull(request.Side, request.Line, request.ReconciledPrediction),
            minimum,
            maximum,
            range,
            coherenceGap,
            worst,
            worstDistance,
            worstDistance / denominator,
            normalizedRange,
            normalizedCoherence,
            sideAgreement,
            ExpNegative(normalizedRange),
            probabilityAgreement,
            normalizedCoherence.HasValue ? ExpNegative(normalizedCoherence.Value) : null,
            components.AsReadOnly());
    }

    private static void Add(
        ICollection<PredictionComponent> components,
        PredictionComponentType type,
        decimal? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        components.Add(new PredictionComponent(
            type,
            value.Value,
            null,
            1m,
            true,
            null,
            DateTime.MinValue,
            null,
            1m));
    }

    private static decimal? DistanceOrNull(SelectionSide side, decimal line, decimal? prediction) =>
        prediction.HasValue ? Distance(side, line, prediction.Value) : null;

    public static decimal Distance(SelectionSide side, decimal line, decimal prediction) =>
        side == SelectionSide.Under ? line - prediction : prediction - line;

    private static decimal? CalculateProbabilityAgreement(IReadOnlyCollection<PredictionComponent> components)
    {
        var probabilities = components
            .Where(component => component.ProbabilityForSelection is >= 0m and <= 1m)
            .ToArray();
        if (probabilities.Length < 2)
        {
            return null;
        }

        var weights = probabilities
            .Select(component => Math.Max(0.000001m, component.Weight * component.DataQualityScore))
            .ToArray();
        var weightSum = weights.Sum();
        var mean = probabilities.Select((component, index) =>
            component.ProbabilityForSelection!.Value * weights[index]).Sum() / weightSum;
        var variance = probabilities.Select((component, index) =>
        {
            var difference = component.ProbabilityForSelection!.Value - mean;
            return weights[index] * difference * difference;
        }).Sum() / weightSum;
        var standardDeviation = (decimal)Math.Sqrt((double)variance);

        // A 0.25 probability standard deviation is deliberately treated as very weak agreement.
        return ExpNegative(standardDeviation / 0.25m);
    }

    private static decimal ExpNegative(decimal value)
    {
        var score = (decimal)Math.Exp(-(double)Math.Max(0m, value));
        return Math.Clamp(score, 0m, 1m);
    }
}
