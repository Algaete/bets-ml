using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RobustPickBacktest;

public sealed class WalkForwardBacktestEngine
{
    private const int EceBinCount = 10;
    private const decimal LogLossEpsilon = 0.000000000001m;

    public RobustPickBacktestReport Run(
        IReadOnlyCollection<ResolvedEvaluation> input,
        BacktestConfiguration configuration,
        string inputSha256)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputSha256);
        ValidateConfiguration(configuration);
        ValidateRows(input, configuration);

        var rows = PrepareRows(input, configuration);
        if (rows.Count == 0)
        {
            return EmptyReport(input.Count, configuration, inputSha256);
        }

        var gridPolicies = configuration.ThresholdGrid.Enabled
            ? BuildGridPolicies(configuration.ThresholdGrid)
            : [];
        var firstTestStart = configuration.FirstTestStartUtc?.ToUniversalTime()
            ?? rows[0].EvaluationAsOfUtc
                .Add(configuration.TrainingWindow)
                .Add(configuration.ValidationWindow);
        var lastEvaluation = rows[^1].EvaluationAsOfUtc;
        var folds = new List<WalkForwardFoldReport>();
        var aggregateTestRows = new List<ResolvedEvaluation>();
        var aggregateGridRows = new List<GridEvaluationSample>();
        var foldNumber = 0;

        for (var testStart = firstTestStart;
             testStart <= lastEvaluation;
             testStart = testStart.Add(configuration.Step))
        {
            var testEnd = testStart.Add(configuration.TestWindow);
            var testRows = rows
                .Where(row => row.EvaluationAsOfUtc >= testStart
                    && row.EvaluationAsOfUtc < testEnd)
                .ToArray();
            if (testRows.Length == 0)
            {
                continue;
            }

            foldNumber++;
            var validationStart = testStart.Subtract(configuration.ValidationWindow);
            var trainingStart = validationStart.Subtract(configuration.TrainingWindow);
            var trainingOutcomeCutoff = validationStart.Subtract(configuration.Embargo);
            var validationOutcomeCutoff = testStart.Subtract(configuration.Embargo);
            var possibleTraining = rows
                .Where(row => row.EvaluationAsOfUtc >= trainingStart
                    && row.EvaluationAsOfUtc < validationStart)
                .ToArray();
            var training = possibleTraining
                .Where(row => OutcomeAvailable(row, configuration) <= trainingOutcomeCutoff)
                .ToArray();
            var excludedUnavailable = possibleTraining.Length - training.Length;
            var possibleValidation = rows
                .Where(row => row.EvaluationAsOfUtc >= validationStart
                    && row.EvaluationAsOfUtc < testStart)
                .ToArray();
            var validation = possibleValidation
                .Where(row => OutcomeAvailable(row, configuration) <= validationOutcomeCutoff)
                .ToArray();
            var excludedUnavailableValidation = possibleValidation.Length - validation.Length;
            var temporalIntegrity = training.All(row =>
                    row.EvaluationAsOfUtc < validationStart
                    && OutcomeAvailable(row, configuration) <= trainingOutcomeCutoff)
                && validation.All(row =>
                    row.EvaluationAsOfUtc >= validationStart
                    && row.EvaluationAsOfUtc < testStart
                    && OutcomeAvailable(row, configuration) <= validationOutcomeCutoff)
                && testRows.All(row => row.EvaluationAsOfUtc >= testStart
                    && row.EvaluationAsOfUtc < testEnd);
            var eligible = temporalIntegrity
                && training.Length >= configuration.MinimumTrainingObservations
                && validation.Length >= configuration.MinimumValidationObservations;
            var reason = !temporalIntegrity
                ? "TEMPORAL_INTEGRITY_FAILED"
                : training.Length < configuration.MinimumTrainingObservations
                    ? $"MINIMUM_TRAINING_OBSERVATIONS_NOT_MET:{training.Length}/{configuration.MinimumTrainingObservations}"
                    : validation.Length < configuration.MinimumValidationObservations
                        ? $"MINIMUM_VALIDATION_OBSERVATIONS_NOT_MET:{validation.Length}/{configuration.MinimumValidationObservations}"
                        : null;
            ThresholdGridFoldReport? thresholdGrid = null;
            if (eligible)
            {
                aggregateTestRows.AddRange(testRows);
                if (configuration.ThresholdGrid.Enabled)
                {
                    var grid = SelectThresholdPolicy(
                        training,
                        validation,
                        testRows,
                        gridPolicies,
                        configuration.ThresholdGrid);
                    thresholdGrid = grid.Report;
                    if (grid.Policy is not null)
                    {
                        aggregateGridRows.AddRange(testRows.Select(row =>
                            new GridEvaluationSample(row, grid.Policy)));
                    }
                }
            }
            else if (configuration.ThresholdGrid.Enabled)
            {
                thresholdGrid = new ThresholdGridFoldReport(
                    gridPolicies.Count,
                    0,
                    [],
                    null,
                    reason,
                    null,
                    null);
            }

            folds.Add(new WalkForwardFoldReport(
                foldNumber,
                "DevelopmentWalkForward",
                trainingStart,
                trainingOutcomeCutoff,
                validationStart,
                validationOutcomeCutoff,
                testStart,
                testEnd,
                eligible,
                reason,
                training.Length,
                excludedUnavailable,
                validation.Length,
                excludedUnavailableValidation,
                training.Length == 0 ? null : training.Max(row => row.EvaluationAsOfUtc),
                training.Length == 0 ? null : training.Max(row => OutcomeAvailable(row, configuration)),
                validation.Length == 0 ? null : validation.Max(row => row.EvaluationAsOfUtc),
                validation.Length == 0 ? null : validation.Max(row => OutcomeAvailable(row, configuration)),
                temporalIntegrity,
                training.Select(row => row.EvaluationId).ToArray(),
                validation.Select(row => row.EvaluationId).ToArray(),
                testRows.Select(row => row.EvaluationId).ToArray(),
                eligible ? CalculateMetrics(testRows) : null,
                thresholdGrid));

            if (foldNumber > 100_000)
            {
                throw new InvalidOperationException("Walk-forward fold guard exceeded.");
            }
        }

        if (folds.Count > 0)
        {
            folds[^1] = folds[^1] with { EvaluationRole = "FinalHoldout" };
        }

        var uniqueAggregateRows = aggregateTestRows
            .GroupBy(row => row.EvaluationId, StringComparer.Ordinal)
            .Select(group => group.Single())
            .OrderBy(row => row.EvaluationAsOfUtc)
            .ThenBy(row => row.FixtureId)
            .ThenBy(row => row.EvaluationId, StringComparer.Ordinal)
            .ToArray();
        var aggregateMetrics = CalculateMetrics(uniqueAggregateRows);
        var aggregateGroups = CalculateGroups(
            uniqueAggregateRows,
            row => Outcome(row, robust: true),
            configuration.Grouping);
        var aggregateBootstrap = Bootstrap(
            uniqueAggregateRows,
            row => Outcome(row, robust: true),
            inputSha256,
            "fixed-robust-shadow",
            configuration.Bootstrap);

        ThresholdGridAggregateReport? thresholdGridAggregate = null;
        if (configuration.ThresholdGrid.Enabled)
        {
            var uniqueGrid = aggregateGridRows
                .GroupBy(sample => sample.Row.EvaluationId, StringComparer.Ordinal)
                .Select(group => group.Single())
                .OrderBy(sample => sample.Row.EvaluationAsOfUtc)
                .ThenBy(sample => sample.Row.FixtureId)
                .ThenBy(sample => sample.Row.EvaluationId, StringComparer.Ordinal)
                .ToArray();
            var policyByEvaluation = uniqueGrid.ToDictionary(
                sample => sample.Row.EvaluationId,
                sample => sample.Policy,
                StringComparer.Ordinal);
            var gridRows = uniqueGrid.Select(sample => sample.Row).ToArray();
            StrategyOutcome GridOutcome(ResolvedEvaluation row) =>
                Outcome(row, policyByEvaluation[row.EvaluationId]);
            thresholdGridAggregate = new ThresholdGridAggregateReport(
                folds.Count(fold => fold.ThresholdGrid?.SelectedPolicy is not null),
                CalculateMetrics(gridRows, GridOutcome),
                Bootstrap(
                    gridRows,
                    GridOutcome,
                    inputSha256,
                    "walk-forward-threshold-grid",
                    configuration.Bootstrap),
                CalculateGroups(gridRows, GridOutcome, configuration.Grouping));
        }

        var reportAsOf = rows.Max(row => OutcomeAvailable(row, configuration));
        return new RobustPickBacktestReport(
            "robust-pick-walk-forward-v3",
            inputSha256,
            configuration,
            input.Count,
            rows.Count,
            folds.Count,
            folds.Count(fold => fold.IsEligible),
            folds.Count == 0 ? null : folds[^1].Fold,
            folds.All(fold => fold.TemporalIntegrityPassed),
            reportAsOf,
            folds,
            aggregateMetrics,
            aggregateBootstrap,
            aggregateGroups,
            thresholdGridAggregate);
    }

    private static List<ResolvedEvaluation> PrepareRows(
        IReadOnlyCollection<ResolvedEvaluation> input,
        BacktestConfiguration configuration)
    {
        IEnumerable<ResolvedEvaluation> rows = input;
        if (configuration.FromUtc.HasValue)
        {
            var from = configuration.FromUtc.Value.ToUniversalTime();
            rows = rows.Where(row => row.EvaluationAsOfUtc >= from);
        }
        if (configuration.ToUtc.HasValue)
        {
            var to = configuration.ToUtc.Value.ToUniversalTime();
            rows = rows.Where(row => row.EvaluationAsOfUtc < to);
        }
        if (configuration.LatestEvaluationPerSelection)
        {
            rows = rows
                .GroupBy(row => row.SelectionKey, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(row => row.EvaluationAsOfUtc)
                    .ThenByDescending(row => row.EvaluationId, StringComparer.Ordinal)
                    .First());
        }

        return rows
            .OrderBy(row => row.EvaluationAsOfUtc)
            .ThenBy(row => row.FixtureId)
            .ThenBy(row => row.EvaluationId, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<GroupedMetricsReport> CalculateGroups(
        IReadOnlyCollection<ResolvedEvaluation> rows,
        Func<ResolvedEvaluation, StrategyOutcome> robustOutcomeFactory,
        GroupingConfiguration configuration)
    {
        var dimensions = new GroupDimension[]
        {
            new("Overall", _ => "ALL"),
            new("Bot", row => GroupText(row.BotKey, configuration)),
            new("MarketFamily", row => GroupText(row.MarketFamily, configuration)),
            new("ExactMarket", row => GroupText(row.MarketType, configuration)),
            new("Scope", row => GroupText(row.Scope, configuration)),
            new("Side", row => GroupText(row.Side, configuration)),
            new("League", row => GroupText(row.League, configuration)),
            new("OddsBand", row => NumericBand(row.Odds, configuration.OddsBandWidth)),
            new("LineBand", row => row.LineValue.HasValue
                ? NumericBand(row.LineValue.Value, configuration.LineBandWidth)
                : configuration.MissingValueLabel),
            new("CalibrationReliabilityBand", row => row.CalibrationReliability.HasValue
                ? UnitIntervalBand(
                    row.CalibrationReliability.Value,
                    configuration.CalibrationReliabilityBandWidth)
                : configuration.MissingValueLabel),
            new("RobustnessDecile", row => row.RobustnessScore.HasValue
                ? RobustnessDecile(row.RobustnessScore.Value)
                : configuration.MissingValueLabel)
        };
        var reports = new List<GroupedMetricsReport>();
        foreach (var dimension in dimensions)
        {
            reports.AddRange(rows
                .GroupBy(dimension.Selector, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new GroupedMetricsReport(
                    dimension.Name,
                    group.Key,
                    group.Count(),
                    CalculateMetrics(group.ToArray(), robustOutcomeFactory))));
        }
        return reports;
    }

    private static string GroupText(string? value, GroupingConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return configuration.MissingValueLabel;
        }
        return string.Join(' ', value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }

    private static string NumericBand(decimal value, decimal width)
    {
        var lower = decimal.Floor(value / width) * width;
        return FormattableString.Invariant(
            $"[{FormatBandValue(lower)},{FormatBandValue(lower + width)})");
    }

    private static string UnitIntervalBand(decimal value, decimal width)
    {
        var bucketCount = (int)decimal.Ceiling(1m / width);
        var bucket = value == 1m
            ? bucketCount - 1
            : Math.Min(bucketCount - 1, (int)decimal.Floor(value / width));
        var lower = bucket * width;
        var upper = Math.Min(1m, lower + width);
        var closing = upper == 1m ? "]" : ")";
        return FormattableString.Invariant(
            $"[{FormatBandValue(lower)},{FormatBandValue(upper)}{closing}");
    }

    private static string RobustnessDecile(decimal score)
    {
        var decile = score == 1m
            ? 10
            : Math.Clamp((int)decimal.Floor(score * 10m) + 1, 1, 10);
        return FormattableString.Invariant($"D{decile:00}");
    }

    private static string FormatBandValue(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static MetricsComparison CalculateMetrics(IReadOnlyCollection<ResolvedEvaluation> rows) =>
        CalculateMetrics(rows, row => Outcome(row, robust: true));

    private static MetricsComparison CalculateMetrics(
        IReadOnlyCollection<ResolvedEvaluation> rows,
        Func<ResolvedEvaluation, StrategyOutcome> robustOutcomeFactory)
    {
        var ordered = rows
            .OrderBy(row => row.EvaluationAsOfUtc)
            .ThenBy(row => row.FixtureId)
            .ThenBy(row => row.EvaluationId, StringComparer.Ordinal)
            .ToArray();
        var baseline = ordered.Select(row => Outcome(row, robust: false)).ToArray();
        var robust = ordered.Select(robustOutcomeFactory).ToArray();
        return CalculateMetrics(baseline, robust, ordered.Length);
    }

    private static MetricsComparison CalculateMetrics(
        IReadOnlyList<StrategyOutcome> baseline,
        IReadOnlyList<StrategyOutcome> robust,
        int candidateCount)
    {
        var baselineMetrics = Strategy(baseline, candidateCount);
        var robustMetrics = Strategy(robust, candidateCount);
        var disagreements = 0;
        var baselineOnly = 0;
        var robustOnly = 0;
        var bothApproved = 0;
        var bothRejected = 0;
        var reductions = 0;
        var robustRejections = 0;
        var avoidedLosses = 0;
        var avoidedLossUnits = 0m;
        var avoidedWins = 0;
        var avoidedWinProfit = 0m;
        var totalStakeReduction = 0m;
        var baselineApprovedCount = 0;
        var baselineApprovedStake = 0m;

        for (var index = 0; index < baseline.Count; index++)
        {
            var baselineApproved = baseline[index].Approved;
            var robustApproved = robust[index].Approved;
            if (baselineApproved != robustApproved)
            {
                disagreements++;
                if (baselineApproved) baselineOnly++;
                else robustOnly++;
            }
            else if (baselineApproved)
            {
                bothApproved++;
            }
            else
            {
                bothRejected++;
            }

            if (!baselineApproved)
            {
                continue;
            }

            baselineApprovedCount++;
            baselineApprovedStake += baseline[index].Stake;
            totalStakeReduction += Math.Max(0m, baseline[index].Stake - robust[index].Stake);
            if (!robustApproved)
            {
                robustRejections++;
                if (baseline[index].Evaluation.SettlementFactor < 0m)
                {
                    avoidedLosses++;
                    avoidedLossUnits += -baseline[index].ProfitLoss;
                }
                else if (baseline[index].Evaluation.SettlementFactor > 0m)
                {
                    avoidedWins++;
                    avoidedWinProfit += baseline[index].ProfitLoss;
                }
            }
            else if (robust[index].Stake < baseline[index].Stake)
            {
                reductions++;
            }
        }

        return new MetricsComparison(
            baselineMetrics,
            robustMetrics,
            new StrategyComparison(
                disagreements,
                baselineOnly,
                robustOnly,
                bothApproved,
                bothRejected,
                reductions,
                baselineApprovedCount > 0 ? (decimal)reductions / baselineApprovedCount : 0m,
                totalStakeReduction,
                baselineApprovedStake > 0m ? totalStakeReduction / baselineApprovedStake : null,
                robustRejections,
                baselineApprovedCount > 0 ? (decimal)robustRejections / baselineApprovedCount : 0m,
                avoidedLosses,
                avoidedLossUnits,
                avoidedWins,
                avoidedWinProfit,
                robustMetrics.ProfitLoss - baselineMetrics.ProfitLoss,
                robustMetrics.Yield.HasValue && baselineMetrics.Yield.HasValue
                    ? robustMetrics.Yield - baselineMetrics.Yield
                    : null,
                robustMetrics.MaximumDrawdown - baselineMetrics.MaximumDrawdown));
    }

    private static StrategyOutcome Outcome(ResolvedEvaluation row, bool robust)
    {
        var approved = robust ? IsRobustApproved(row.RobustDecision) : row.BaselineApproved;
        var requestedStake = robust
            ? row.RobustRecommendedStake ?? 0m
            : row.BaselineStake;
        return Outcome(
            row,
            approved,
            requestedStake,
            robust ? row.RobustProbability : row.BaselineProbability,
            robust ? row.RobustPredictiveCdf : row.BaselinePredictiveCdf);
    }

    private static StrategyOutcome Outcome(ResolvedEvaluation row, RobustThresholdPolicy policy)
    {
        var approved = PassesThresholdPolicy(row, policy);
        return Outcome(
            row,
            approved,
            row.ThresholdGridStake.GetValueOrDefault(),
            row.RobustProbability,
            row.RobustPredictiveCdf);
    }

    private static StrategyOutcome Outcome(
        ResolvedEvaluation row,
        bool approved,
        decimal requestedStake,
        decimal? probability,
        AuditablePredictiveCdf? predictiveCdf)
    {
        var stake = approved ? requestedStake : 0m;
        var profitLoss = stake <= 0m ? 0m : ProfitLoss(row, stake);
        return new StrategyOutcome(
            row,
            approved && stake > 0m,
            stake,
            profitLoss,
            probability,
            ResolveBinaryOutcome(row),
            predictiveCdf is not null && row.ObservedMarketValue.HasValue
                ? ContinuousRankedProbabilityScore(
                    predictiveCdf.Points,
                    row.ObservedMarketValue.Value)
                : null,
            ResolveClvOdds(row),
            ResolveClvProbability(row),
            row.ClvLine);
    }

    private static StrategyMetrics Strategy(
        IReadOnlyCollection<StrategyOutcome> outcomes,
        int candidateCount)
    {
        var approved = outcomes.Where(outcome => outcome.Approved).ToArray();
        var totalStake = approved.Sum(outcome => outcome.Stake);
        var profitLoss = approved.Sum(outcome => outcome.ProfitLoss);
        var probabilityRows = approved
            .Where(outcome => outcome.Probability.HasValue && outcome.BinaryOutcome.HasValue)
            .ToArray();
        decimal? brier = probabilityRows.Length == 0
            ? null
            : probabilityRows.Average(outcome =>
            {
                var difference = outcome.Probability!.Value - outcome.BinaryOutcome!.Value;
                return difference * difference;
            });
        decimal? logLoss = probabilityRows.Length == 0
            ? null
            : probabilityRows.Average(outcome => BinaryLogLoss(
                outcome.Probability!.Value,
                outcome.BinaryOutcome!.Value));
        decimal? predictedMean = probabilityRows.Length == 0
            ? null
            : probabilityRows.Average(outcome => outcome.Probability!.Value);
        decimal? observedMean = probabilityRows.Length == 0
            ? null
            : probabilityRows.Average(outcome => outcome.BinaryOutcome!.Value);
        var nonPush = approved.Count(outcome => outcome.Evaluation.SettlementFactor != 0m);
        var positive = approved.Count(outcome => outcome.Evaluation.SettlementFactor > 0m);
        var clvOdds = approved.Where(outcome => outcome.ClvOdds.HasValue).ToArray();
        var clvProbability = approved.Where(outcome => outcome.ClvProbability.HasValue).ToArray();
        var clvLine = approved.Where(outcome => outcome.ClvLine.HasValue).ToArray();
        var pointEdges = approved.Where(outcome => outcome.Evaluation.PointEdge.HasValue).ToArray();
        var robustEdges = approved.Where(outcome => outcome.Evaluation.RobustEdge.HasValue).ToArray();
        var pointExpectedValues = approved
            .Where(outcome => outcome.Evaluation.PointExpectedValue.HasValue)
            .ToArray();
        var robustExpectedValues = approved
            .Where(outcome => outcome.Evaluation.RobustExpectedValue.HasValue)
            .ToArray();
        var positiveEvStability = approved
            .Where(outcome => outcome.Evaluation.PositiveEvStability.HasValue)
            .ToArray();
        var crps = approved.Where(outcome => outcome.Crps.HasValue).ToArray();
        var exposure = ExposureConcentration(approved);

        return new StrategyMetrics(
            candidateCount,
            approved.Length,
            approved.Length,
            totalStake,
            profitLoss,
            totalStake > 0m ? profitLoss / totalStake : null,
            MaximumDrawdown(approved),
            approved.Count(outcome => outcome.Evaluation.SettlementFactor == 1m),
            approved.Count(outcome => outcome.Evaluation.SettlementFactor == 0.5m),
            approved.Count(outcome => outcome.Evaluation.SettlementFactor == 0m),
            approved.Count(outcome => outcome.Evaluation.SettlementFactor == -0.5m),
            approved.Count(outcome => outcome.Evaluation.SettlementFactor == -1m),
            approved.Length > 0 ? approved.Average(outcome => outcome.Evaluation.Odds) : null,
            nonPush > 0 ? (decimal)positive / nonPush : null,
            LongestLosingStreak(approved),
            pointEdges.Length,
            pointEdges.Length > 0
                ? pointEdges.Average(outcome => outcome.Evaluation.PointEdge!.Value)
                : null,
            robustEdges.Length,
            robustEdges.Length > 0
                ? robustEdges.Average(outcome => outcome.Evaluation.RobustEdge!.Value)
                : null,
            pointExpectedValues.Length,
            pointExpectedValues.Length > 0
                ? pointExpectedValues.Average(
                    outcome => outcome.Evaluation.PointExpectedValue!.Value)
                : null,
            robustExpectedValues.Length,
            robustExpectedValues.Length > 0
                ? robustExpectedValues.Average(
                    outcome => outcome.Evaluation.RobustExpectedValue!.Value)
                : null,
            positiveEvStability.Length,
            positiveEvStability.Length > 0
                ? positiveEvStability.Average(
                    outcome => outcome.Evaluation.PositiveEvStability!.Value)
                : null,
            exposure.GroupCount,
            exposure.Hhi,
            exposure.MaximumShare,
            probabilityRows.Length,
            brier,
            logLoss,
            ExpectedCalibrationError(probabilityRows),
            predictedMean,
            observedMean,
            predictedMean.HasValue && observedMean.HasValue ? predictedMean - observedMean : null,
            crps.Length,
            crps.Length > 0 ? crps.Average(outcome => outcome.Crps!.Value) : null,
            clvOdds.Length,
            clvOdds.Length > 0 ? clvOdds.Average(outcome => outcome.ClvOdds!.Value) : null,
            clvProbability.Length,
            clvProbability.Length > 0
                ? clvProbability.Average(outcome => outcome.ClvProbability!.Value)
                : null,
            clvLine.Length,
            clvLine.Length > 0 ? clvLine.Average(outcome => outcome.ClvLine!.Value) : null);
    }

    private static decimal MaximumDrawdown(IEnumerable<StrategyOutcome> outcomes)
    {
        var running = 0m;
        var peak = 0m;
        var maximum = 0m;
        var settlements = outcomes
            .GroupBy(outcome => EffectiveSettlementTime(outcome.Evaluation))
            .OrderBy(group => group.Key)
            .Select(group => group.Sum(outcome => outcome.ProfitLoss));
        foreach (var profitLoss in settlements)
        {
            running += profitLoss;
            peak = Math.Max(peak, running);
            maximum = Math.Max(maximum, peak - running);
        }
        return maximum;
    }

    private static int LongestLosingStreak(IEnumerable<StrategyOutcome> outcomes)
    {
        var current = 0;
        var longest = 0;
        foreach (var outcome in outcomes
            .OrderBy(item => EffectiveSettlementTime(item.Evaluation))
            .ThenBy(item => item.Evaluation.FixtureId)
            .ThenBy(item => item.Evaluation.EvaluationId, StringComparer.Ordinal))
        {
            if (outcome.Evaluation.SettlementFactor < 0m)
            {
                current++;
                longest = Math.Max(longest, current);
            }
            else if (outcome.Evaluation.SettlementFactor > 0m)
            {
                current = 0;
            }
        }
        return longest;
    }

    private static DateTimeOffset EffectiveSettlementTime(ResolvedEvaluation row) =>
        (row.OutcomeAvailableUtc ?? row.FixtureEndUtc).ToUniversalTime();

    private static decimal BinaryLogLoss(decimal probability, decimal outcome)
    {
        var bounded = Math.Clamp(probability, LogLossEpsilon, 1m - LogLossEpsilon);
        return (decimal)(-(double)outcome * Math.Log((double)bounded)
            - (double)(1m - outcome) * Math.Log((double)(1m - bounded)));
    }

    private static decimal? ExpectedCalibrationError(IReadOnlyCollection<StrategyOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return null;
        }
        var total = (decimal)outcomes.Count;
        return outcomes
            .GroupBy(outcome => Math.Min(
                EceBinCount - 1,
                (int)decimal.Floor(outcome.Probability!.Value * EceBinCount)))
            .Sum(bin =>
            {
                var predicted = bin.Average(outcome => outcome.Probability!.Value);
                var observed = bin.Average(outcome => outcome.BinaryOutcome!.Value);
                return bin.Count() / total * Math.Abs(predicted - observed);
            });
    }

    private static ExposureMetrics ExposureConcentration(
        IReadOnlyCollection<StrategyOutcome> outcomes)
    {
        var totalStake = outcomes.Sum(outcome => outcome.Stake);
        if (totalStake <= 0m)
        {
            return new ExposureMetrics(0, null, null);
        }
        var groups = outcomes
            .GroupBy(outcome => string.IsNullOrWhiteSpace(outcome.Evaluation.ExposureGroupKey)
                    ? FormattableString.Invariant($"FIXTURE:{outcome.Evaluation.FixtureId}")
                    : outcome.Evaluation.ExposureGroupKey.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Sum(outcome => outcome.Stake) / totalStake)
            .ToArray();
        return new ExposureMetrics(
            groups.Length,
            groups.Sum(share => share * share),
            groups.Max());
    }

    private static decimal ContinuousRankedProbabilityScore(
        IReadOnlyList<PredictiveCdfPoint> points,
        decimal observedValue)
    {
        var boundaries = points
            .Select(point => point.Value)
            .Append(observedValue)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var score = 0m;
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var left = boundaries[index];
            var width = boundaries[index + 1] - left;
            var cdf = points
                .TakeWhile(point => point.Value <= left)
                .Select(point => point.CumulativeProbability)
                .LastOrDefault();
            var observedCdf = left >= observedValue ? 1m : 0m;
            var difference = cdf - observedCdf;
            score += difference * difference * width;
        }
        return score;
    }

    private static ThresholdGridSelection SelectThresholdPolicy(
        IReadOnlyCollection<ResolvedEvaluation> training,
        IReadOnlyCollection<ResolvedEvaluation> validation,
        IReadOnlyCollection<ResolvedEvaluation> test,
        IReadOnlyCollection<RobustThresholdPolicy> policies,
        ThresholdGridConfiguration configuration)
    {
        var rawCandidates = policies.Select(policy => new ThresholdCandidateMetrics(
            policy,
            CalculateMetrics(training, row => Outcome(row, policy)).RobustShadow,
            CalculateMetrics(validation, row => Outcome(row, policy)).RobustShadow))
            .ToArray();
        var eligibleRaw = rawCandidates
            .Where(candidate =>
                candidate.Training.ApprovedPicks >= configuration.MinimumApprovedTrainingPicks
                && candidate.Validation.ApprovedPicks
                    >= configuration.MinimumApprovedValidationPicks)
            .ToArray();
        var sampleEligiblePolicies = eligibleRaw
            .Select(candidate => candidate.Policy)
            .ToHashSet();
        var reports = rawCandidates
            .Select(candidate => CandidateReport(
                candidate,
                sampleEligiblePolicies.Contains(candidate.Policy)
                    ? Objective(candidate, eligibleRaw, configuration.ObjectiveWeights)
                    : null))
            .OrderBy(candidate => candidate.Policy.StableKey, StringComparer.Ordinal)
            .ToArray();
        var eligible = reports
            .Where(candidate => candidate.Objective is not null
                && candidate.Objective.UnavailableComponents.Count == 0)
            .OrderByDescending(candidate => candidate.Objective!.WeightedScore)
            .ThenByDescending(candidate => candidate.ValidationProfitLoss)
            .ThenByDescending(candidate => candidate.ValidationYield ?? decimal.MinValue)
            .ThenBy(candidate => candidate.ValidationMaximumDrawdown)
            .ThenByDescending(candidate => candidate.ApprovedValidationPicks)
            .ThenBy(candidate => candidate.ValidationEce ?? decimal.MaxValue)
            .ThenByDescending(candidate => candidate.ValidationAverageClv ?? decimal.MinValue)
            .ThenBy(candidate => candidate.Policy.StableKey, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length == 0)
        {
            return new ThresholdGridSelection(
                null,
                new ThresholdGridFoldReport(
                    reports.Length,
                    0,
                    reports,
                    null,
                    "NO_GRID_POLICY_MET_SAMPLE_AND_OBJECTIVE_COVERAGE_REQUIREMENTS",
                    null,
                    null));
        }

        var selected = eligible[0];
        return new ThresholdGridSelection(
            selected.Policy,
            new ThresholdGridFoldReport(
                reports.Length,
                eligible.Length,
                reports,
                selected.Policy,
                "SELECTED_ON_VALIDATION_ONLY:MULTI_CRITERIA_WEIGHTED_MIN_MAX;TEST_UNTOUCHED",
                selected,
                CalculateMetrics(test, row => Outcome(row, selected.Policy))));
    }

    private static ThresholdGridCandidateReport CandidateReport(
        ThresholdCandidateMetrics candidate,
        ThresholdObjectiveBreakdown? objective) => new(
        candidate.Policy,
        candidate.Training.ApprovedPicks,
        candidate.Training.ProfitLoss,
        candidate.Training.Yield,
        candidate.Training.MaximumDrawdown,
        candidate.Validation.ApprovedPicks,
        candidate.Validation.ProfitLoss,
        candidate.Validation.Yield,
        candidate.Validation.MaximumDrawdown,
        candidate.Validation.ExpectedCalibrationError,
        candidate.Validation.AverageClvOdds,
        objective);

    private static ThresholdObjectiveBreakdown Objective(
        ThresholdCandidateMetrics candidate,
        IReadOnlyCollection<ThresholdCandidateMetrics> candidates,
        ThresholdObjectiveWeights weights)
    {
        var profitLoss = NormalizeObjective(
            candidate.Validation.ProfitLoss,
            candidates.Select(item => (decimal?)item.Validation.ProfitLoss),
            higherIsBetter: true);
        var yield = NormalizeObjective(
            candidate.Validation.Yield,
            candidates.Select(item => item.Validation.Yield),
            higherIsBetter: true);
        var drawdown = NormalizeObjective(
            candidate.Validation.MaximumDrawdown,
            candidates.Select(item => (decimal?)item.Validation.MaximumDrawdown),
            higherIsBetter: false);
        var volume = NormalizeObjective(
            candidate.Validation.ApprovedPicks,
            candidates.Select(item => (decimal?)item.Validation.ApprovedPicks),
            higherIsBetter: true);
        var calibrationQuality = candidate.Validation.ExpectedCalibrationError.HasValue
            ? (decimal?)(1m - candidate.Validation.ExpectedCalibrationError.Value)
            : null;
        var calibration = NormalizeObjective(
            calibrationQuality,
            candidates.Select(item => item.Validation.ExpectedCalibrationError.HasValue
                ? 1m - item.Validation.ExpectedCalibrationError.Value
                : (decimal?)null),
            higherIsBetter: true);
        var clv = NormalizeObjective(
            candidate.Validation.AverageClvOdds,
            candidates.Select(item => item.Validation.AverageClvOdds),
            higherIsBetter: true);
        var unavailable = new List<string>();
        AddUnavailable(unavailable, "YIELD", candidate.Validation.Yield, weights.Yield);
        AddUnavailable(
            unavailable,
            "CALIBRATION_ECE",
            candidate.Validation.ExpectedCalibrationError,
            weights.Calibration);
        AddUnavailable(
            unavailable,
            "CLV_ODDS",
            candidate.Validation.AverageClvOdds,
            weights.Clv);
        var totalWeight = weights.ProfitLoss
            + weights.Yield
            + weights.Drawdown
            + weights.Volume
            + weights.Calibration
            + weights.Clv;
        var weightedScore = (weights.ProfitLoss * profitLoss
            + weights.Yield * yield
            + weights.Drawdown * drawdown
            + weights.Volume * volume
            + weights.Calibration * calibration
            + weights.Clv * clv) / totalWeight;
        return new ThresholdObjectiveBreakdown(
            profitLoss,
            yield,
            drawdown,
            volume,
            calibration,
            clv,
            weightedScore,
            unavailable);
    }

    private static decimal NormalizeObjective(
        decimal? value,
        IEnumerable<decimal?> population,
        bool higherIsBetter)
    {
        if (!value.HasValue)
        {
            return 0m;
        }
        var available = population.Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();
        if (available.Length == 0)
        {
            return 0m;
        }
        var minimum = available.Min();
        var maximum = available.Max();
        if (minimum == maximum)
        {
            return 0.5m;
        }
        var normalized = (value.Value - minimum) / (maximum - minimum);
        return higherIsBetter ? normalized : 1m - normalized;
    }

    private static void AddUnavailable(
        ICollection<string> unavailable,
        string name,
        decimal? value,
        decimal weight)
    {
        if (weight > 0m && !value.HasValue)
        {
            unavailable.Add(name);
        }
    }

    private static IReadOnlyList<RobustThresholdPolicy> BuildGridPolicies(
        ThresholdGridConfiguration configuration)
    {
        var policies = new List<RobustThresholdPolicy>();
        foreach (var edge in configuration.MinRobustEdge)
        foreach (var ev in configuration.MinRobustExpectedValue)
        foreach (var evStability in configuration.MinPositiveEvStability)
        foreach (var scenarioStability in configuration.MinScenarioSideStability)
        foreach (var distance in configuration.MinNormalizedWorstCaseDistance)
        foreach (var range in configuration.MaxNormalizedConsensusRange)
        foreach (var gap in configuration.MaxNormalizedCoherenceGap)
        foreach (var calibration in configuration.MinCalibrationReliability)
        {
            policies.Add(new RobustThresholdPolicy(
                edge,
                ev,
                evStability,
                scenarioStability,
                distance,
                range,
                gap,
                calibration));
            if (policies.Count > configuration.MaximumGridCombinations)
            {
                throw new InvalidOperationException(
                    $"Threshold grid exceeds MaximumGridCombinations={configuration.MaximumGridCombinations}.");
            }
        }

        return policies
            .Distinct()
            .OrderBy(policy => policy.StableKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool PassesThresholdPolicy(
        ResolvedEvaluation row,
        RobustThresholdPolicy policy) =>
        row.ThresholdGridEligible
        && row.ThresholdGridStake is > 0m
        && row.RobustEdge >= policy.MinRobustEdge
        && row.RobustExpectedValue > policy.MinRobustExpectedValue
        && row.PositiveEvStability >= policy.MinPositiveEvStability
        && row.ScenarioSideStability >= policy.MinScenarioSideStability
        && row.NormalizedWorstCaseDistance >= policy.MinNormalizedWorstCaseDistance
        && row.NormalizedConsensusRange <= policy.MaxNormalizedConsensusRange
        && row.NormalizedCoherenceGap <= policy.MaxNormalizedCoherenceGap
        && row.CalibrationReliability >= policy.MinCalibrationReliability;

    private static BootstrapConfidenceIntervals? Bootstrap(
        IReadOnlyCollection<ResolvedEvaluation> rows,
        Func<ResolvedEvaluation, StrategyOutcome> robustOutcomeFactory,
        string inputSha256,
        string scope,
        ClusterBootstrapConfiguration configuration)
    {
        if (configuration.Replicates <= 0 || rows.Count == 0)
        {
            return null;
        }

        var plan = CreateBootstrapPlan(rows, configuration.ClusterBy);
        var seed = DeriveBootstrapSeed(inputSha256, scope, configuration);
        var random = new StableRandom(seed);
        var baselineProfit = new decimal[configuration.Replicates];
        var robustProfit = new decimal[configuration.Replicates];
        var profitDelta = new decimal[configuration.Replicates];
        var baselineYield = new List<decimal>(configuration.Replicates);
        var robustYield = new List<decimal>(configuration.Replicates);
        var yieldDelta = new List<decimal>(configuration.Replicates);
        var baselineDrawdown = new decimal[configuration.Replicates];
        var robustDrawdown = new decimal[configuration.Replicates];

        for (var replicate = 0; replicate < configuration.Replicates; replicate++)
        {
            var sample = plan.Sample(random, rows.Count);
            var metrics = CalculateMetrics(sample, robustOutcomeFactory);
            baselineProfit[replicate] = metrics.Baseline.ProfitLoss;
            robustProfit[replicate] = metrics.RobustShadow.ProfitLoss;
            profitDelta[replicate] = metrics.Difference.ProfitLossDelta;
            baselineDrawdown[replicate] = metrics.Baseline.MaximumDrawdown;
            robustDrawdown[replicate] = metrics.RobustShadow.MaximumDrawdown;
            if (metrics.Baseline.Yield.HasValue)
            {
                baselineYield.Add(metrics.Baseline.Yield.Value);
            }
            if (metrics.RobustShadow.Yield.HasValue)
            {
                robustYield.Add(metrics.RobustShadow.Yield.Value);
            }
            if (metrics.Difference.YieldDelta.HasValue)
            {
                yieldDelta.Add(metrics.Difference.YieldDelta.Value);
            }
        }

        return new BootstrapConfidenceIntervals(
            plan.Mode == "Fixture-Day"
                ? "HierarchicalClusteredPercentileBootstrap"
                : "ClusteredPercentileBootstrap",
            plan.Mode,
            plan.PrimaryClusterCount,
            plan.DayClusterCount,
            plan.FixtureClusterCount,
            configuration.Replicates,
            configuration.ConfidenceLevel,
            seed,
            Interval(baselineProfit, configuration.ConfidenceLevel)!,
            Interval(robustProfit, configuration.ConfidenceLevel)!,
            Interval(profitDelta, configuration.ConfidenceLevel)!,
            Interval(baselineYield, configuration.ConfidenceLevel),
            Interval(robustYield, configuration.ConfidenceLevel),
            Interval(yieldDelta, configuration.ConfidenceLevel),
            Interval(baselineDrawdown, configuration.ConfidenceLevel)!,
            Interval(robustDrawdown, configuration.ConfidenceLevel)!);
    }

    private static MetricConfidenceInterval? Interval(
        IEnumerable<decimal> values,
        decimal confidenceLevel)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }
        var tail = (1m - confidenceLevel) / 2m;
        return new MetricConfidenceInterval(
            Quantile(sorted, tail),
            Quantile(sorted, 0.50m),
            Quantile(sorted, 1m - tail));
    }

    private static decimal Quantile(IReadOnlyList<decimal> sorted, decimal probability)
    {
        var position = probability * (sorted.Count - 1);
        var lower = (int)decimal.Floor(position);
        var upper = (int)decimal.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }
        var fraction = position - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    private static BootstrapPlan CreateBootstrapPlan(
        IReadOnlyCollection<ResolvedEvaluation> rows,
        string clusterBy)
    {
        var mode = NormalizeClusterMode(clusterBy);
        var orderedRows = rows
            .OrderBy(row => row.EvaluationAsOfUtc)
            .ThenBy(row => row.FixtureId)
            .ThenBy(row => row.EvaluationId, StringComparer.Ordinal)
            .ToArray();
        var days = orderedRows
            .GroupBy(DayKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(day => new DayBootstrapCluster(
                day.GroupBy(row => row.FixtureId)
                    .OrderBy(fixture => fixture.Key)
                    .Select(fixture => fixture.ToArray())
                    .ToArray()))
            .ToArray();
        var fixtureClusters = orderedRows
            .GroupBy(row => row.FixtureId)
            .OrderBy(group => group.Key)
            .Select(group => group.ToArray())
            .ToArray();
        var flatClusters = mode switch
        {
            "Fixture" => fixtureClusters,
            "Day" => orderedRows
                .GroupBy(DayKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.ToArray())
                .ToArray(),
            "Fixture-Day" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(clusterBy), clusterBy, null)
        };
        return new BootstrapPlan(mode, flatClusters, days, days.Length, fixtureClusters.Length);
    }

    private static string DayKey(ResolvedEvaluation row) =>
        $"D:{row.FixtureStartUtc.UtcDateTime:yyyy-MM-dd}";

    private static string NormalizeClusterMode(string clusterBy)
    {
        var normalized = clusterBy.Trim()
            .Replace('_', '-')
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized switch
        {
            "FIXTURE" => "Fixture",
            "DAY" => "Day",
            "FIXTURE-DAY" or "FIXTUREDAY" => "Fixture-Day",
            _ => throw new ArgumentOutOfRangeException(nameof(clusterBy), clusterBy, null)
        };
    }

    private static ulong DeriveBootstrapSeed(
        string inputSha256,
        string scope,
        ClusterBootstrapConfiguration configuration)
    {
        var canonical = FormattableString.Invariant(
            $"{inputSha256}|{scope}|{NormalizeClusterMode(configuration.ClusterBy)}|{configuration.Replicates}|{configuration.ConfidenceLevel:G29}|{configuration.SeedVersion}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }

    private static decimal ProfitLoss(ResolvedEvaluation row, decimal stake)
    {
        if (row.UnitProfitLoss.HasValue)
        {
            return stake * row.UnitProfitLoss.Value;
        }
        return row.SettlementFactor > 0m
            ? stake * row.SettlementFactor * (row.Odds - 1m)
            : stake * row.SettlementFactor;
    }

    private static decimal? ResolveBinaryOutcome(ResolvedEvaluation row)
    {
        if (row.BinaryOutcome.HasValue)
        {
            return row.BinaryOutcome;
        }
        return row.SettlementFactor switch
        {
            1m => 1m,
            -1m => 0m,
            _ => null
        };
    }

    private static decimal? ResolveClvOdds(ResolvedEvaluation row) =>
        row.ClvOdds
        ?? (row.ClosingOdds is > 1m ? row.Odds / row.ClosingOdds.Value - 1m : null);

    private static decimal? ResolveClvProbability(ResolvedEvaluation row) =>
        row.ClvProbability
        ?? (row.ClosingNoVigProbability.HasValue
            ? row.ClosingNoVigProbability.Value - (row.MarketProbability ?? 1m / row.Odds)
            : null);

    private static DateTimeOffset OutcomeAvailable(
        ResolvedEvaluation row,
        BacktestConfiguration configuration) =>
        (row.OutcomeAvailableUtc ?? row.FixtureEndUtc.Add(configuration.OutcomeAvailabilityLag))
        .ToUniversalTime();

    private static bool IsRobustApproved(string decision) =>
        IsApprove(decision) || NormalizeDecision(decision) == "REDUCESTAKE";

    private static bool IsApprove(string decision) => NormalizeDecision(decision) == "APPROVE";

    private static string NormalizeDecision(string decision) => new(
        decision.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static void ValidateRows(
        IReadOnlyCollection<ResolvedEvaluation> rows,
        BacktestConfiguration configuration)
    {
        var duplicateIds = rows
            .GroupBy(row => row.EvaluationId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIds is not null)
        {
            throw new InvalidDataException($"Duplicate EvaluationId '{duplicateIds.Key}'.");
        }

        foreach (var row in rows)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(row.EvaluationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(row.SelectionKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(row.RobustDecision);
            if (row.EvaluationAsOfUtc >= row.FixtureStartUtc)
            {
                throw new InvalidDataException(
                    $"Evaluation '{row.EvaluationId}' is not strictly pre-match.");
            }
            if (row.FixtureEndUtc < row.FixtureStartUtc)
            {
                throw new InvalidDataException($"Fixture dates are invalid for '{row.EvaluationId}'.");
            }
            if (row.OutcomeAvailableUtc.HasValue
                && row.OutcomeAvailableUtc.Value < row.FixtureEndUtc)
            {
                throw new InvalidDataException(
                    $"Outcome availability predates fixture end for '{row.EvaluationId}'.");
            }
            if (row.Odds <= 1m
                || row.BaselineStake < 0m
                || row.RobustRecommendedStake < 0m
                || row.RobustRecommendedStake > row.BaselineStake
                || row.ThresholdGridStake < 0m
                || row.ThresholdGridStake > row.BaselineStake
                || row.SettlementFactor is not (-1m or -0.5m or 0m or 0.5m or 1m)
                || row.BaselineProbability is < 0m or > 1m
                || row.RobustProbability is < 0m or > 1m
                || row.MarketProbability is < 0m or > 1m
                || row.PositiveEvStability is < 0m or > 1m
                || row.ScenarioSideStability is < 0m or > 1m
                || row.CalibrationReliability is < 0m or > 1m
                || row.RobustnessScore is < 0m or > 1m
                || row.NormalizedWorstCaseDistance < 0m
                || row.NormalizedConsensusRange < 0m
                || row.NormalizedCoherenceGap < 0m
                || row.ClosingNoVigProbability is < 0m or > 1m
                || row.BinaryOutcome.HasValue && row.BinaryOutcome is not (0m or 1m)
                || row.ClosingOdds.HasValue && row.ClosingOdds <= 1m)
            {
                throw new InvalidDataException($"Numeric values are invalid for '{row.EvaluationId}'.");
            }
            if (row.BaselineApproved && row.BaselineStake <= 0m)
            {
                throw new InvalidDataException(
                    $"Approved baseline evaluation '{row.EvaluationId}' has no stake.");
            }
            var decision = NormalizeDecision(row.RobustDecision);
            if (decision is not ("APPROVE" or "REJECT" or "REDUCESTAKE" or "MANUALREVIEW"))
            {
                throw new InvalidDataException(
                    $"Unknown RobustDecision '{row.RobustDecision}' for '{row.EvaluationId}'.");
            }
            if (decision is "APPROVE" or "REDUCESTAKE"
                && !row.RobustRecommendedStake.HasValue)
            {
                throw new InvalidDataException(
                    $"Approved robust evaluation '{row.EvaluationId}' requires RobustRecommendedStake.");
            }
            if (configuration.ThresholdGrid.Enabled
                && row.ThresholdGridEligible
                && (!row.ThresholdGridStake.HasValue
                    || !row.RobustEdge.HasValue
                    || !row.RobustExpectedValue.HasValue
                    || !row.PositiveEvStability.HasValue
                    || !row.ScenarioSideStability.HasValue
                    || !row.NormalizedWorstCaseDistance.HasValue
                    || !row.NormalizedConsensusRange.HasValue
                    || !row.NormalizedCoherenceGap.HasValue
                    || !row.CalibrationReliability.HasValue))
            {
                throw new InvalidDataException(
                    $"Threshold-grid evidence is incomplete for '{row.EvaluationId}'.");
            }
            if ((row.BaselinePredictiveCdf is not null || row.RobustPredictiveCdf is not null)
                && !row.ObservedMarketValue.HasValue)
            {
                throw new InvalidDataException(
                    $"Predictive CDF for '{row.EvaluationId}' requires ObservedMarketValue.");
            }
            ValidatePredictiveCdf(row, row.BaselinePredictiveCdf, "baseline");
            ValidatePredictiveCdf(row, row.RobustPredictiveCdf, "robust");
        }
    }

    private static void ValidatePredictiveCdf(
        ResolvedEvaluation row,
        AuditablePredictiveCdf? distribution,
        string strategy)
    {
        if (distribution is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(distribution.DistributionId)
            || !string.Equals(
                distribution.Method,
                "DiscreteStepCdfV1",
                StringComparison.OrdinalIgnoreCase)
            || distribution.AsOfUtc > row.EvaluationAsOfUtc
            || distribution.AsOfUtc >= row.FixtureStartUtc
            || distribution.Points is null
            || distribution.Points.Count == 0
            || distribution.EvidenceIds is null
            || distribution.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || distribution.EvidenceIds.Distinct(StringComparer.Ordinal).Count()
                != distribution.EvidenceIds.Count)
        {
            throw new InvalidDataException(
                $"Auditable {strategy} predictive CDF is invalid for '{row.EvaluationId}'.");
        }
        decimal? previousValue = null;
        var previousProbability = 0m;
        foreach (var point in distribution.Points)
        {
            if (previousValue.HasValue && point.Value <= previousValue.Value
                || point.CumulativeProbability is < 0m or > 1m
                || point.CumulativeProbability < previousProbability)
            {
                throw new InvalidDataException(
                    $"{strategy} predictive CDF points are invalid for '{row.EvaluationId}'.");
            }
            previousValue = point.Value;
            previousProbability = point.CumulativeProbability;
        }
        if (previousProbability != 1m)
        {
            throw new InvalidDataException(
                $"{strategy} predictive CDF must finish at 1 for '{row.EvaluationId}'.");
        }
    }

    private static void ValidateConfiguration(BacktestConfiguration configuration)
    {
        if (configuration.TrainingWindowDays <= 0m
            || configuration.ValidationWindowDays <= 0m
            || configuration.TestWindowDays <= 0m
            || configuration.StepDays < configuration.TestWindowDays
            || configuration.EmbargoHours < 0m
            || configuration.OutcomeAvailabilityLagHours < 0m
            || configuration.MinimumTrainingObservations < 0
            || configuration.MinimumValidationObservations < 0
            || configuration.FromUtc.HasValue && configuration.ToUtc.HasValue
                && configuration.FromUtc >= configuration.ToUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "Windows must be positive, step must not overlap tests, and lag/embargo cannot be negative.");
        }
        if (string.IsNullOrWhiteSpace(configuration.Bootstrap.ClusterBy))
        {
            throw new ArgumentOutOfRangeException(nameof(configuration.Bootstrap));
        }
        _ = NormalizeClusterMode(configuration.Bootstrap.ClusterBy);
        if (configuration.Bootstrap.Replicates < 0
            || configuration.Bootstrap.Replicates > 100_000
            || configuration.Bootstrap.ConfidenceLevel is <= 0m or >= 1m
            || string.IsNullOrWhiteSpace(configuration.Bootstrap.SeedVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(configuration.Bootstrap));
        }
        if (configuration.Grouping.OddsBandWidth <= 0m
            || configuration.Grouping.LineBandWidth <= 0m
            || configuration.Grouping.CalibrationReliabilityBandWidth is <= 0m or > 1m
            || string.IsNullOrWhiteSpace(configuration.Grouping.MissingValueLabel))
        {
            throw new ArgumentOutOfRangeException(nameof(configuration.Grouping));
        }
        if (configuration.ThresholdGrid.Enabled)
        {
            var grid = configuration.ThresholdGrid;
            var lists = new IReadOnlyCollection<decimal>[]
            {
                grid.MinRobustEdge,
                grid.MinRobustExpectedValue,
                grid.MinPositiveEvStability,
                grid.MinScenarioSideStability,
                grid.MinNormalizedWorstCaseDistance,
                grid.MaxNormalizedConsensusRange,
                grid.MaxNormalizedCoherenceGap,
                grid.MinCalibrationReliability
            };
            var weights = grid.ObjectiveWeights;
            var weightValues = new[]
            {
                weights.ProfitLoss,
                weights.Yield,
                weights.Drawdown,
                weights.Volume,
                weights.Calibration,
                weights.Clv
            };
            if (lists.Any(list => list is null || list.Count == 0)
                || grid.MinPositiveEvStability.Any(value => value is < 0m or > 1m)
                || grid.MinScenarioSideStability.Any(value => value is < 0m or > 1m)
                || grid.MinCalibrationReliability.Any(value => value is < 0m or > 1m)
                || grid.MinimumApprovedTrainingPicks <= 0
                || grid.MinimumApprovedValidationPicks <= 0
                || weightValues.Any(value => value < 0m)
                || weightValues.All(value => value == 0m)
                || grid.MaximumGridCombinations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(configuration.ThresholdGrid));
            }
        }
    }

    private static RobustPickBacktestReport EmptyReport(
        int inputCount,
        BacktestConfiguration configuration,
        string inputSha256) => new(
            "robust-pick-walk-forward-v3",
            inputSha256,
            configuration,
            inputCount,
            0,
            0,
            0,
            null,
            true,
            null,
            [],
            CalculateMetrics([]),
            null,
            [],
            null);

    private sealed record GridEvaluationSample(
        ResolvedEvaluation Row,
        RobustThresholdPolicy Policy);

    private sealed record GroupDimension(
        string Name,
        Func<ResolvedEvaluation, string> Selector);

    private sealed record ExposureMetrics(
        int GroupCount,
        decimal? Hhi,
        decimal? MaximumShare);

    private sealed record ThresholdCandidateMetrics(
        RobustThresholdPolicy Policy,
        StrategyMetrics Training,
        StrategyMetrics Validation);

    private sealed record ThresholdGridSelection(
        RobustThresholdPolicy? Policy,
        ThresholdGridFoldReport Report);

    private sealed record DayBootstrapCluster(ResolvedEvaluation[][] Fixtures);

    private sealed class BootstrapPlan(
        string mode,
        ResolvedEvaluation[][] flatClusters,
        DayBootstrapCluster[] days,
        int dayClusterCount,
        int fixtureClusterCount)
    {
        public string Mode { get; } = mode;
        public int DayClusterCount { get; } = dayClusterCount;
        public int FixtureClusterCount { get; } = fixtureClusterCount;
        public int PrimaryClusterCount => Mode == "Fixture"
            ? FixtureClusterCount
            : DayClusterCount;

        public List<ResolvedEvaluation> Sample(StableRandom random, int capacity)
        {
            var sample = new List<ResolvedEvaluation>(capacity);
            if (Mode != "Fixture-Day")
            {
                for (var block = 0; block < flatClusters.Length; block++)
                {
                    sample.AddRange(flatClusters[random.NextInt(flatClusters.Length)]);
                }
                return sample;
            }

            for (var dayIndex = 0; dayIndex < days.Length; dayIndex++)
            {
                var day = days[random.NextInt(days.Length)];
                for (var fixtureIndex = 0;
                     fixtureIndex < day.Fixtures.Length;
                     fixtureIndex++)
                {
                    sample.AddRange(day.Fixtures[random.NextInt(day.Fixtures.Length)]);
                }
            }
            return sample;
        }
    }

    private sealed class StableRandom
    {
        private ulong _state;

        public StableRandom(ulong seed) => _state = seed;

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            }
            return (int)(NextUInt64() % (uint)exclusiveMaximum);
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
}
