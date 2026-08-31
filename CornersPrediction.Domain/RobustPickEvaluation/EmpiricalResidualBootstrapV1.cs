using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface ISettlementAdapter
{
    string SettlementVersion { get; }

    SettlementOutcome Settle(decimal line, SelectionSide side, int actualResult);
}

public interface IPredictiveDistributionService
{
    PredictiveDistributionResult Build(
        PredictiveDistributionRequest request,
        IReadOnlyCollection<HistoricalResidualObservation> observations,
        EmpiricalResidualBootstrapOptions options,
        ISettlementAdapter settlementAdapter,
        CancellationToken cancellationToken = default);
}

public sealed class EmpiricalResidualBootstrapV1 : IPredictiveDistributionService
{
    public const string Method = "EmpiricalResidualBootstrap";
    public const string Version = "empirical-residual-bootstrap-v1";

    public PredictiveDistributionResult Build(
        PredictiveDistributionRequest request,
        IReadOnlyCollection<HistoricalResidualObservation> observations,
        EmpiricalResidualBootstrapOptions options,
        ISettlementAdapter settlementAdapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settlementAdapter);
        Validate(request, options);

        var seed = DeterministicSeed.Derive(request);
        var warnings = new HashSet<RobustReasonCode>();
        var sameFamily = observations
            .Where(item => item.MarketFamily == request.MarketFamily)
            .ToArray();
        var temporal = new List<HistoricalResidualObservation>(sameFamily.Length);
        foreach (var observation in sameFamily)
        {
            if (!AllUtc(observation)
                || observation.FixtureEndUtc < observation.FixtureStartUtc)
            {
                warnings.Add(RobustReasonCode.LookaheadDataDetected);
                continue;
            }

            if (observation.ModelTrainedThroughUtc >= observation.FixtureStartUtc)
            {
                warnings.Add(RobustReasonCode.ModelTrainedAfterFixture);
                continue;
            }

            if (observation.ModelTrainedThroughUtc > observation.PredictionAsOfUtc)
            {
                warnings.Add(RobustReasonCode.LookaheadDataDetected);
                continue;
            }

            var outcomeAvailableUtc = observation.OutcomeAvailableUtc
                ?? observation.FixtureEndUtc + options.OutcomeAvailabilityLag;
            if (outcomeAvailableUtc.Kind != DateTimeKind.Utc
                || outcomeAvailableUtc < observation.FixtureEndUtc
                || observation.PredictionAsOfUtc >= observation.FixtureStartUtc
                || observation.PredictionAsOfUtc > request.EvaluationAsOfUtc
                || outcomeAvailableUtc > request.EvaluationAsOfUtc)
            {
                warnings.Add(RobustReasonCode.LookaheadDataDetected);
                continue;
            }

            if (observation.ActualResult < 0m
                || observation.HistoricalPreMatchPrediction < 0m
                || observation.DataQualityScore <= 0m)
            {
                continue;
            }

            temporal.Add(observation);
        }

        var weighted = SelectFallback(request, temporal, options);
        if (weighted.Items.Count == 0)
        {
            warnings.Add(RobustReasonCode.ResidualSampleTooSmall);
            warnings.Add(RobustReasonCode.ErrorScaleUnavailable);
            return new PredictiveDistributionResult(
                null,
                ResidualFallbackLevel.Unavailable,
                0,
                0m,
                options.MinimumEffectiveN,
                options.TargetEffectiveN,
                ErrorScaleMethod.Unavailable,
                null,
                seed,
                warnings.OrderBy(item => item).ToArray());
        }

        if (weighted.EffectiveN < options.MinimumEffectiveN)
        {
            warnings.Add(RobustReasonCode.ResidualSampleTooSmall);
        }

        var sourceScope = weighted.Items.All(item => item.Observation.SourceScope == ResidualSourceScope.AllCandidates)
            ? ResidualSourceScope.AllCandidates
            : ResidualSourceScope.SelectedPicksOnly;
        if (sourceScope == ResidualSourceScope.SelectedPicksOnly)
        {
            warnings.Add(RobustReasonCode.EvidenceInsufficient);
        }

        var error = CalculateErrorScale(weighted.Items, options);
        if (!error.Scale.HasValue)
        {
            warnings.Add(RobustReasonCode.ErrorScaleUnavailable);
        }

        var random = new StableRandom(seed);
        var cumulative = BuildCumulativeWeights(weighted.Items);
        var results = new int[options.SimulationCount];
        var settlementCounts = new int[5];
        for (var index = 0; index < results.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sampledIndex = SampleIndex(cumulative, random.NextUnitDecimal());
            var residual = weighted.Items[sampledIndex].Residual;
            var continuous = Math.Max(0m, request.ReconciledPrediction + residual);
            var discrete = DeterministicStochasticRound(continuous, random);
            results[index] = discrete;
            settlementCounts[(int)settlementAdapter.Settle(request.Line, request.Side, discrete)]++;
        }

        Array.Sort(results);
        var mean = results.Select(item => (decimal)item).Average();
        var variance = results.Select(item =>
        {
            var difference = item - mean;
            return difference * difference;
        }).Average();
        var median = Quantile(results, 0.50m);
        var absoluteDeviations = results
            .Select(item => Math.Abs(item - median))
            .OrderBy(item => item)
            .ToArray();
        var simulationMad = Quantile(absoluteDeviations, 0.50m);
        var histogram = results
            .GroupBy(item => item)
            .ToDictionary(group => group.Key, group => group.Count());
        var count = (decimal)results.Length;
        var distribution = new PredictiveDistribution(
            Quantile(results, 0.01m),
            Quantile(results, 0.05m),
            Quantile(results, 0.10m),
            Quantile(results, 0.25m),
            median,
            Quantile(results, 0.75m),
            Quantile(results, 0.90m),
            Quantile(results, 0.95m),
            Quantile(results, 0.99m),
            mean,
            (decimal)Math.Sqrt((double)variance),
            simulationMad,
            error.Scale,
            weighted.EffectiveN,
            results.Length,
            Method,
            $"{Version};settlement={settlementAdapter.SettlementVersion}",
            histogram,
            settlementCounts[(int)SettlementOutcome.Win] / count,
            settlementCounts[(int)SettlementOutcome.HalfWin] / count,
            settlementCounts[(int)SettlementOutcome.Push] / count,
            settlementCounts[(int)SettlementOutcome.HalfLoss] / count,
            settlementCounts[(int)SettlementOutcome.Loss] / count);

        return new PredictiveDistributionResult(
            distribution,
            weighted.Level,
            weighted.Items.Count,
            weighted.EffectiveN,
            options.MinimumEffectiveN,
            options.TargetEffectiveN,
            error.Method,
            sourceScope,
            seed,
            warnings.OrderBy(item => item).ToArray());
    }

    private static FallbackSelection SelectFallback(
        PredictiveDistributionRequest request,
        IReadOnlyCollection<HistoricalResidualObservation> observations,
        EmpiricalResidualBootstrapOptions options)
    {
        FallbackSelection? broadest = null;
        foreach (var level in Enum.GetValues<ResidualFallbackLevel>()
                     .Where(level => level != ResidualFallbackLevel.Unavailable))
        {
            var matches = observations
                .Where(item => MatchesLevel(item, request, level, options.LineBandWidth))
                .OrderBy(item => item.FixtureId)
                .ThenBy(item => item.PredictionAsOfUtc)
                .ThenBy(item => item.Line)
                .Select(item => new WeightedResidual(
                    item,
                    item.ActualResult - item.HistoricalPreMatchPrediction,
                    CalculateWeight(item, request, options)))
                .Where(item => item.Weight > options.Epsilon)
                .ToArray();
            if (matches.Length == 0)
            {
                continue;
            }

            var effectiveN = EffectiveSampleSize(matches.Select(item => item.Weight));
            var candidate = new FallbackSelection(level, matches, effectiveN);
            broadest = candidate;
            if (effectiveN >= options.MinimumEffectiveN)
            {
                return candidate;
            }
        }

        return broadest ?? new FallbackSelection(
            ResidualFallbackLevel.Unavailable,
            [],
            0m);
    }

    private static bool MatchesLevel(
        HistoricalResidualObservation item,
        PredictiveDistributionRequest request,
        ResidualFallbackLevel level,
        decimal lineBandWidth)
    {
        var exactMarket = EqualsNormalized(item.MarketType, request.MarketType);
        var sameSide = item.Side == request.Side;
        var sameLeague = EqualsKnown(item.League, request.League);
        var sameScope = item.MarketScope == request.MarketScope;
        var sameLineBand = decimal.Floor(item.Line / lineBandWidth)
            == decimal.Floor(request.Line / lineBandWidth);

        return level switch
        {
            ResidualFallbackLevel.ExactMarketSideLeagueLineBand =>
                exactMarket && sameSide && sameLeague && sameLineBand,
            ResidualFallbackLevel.ExactMarketSideLeague => exactMarket && sameSide && sameLeague,
            ResidualFallbackLevel.ExactMarketSide => exactMarket && sameSide,
            ResidualFallbackLevel.MarketFamilyScopeSide => sameScope && sameSide,
            ResidualFallbackLevel.MarketFamilyScope => sameScope,
            ResidualFallbackLevel.MarketFamily => true,
            _ => false
        };
    }

    private static bool EqualsNormalized(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool EqualsKnown(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && EqualsNormalized(left, right);

    private static bool AllUtc(HistoricalResidualObservation observation) =>
        observation.FixtureStartUtc.Kind == DateTimeKind.Utc
        && observation.FixtureEndUtc.Kind == DateTimeKind.Utc
        && observation.PredictionAsOfUtc.Kind == DateTimeKind.Utc
        && observation.ModelTrainedThroughUtc.Kind == DateTimeKind.Utc;

    private static decimal CalculateWeight(
        HistoricalResidualObservation item,
        PredictiveDistributionRequest request,
        EmpiricalResidualBootstrapOptions options)
    {
        var ageDays = Math.Max(0d, (request.EvaluationAsOfUtc - item.FixtureEndUtc).TotalDays);
        var recency = Math.Exp(-Math.Log(2d) * ageDays / (double)options.RecencyHalfLifeDays);
        var line = options.UseLineSimilarity
            ? Math.Exp(-(double)(Math.Abs(item.Line - request.Line) / options.LineSimilarityScale))
            : 1d;
        var odds = 1d;
        if (options.UseOddsSimilarity)
        {
            odds = item.Odds.HasValue && request.Odds.HasValue
                ? Math.Exp(-(double)(Math.Abs(item.Odds.Value - request.Odds.Value) / options.OddsSimilarityScale))
                : 0.5d;
        }

        var model = EqualsNormalized(item.ModelVersionFamily, request.ModelVersion)
            ? options.SameModelVersionWeight
            : options.DifferentModelVersionWeight;
        var league = EqualsKnown(item.League, request.League)
            ? options.SameLeagueWeight
            : options.DifferentLeagueWeight;
        var quality = Math.Clamp(item.DataQualityScore, 0m, 1m);
        return (decimal)(recency * line * odds) * model * league * quality;
    }

    public static decimal EffectiveSampleSize(IEnumerable<decimal> weights)
    {
        var materialized = weights.Where(weight => weight > 0m).ToArray();
        if (materialized.Length == 0)
        {
            return 0m;
        }

        var sum = materialized.Sum();
        var squareSum = materialized.Sum(weight => weight * weight);
        return squareSum <= 0m ? 0m : sum * sum / squareSum;
    }

    private static ErrorScale CalculateErrorScale(
        IReadOnlyList<WeightedResidual> items,
        EmpiricalResidualBootstrapOptions options)
    {
        var median = WeightedMedian(items.Select(item => (item.Residual, item.Weight)));
        var mad = WeightedMedian(items.Select(item => (Math.Abs(item.Residual - median), item.Weight)));
        var robustSigma = 1.4826m * mad;
        if (robustSigma > options.Epsilon)
        {
            return new ErrorScale(robustSigma, ErrorScaleMethod.RobustMad);
        }

        var weightSum = items.Sum(item => item.Weight);
        var rmse = (decimal)Math.Sqrt((double)(items.Sum(item =>
            item.Weight * item.Residual * item.Residual) / weightSum));
        if (rmse > options.Epsilon)
        {
            return new ErrorScale(rmse, ErrorScaleMethod.WeightedRmse);
        }

        var mae = items.Sum(item => item.Weight * Math.Abs(item.Residual)) / weightSum;
        if (mae > options.Epsilon)
        {
            return new ErrorScale(mae, ErrorScaleMethod.WeightedMae);
        }

        if (options.ConfiguredModelMae is > 0m)
        {
            return new ErrorScale(options.ConfiguredModelMae, ErrorScaleMethod.ConfiguredModelMae);
        }

        return new ErrorScale(null, ErrorScaleMethod.Unavailable);
    }

    private static decimal WeightedMedian(IEnumerable<(decimal Value, decimal Weight)> observations)
    {
        var ordered = observations
            .Where(item => item.Weight > 0m)
            .OrderBy(item => item.Value)
            .ToArray();
        var target = ordered.Sum(item => item.Weight) / 2m;
        var cumulative = 0m;
        foreach (var item in ordered)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
            {
                return item.Value;
            }
        }

        return ordered[^1].Value;
    }

    private static decimal[] BuildCumulativeWeights(IReadOnlyList<WeightedResidual> items)
    {
        var total = items.Sum(item => item.Weight);
        var cumulative = new decimal[items.Count];
        var running = 0m;
        for (var index = 0; index < items.Count; index++)
        {
            running += items[index].Weight / total;
            cumulative[index] = running;
        }
        cumulative[^1] = 1m;
        return cumulative;
    }

    private static int SampleIndex(IReadOnlyList<decimal> cumulative, decimal draw)
    {
        var low = 0;
        var high = cumulative.Count - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (draw <= cumulative[middle])
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }
        return low;
    }

    private static int DeterministicStochasticRound(decimal value, StableRandom random)
    {
        var bounded = Math.Clamp(value, 0m, int.MaxValue);
        var lower = decimal.Floor(bounded);
        var fraction = bounded - lower;
        return checked((int)lower + (random.NextUnitDecimal() < fraction ? 1 : 0));
    }

    private static decimal Quantile(IReadOnlyList<int> sorted, decimal probability)
    {
        var position = probability * (sorted.Count - 1);
        var lowerIndex = (int)decimal.Floor(position);
        var upperIndex = (int)decimal.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }
        var fraction = position - lowerIndex;
        return sorted[lowerIndex] + fraction * (sorted[upperIndex] - sorted[lowerIndex]);
    }

    private static decimal Quantile(IReadOnlyList<decimal> sorted, decimal probability)
    {
        var position = probability * (sorted.Count - 1);
        var lowerIndex = (int)decimal.Floor(position);
        var upperIndex = (int)decimal.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }
        var fraction = position - lowerIndex;
        return sorted[lowerIndex] + fraction * (sorted[upperIndex] - sorted[lowerIndex]);
    }

    private static void Validate(
        PredictiveDistributionRequest request,
        EmpiricalResidualBootstrapOptions options)
    {
        if (request.EvaluationAsOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("EvaluationAsOfUtc must be UTC.", nameof(request));
        }
        if (request.Line < 0m || request.ReconciledPrediction < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Line and prediction cannot be negative.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MarketType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RobustnessVersion);
        if (options.OutcomeAvailabilityLag < TimeSpan.Zero
            || options.SimulationCount <= 0
            || options.ProbabilityLowerQuantile is < 0m or > 1m
            || options.ProbabilityUpperQuantile is < 0m or > 1m
            || options.ProbabilityLowerQuantile > options.ProbabilityUpperQuantile
            || options.MinimumEffectiveN < 0m
            || options.TargetEffectiveN <= 0m
            || options.RecencyHalfLifeDays <= 0m
            || options.LineBandWidth <= 0m
            || options.LineSimilarityScale <= 0m
            || options.OddsSimilarityScale <= 0m
            || options.SameModelVersionWeight < 0m
            || options.DifferentModelVersionWeight < 0m
            || options.SameLeagueWeight < 0m
            || options.DifferentLeagueWeight < 0m
            || options.Epsilon <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid residual bootstrap options.");
        }
    }

    private sealed record WeightedResidual(
        HistoricalResidualObservation Observation,
        decimal Residual,
        decimal Weight);

    private sealed record FallbackSelection(
        ResidualFallbackLevel Level,
        IReadOnlyList<WeightedResidual> Items,
        decimal EffectiveN);

    private sealed record ErrorScale(decimal? Scale, ErrorScaleMethod Method);

    private sealed class StableRandom
    {
        private ulong _state;

        public StableRandom(ulong seed) => _state = seed;

        public decimal NextUnitDecimal()
        {
            var value = NextUInt64() >> 11;
            return (decimal)value / 9007199254740992m;
        }

        private ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var result = _state;
            result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9UL;
            result = (result ^ (result >> 27)) * 0x94D049BB133111EBUL;
            return result ^ (result >> 31);
        }
    }

    private static class DeterministicSeed
    {
        public static ulong Derive(PredictiveDistributionRequest request)
        {
            var canonical = string.Join('|',
                request.FixtureId.ToString(CultureInfo.InvariantCulture),
                request.MarketType.Trim().ToUpperInvariant(),
                request.Side.ToString().ToUpperInvariant(),
                request.Line.ToString("0.############################", CultureInfo.InvariantCulture),
                request.ModelVersion.Trim(),
                request.EvaluationAsOfUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                request.RobustnessVersion.Trim());
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return BinaryPrimitives.ReadUInt64BigEndian(hash);
        }
    }
}
