using System.Text;
using System.Text.Json;

namespace RobustPickBacktest;

public static class BacktestSelfTest
{
    public static void Run()
    {
        var configuration = new BacktestConfiguration
        {
            TrainingWindowDays = 20m,
            ValidationWindowDays = 3m,
            TestWindowDays = 5m,
            StepDays = 5m,
            EmbargoHours = 2m,
            OutcomeAvailabilityLagHours = 8m,
            MinimumTrainingObservations = 3,
            MinimumValidationObservations = 2,
            FirstTestStartUtc = At(2026, 1, 10),
            Bootstrap = new ClusterBootstrapConfiguration
            {
                Replicates = 200,
                ConfidenceLevel = 0.95m,
                ClusterBy = "Fixture-Day"
            },
            ThresholdGrid = new ThresholdGridConfiguration
            {
                Enabled = true,
                MinRobustEdge = [0.01m, 0.05m],
                MinRobustExpectedValue = [0m],
                MinPositiveEvStability = [0.75m],
                MinScenarioSideStability = [0.75m],
                MinNormalizedWorstCaseDistance = [0.25m],
                MaxNormalizedConsensusRange = [0.75m],
                MaxNormalizedCoherenceGap = [0.75m],
                MinCalibrationReliability = [0.50m],
                MinimumApprovedTrainingPicks = 2,
                MinimumApprovedValidationPicks = 2,
                ObjectiveWeights = new ThresholdObjectiveWeights
                {
                    ProfitLoss = 1m,
                    Yield = 1m,
                    Drawdown = 1m,
                    Volume = 1m,
                    Calibration = 1m,
                    Clv = 1m
                }
            }
        };
        var rows = new List<ResolvedEvaluation>
        {
            Training("train-1", At(2026, 1, 1)),
            Training("train-2", At(2026, 1, 2)),
            Training("train-3", At(2026, 1, 3)),
            Training("training-future-poison", At(2026, 1, 4)).WithOutcome(
                fixtureStart: At(2026, 1, 6, 20),
                fixtureEnd: At(2026, 1, 7),
                outcomeAvailable: At(2026, 1, 7, 8)),
            Training("validation-1", At(2026, 1, 7)),
            Training("validation-2", At(2026, 1, 8)),
            Training("validation-future-poison", At(2026, 1, 9)).WithOutcome(
                fixtureStart: At(2026, 1, 9, 20),
                fixtureEnd: At(2026, 1, 10),
                outcomeAvailable: At(2026, 1, 10, 8)),
            Test(
                "test-loss",
                At(2026, 1, 10, 1),
                settlementFactor: -1m,
                robustDecision: "Reject",
                robustStake: 0m,
                robustEdge: 0m,
                baselineProbability: 0.70m,
                robustProbability: 0.40m,
                clvOdds: 0.02m),
            Test(
                "test-win",
                At(2026, 1, 11, 1),
                settlementFactor: 1m,
                robustDecision: "ReduceStake",
                robustStake: 0.5m,
                robustEdge: 0.02m,
                baselineProbability: 0.60m,
                robustProbability: 0.70m,
                clvOdds: 0.04m)
        };

        var engine = new WalkForwardBacktestEngine();
        var report = engine.Run(rows, configuration, "self-test-sha256");
        Equal(report.FoldCount, 1, "fold count");
        Equal(report.EligibleFoldCount, 1, "eligible folds");
        Assert(report.TemporalIntegrityPassed, "temporal integrity");
        var fold = report.Folds.Single();
        Assert(fold.EvaluationRole == "FinalHoldout", "final holdout role");
        Equal(report.FinalHoldoutFold ?? 0, 1, "final holdout fold");
        Equal(fold.TrainingObservationCount, 3, "training count");
        Equal(fold.ExcludedUnavailableTrainingCount, 1, "unavailable training count");
        Equal(fold.ValidationObservationCount, 2, "validation count");
        Equal(fold.ExcludedUnavailableValidationCount, 1, "unavailable validation count");
        Assert(!fold.TrainingEvaluationIds.Contains("training-future-poison"),
            "future outcome leaked into training");
        Assert(!fold.ValidationEvaluationIds.Contains("validation-future-poison"),
            "future outcome leaked into validation");
        Assert(fold.MaximumTrainingOutcomeAvailableUtc <= fold.TrainingOutcomeCutoffUtc,
            "training availability crossed embargo cutoff");
        Assert(fold.MaximumValidationOutcomeAvailableUtc <= fold.ValidationOutcomeCutoffUtc,
            "validation availability crossed embargo cutoff");

        var metrics = report.Aggregate;
        Close(metrics.Baseline.TotalStake, 2m, "baseline stake");
        Close(metrics.Baseline.ProfitLoss, 0m, "baseline P/L");
        Close(metrics.Baseline.Yield, 0m, "baseline yield");
        Close(metrics.Baseline.MaximumDrawdown, 1m, "baseline drawdown");
        Close(metrics.Baseline.HitRate, 0.5m, "baseline hit rate");
        Close(metrics.Baseline.AverageOdds, 2m, "baseline average odds");
        Equal(metrics.Baseline.LongestLosingStreak, 1, "baseline losing streak");
        Equal(metrics.Baseline.ResolvedPicks, 2, "baseline resolved picks");
        Close(metrics.RobustShadow.TotalStake, 0.5m, "robust stake");
        Close(metrics.RobustShadow.ProfitLoss, 0.5m, "robust P/L");
        Close(metrics.RobustShadow.Yield, 1m, "robust yield");
        Close(metrics.RobustShadow.MaximumDrawdown, 0m, "robust drawdown");
        Close(metrics.RobustShadow.HitRate, 1m, "robust hit rate");
        Equal(metrics.RobustShadow.LongestLosingStreak, 0, "robust losing streak");
        Equal(metrics.RobustShadow.ResolvedPicks, 1, "robust resolved picks");
        Equal(metrics.Difference.ApprovalDisagreements, 1, "approval disagreements");
        Equal(metrics.Difference.StakeReductions, 1, "stake reductions");
        Close(metrics.Difference.StakeReductionRate, 0.5m, "stake reduction rate");
        Close(metrics.Difference.TotalStakeReduction, 1.5m, "total stake reduction");
        Close(metrics.Difference.StakeReductionPercentage, 0.75m, "stake reduction percentage");
        Equal(metrics.Difference.RobustRejectionsOfBaselineBets, 1, "robust rejections");
        Close(metrics.Difference.RobustRejectionRate, 0.5m, "robust rejection rate");
        Equal(metrics.Difference.AvoidedLosses, 1, "avoided losses");
        Close(metrics.Difference.AvoidedLossUnits, 1m, "avoided loss units");
        Equal(metrics.Difference.AvoidedWins, 0, "avoided wins");
        Close(metrics.Baseline.BrierScore, 0.325m, "baseline Brier");
        Close(metrics.RobustShadow.BrierScore, 0.09m, "robust Brier");
        Close(metrics.Baseline.LogLoss, 0.857399m, "baseline log loss");
        Close(metrics.RobustShadow.LogLoss, 0.356675m, "robust log loss");
        Close(metrics.Baseline.ExpectedCalibrationError, 0.55m, "baseline ECE");
        Close(metrics.RobustShadow.ExpectedCalibrationError, 0.30m, "robust ECE");
        Close(metrics.Baseline.AverageClvOdds, 0.03m, "baseline CLV");
        Close(metrics.RobustShadow.AverageClvOdds, 0.04m, "robust CLV");
        Close(metrics.Baseline.AveragePointEdge, 0.10m, "baseline point edge");
        Close(metrics.Baseline.AverageRobustEdge, 0.01m, "baseline robust edge");
        Close(metrics.RobustShadow.AverageRobustEdge, 0.02m, "robust selected edge");
        Close(metrics.Baseline.AveragePointExpectedValue, 0.20m, "baseline point EV");
        Close(metrics.Baseline.AverageRobustExpectedValue, 0.05m, "baseline robust EV");
        Close(metrics.Baseline.AveragePositiveEvStability, 0.90m,
            "positive-EV stability");
        Close(metrics.Baseline.ExposureConcentrationHhi, 0.50m, "baseline HHI");
        Close(metrics.RobustShadow.ExposureConcentrationHhi, 1m, "robust HHI");
        Equal(metrics.Baseline.CrpsObservationCount, 2, "baseline CRPS count");
        Equal(metrics.RobustShadow.CrpsObservationCount, 1, "robust CRPS count");
        Close(metrics.Baseline.AverageCrps, 0.325m, "baseline CRPS");
        Close(metrics.RobustShadow.AverageCrps, 0.09m, "robust CRPS");
        Assert(report.BootstrapConfidenceIntervals is not null, "missing clustered bootstrap");
        Equal(report.BootstrapConfidenceIntervals!.Replicates, 200, "bootstrap replicates");
        Assert(report.BootstrapConfidenceIntervals.ClusterBy == "Fixture-Day",
            "hierarchical bootstrap mode");
        Assert(report.BootstrapConfidenceIntervals.Method
                == "HierarchicalClusteredPercentileBootstrap",
            "hierarchical bootstrap method");
        Equal(report.BootstrapConfidenceIntervals.ClusterCount, 2, "bootstrap primary clusters");
        Equal(report.BootstrapConfidenceIntervals.DayClusterCount, 2, "bootstrap day clusters");
        Equal(report.BootstrapConfidenceIntervals.FixtureClusterCount, 2,
            "bootstrap fixture clusters");

        var grid = fold.ThresholdGrid;
        Assert(grid?.SelectedPolicy is not null, "threshold grid did not select a policy");
        Close(grid!.SelectedPolicy!.MinRobustEdge, 0.01m, "validation-selected edge threshold");
        Assert(grid.SelectionReason!.StartsWith(
                "SELECTED_ON_VALIDATION_ONLY",
                StringComparison.Ordinal),
            "grid selection is not marked validation-only");
        Assert(grid.SelectedPerformance?.Objective is not null,
            "missing multi-criterion objective breakdown");
        Assert(grid.SelectedPerformance!.Objective!.UnavailableComponents.Count == 0,
            "available objective components marked missing");
        Close(grid.SelectedPerformance.Objective.WeightedScore, 0.5m,
            "single eligible policy normalized objective");
        Equal(grid.Candidates.Count, 2, "reported grid candidates");
        Equal(grid.EligiblePolicyCount, 1, "eligible validation policies");
        Assert(report.ThresholdGridAggregate is not null, "missing threshold-grid aggregate");
        Close(report.ThresholdGridAggregate!.Metrics.RobustShadow.ProfitLoss,
            0.5m,
            "grid OOS profit");
        Assert(report.ThresholdGridAggregate.BootstrapConfidenceIntervals is not null,
            "missing grid bootstrap");
        var changedTestRows = rows
            .Where(row => row.EvaluationId is not ("test-loss" or "test-win"))
            .Concat([
                Test(
                    "test-loss",
                    At(2026, 1, 10, 1),
                    settlementFactor: 1m,
                    robustDecision: "Reject",
                    robustStake: 0m,
                    robustEdge: 0m,
                    baselineProbability: 0.10m,
                    robustProbability: 0.90m,
                    clvOdds: -0.20m),
                Test(
                    "test-win",
                    At(2026, 1, 11, 1),
                    settlementFactor: -1m,
                    robustDecision: "ReduceStake",
                    robustStake: 0.5m,
                    robustEdge: 0.02m,
                    baselineProbability: 0.90m,
                    robustProbability: 0.10m,
                    clvOdds: -0.30m)
            ])
            .ToArray();
        var changedTestReport = engine.Run(
            changedTestRows,
            configuration,
            "changed-final-test-sha256");
        Assert(changedTestReport.Folds.Single().ThresholdGrid!.SelectedPolicy!.StableKey
            == grid.SelectedPolicy.StableKey,
            "final test changed the selected threshold");
        Close(changedTestReport.Folds.Single().ThresholdGrid!.SelectedPerformance!
                .Objective!.WeightedScore,
            grid.SelectedPerformance.Objective.WeightedScore,
            "final test changed validation objective");
        Close(changedTestReport.ThresholdGridAggregate!.Metrics.RobustShadow.ProfitLoss,
            -0.5m,
            "changed final-test OOS profit");

        var requiredDimensions = new[]
        {
            "Overall", "Bot", "MarketFamily", "ExactMarket", "Scope", "Side", "League",
            "OddsBand", "LineBand", "CalibrationReliabilityBand", "RobustnessDecile"
        };
        foreach (var dimension in requiredDimensions)
        {
            var dimensionGroups = report.Groups
                .Where(group => group.Dimension == dimension)
                .ToArray();
            Assert(dimensionGroups.Length > 0, $"missing group dimension {dimension}");
            Equal(dimensionGroups.Sum(group => group.ObservationCount), 2,
                $"group partition {dimension}");
        }
        Assert(report.Groups.Any(group => group.Dimension == "Scope" && group.Key == "TOTAL"),
            "scope group");
        Assert(report.Groups.Any(group =>
                group.Dimension == "RobustnessDecile" && group.Key == "D09"),
            "robustness decile group");
        Assert(report.Groups.Any(group =>
                group.Dimension == "CalibrationReliabilityBand"
                && group.Key == "[0.8,0.9)"),
            "calibration reliability band");

        var json1 = JsonSerializer.Serialize(report, Cli.ReportJsonOptions);
        var json2 = JsonSerializer.Serialize(engine.Run(rows, configuration, "self-test-sha256"),
            Cli.ReportJsonOptions);
        Assert(json1 == json2, "report is not deterministic");
        var repeated = engine.Run(rows, configuration, "self-test-sha256");
        Assert(report.BootstrapConfidenceIntervals.DeterministicSeed
            == repeated.BootstrapConfidenceIntervals!.DeterministicSeed,
            "bootstrap seed is not deterministic");

        var arrayBytes = JsonSerializer.SerializeToUtf8Bytes(rows);
        Equal(BacktestInputLoader.Parse(arrayBytes).Count, rows.Count, "JSON array parser");
        var jsonLines = string.Join('\n', rows.Select(row => JsonSerializer.Serialize(row)));
        Equal(BacktestInputLoader.Parse(Encoding.UTF8.GetBytes(jsonLines)).Count,
            rows.Count,
            "JSONL parser");
        AssertThrows<InvalidDataException>(
            () => engine.Run([InvalidCdfRow()], new BacktestConfiguration(), "invalid-cdf"),
            "invalid predictive CDF must fail closed");
        var parsedCommand = Cli.Parse([
            "--input", "evaluations.jsonl",
            "--bootstrap-replicates", "17",
            "--bootstrap-confidence", "0.90",
            "--bootstrap-cluster", "fixture-day",
            "--validation-days", "12",
            "--min-validation", "4",
            "--odds-band-width", "0.20",
            "--grid", "true",
            "--grid-min-edge", "0.01,0.02",
            "--grid-min-picks", "2",
            "--grid-min-validation-picks", "3",
            "--grid-weight-calibration", "1.25"
        ]);
        Equal(parsedCommand.Configuration.Bootstrap.Replicates, 17, "CLI bootstrap replicates");
        Close(parsedCommand.Configuration.Bootstrap.ConfidenceLevel, 0.90m,
            "CLI bootstrap confidence");
        Assert(parsedCommand.Configuration.Bootstrap.ClusterBy == "fixture-day",
            "CLI cluster mode");
        Close(parsedCommand.Configuration.ValidationWindowDays, 12m,
            "CLI validation window");
        Equal(parsedCommand.Configuration.MinimumValidationObservations, 4,
            "CLI validation observations");
        Close(parsedCommand.Configuration.Grouping.OddsBandWidth, 0.20m,
            "CLI odds band");
        Assert(parsedCommand.Configuration.ThresholdGrid.Enabled, "CLI grid enabled");
        Equal(parsedCommand.Configuration.ThresholdGrid.MinRobustEdge.Count, 2,
            "CLI edge threshold count");
        Equal(parsedCommand.Configuration.ThresholdGrid.MinimumApprovedTrainingPicks, 2,
            "CLI grid minimum picks");
        Equal(parsedCommand.Configuration.ThresholdGrid.MinimumApprovedValidationPicks, 3,
            "CLI grid validation picks");
        Close(parsedCommand.Configuration.ThresholdGrid.ObjectiveWeights.Calibration, 1.25m,
            "CLI calibration objective weight");

        Console.WriteLine("PASS strict train-validation-final-test excludes unavailable outcomes");
        Console.WriteLine("PASS baseline and robust stake/P&L/yield/drawdown metrics");
        Console.WriteLine("PASS value, exposure, Brier, CRPS and CLV metrics");
        Console.WriteLine("PASS hit rate, odds, log loss, ECE, streak and rejection/reduction KPIs");
        Console.WriteLine("PASS all mandatory grouped reports and missing-safe bands");
        Console.WriteLine("PASS deterministic hierarchical fixture-day bootstrap intervals");
        Console.WriteLine("PASS threshold grid is selected on validation and frozen for final test");
        Console.WriteLine("PASS deterministic JSON report, JSON/JSONL parsing and CLI options");
        Console.WriteLine("RobustPickBacktest self-test passed.");
    }

    private static ResolvedEvaluation Training(string id, DateTimeOffset evaluation) =>
        Row(id, evaluation, 1m, "Approve", 1m, 0.02m, 0.55m, 0.55m, 0m);

    private static ResolvedEvaluation Test(
        string id,
        DateTimeOffset evaluation,
        decimal settlementFactor,
        string robustDecision,
        decimal robustStake,
        decimal robustEdge,
        decimal baselineProbability,
        decimal robustProbability,
        decimal clvOdds) => Row(
            id,
            evaluation,
            settlementFactor,
            robustDecision,
            robustStake,
            robustEdge,
            baselineProbability,
            robustProbability,
            clvOdds);

    private static ResolvedEvaluation Row(
        string id,
        DateTimeOffset evaluation,
        decimal settlementFactor,
        string robustDecision,
        decimal robustStake,
        decimal robustEdge,
        decimal baselineProbability,
        decimal robustProbability,
        decimal clvOdds)
    {
        var fixtureStart = evaluation.AddHours(4);
        var fixtureEnd = fixtureStart.AddHours(2);
        return new ResolvedEvaluation
        {
            EvaluationId = id,
            SelectionKey = id,
            FixtureId = StableFixtureId(id),
            EvaluationAsOfUtc = evaluation,
            FixtureStartUtc = fixtureStart,
            FixtureEndUtc = fixtureEnd,
            OutcomeAvailableUtc = fixtureEnd.AddHours(8),
            BotKey = "SELF",
            MarketFamily = "Shots",
            MarketType = "TotalShots",
            Scope = "Total",
            Side = "Over",
            League = "Test League",
            LineValue = 10.5m,
            RobustnessScore = 0.85m,
            BaselineApproved = true,
            BaselineStake = 1m,
            RobustDecision = robustDecision,
            RobustRecommendedStake = robustStake,
            Odds = 2m,
            SettlementFactor = settlementFactor,
            BaselineProbability = baselineProbability,
            RobustProbability = robustProbability,
            MarketProbability = 0.5m,
            BinaryOutcome = settlementFactor == 1m ? 1m : 0m,
            ClvOdds = clvOdds,
            ClvProbability = clvOdds,
            ClvLine = 0.1m,
            ThresholdGridEligible = true,
            ThresholdGridStake = 0.5m,
            PointEdge = 0.10m,
            RobustEdge = robustEdge,
            PointExpectedValue = 0.20m,
            RobustExpectedValue = 0.05m,
            PositiveEvStability = 0.90m,
            ScenarioSideStability = 0.90m,
            NormalizedWorstCaseDistance = 0.50m,
            NormalizedConsensusRange = 0.40m,
            NormalizedCoherenceGap = 0.40m,
            CalibrationReliability = 0.80m,
            ObservedMarketValue = settlementFactor == 1m ? 1m : 0m,
            BaselinePredictiveCdf = BernoulliCdf(
                $"{id}-baseline",
                evaluation,
                baselineProbability),
            RobustPredictiveCdf = BernoulliCdf(
                $"{id}-robust",
                evaluation,
                robustProbability)
        };
    }

    private static AuditablePredictiveCdf BernoulliCdf(
        string id,
        DateTimeOffset asOfUtc,
        decimal successProbability) => new()
    {
        DistributionId = id,
        Method = "DiscreteStepCdfV1",
        AsOfUtc = asOfUtc,
        SourceVersion = "self-test-v1",
        EvidenceIds = [$"evidence:{id}"],
        Points =
        [
            new PredictiveCdfPoint(0m, 1m - successProbability),
            new PredictiveCdfPoint(1m, 1m)
        ]
    };

    private static ResolvedEvaluation WithOutcome(
        this ResolvedEvaluation source,
        DateTimeOffset fixtureStart,
        DateTimeOffset fixtureEnd,
        DateTimeOffset outcomeAvailable) => new()
    {
        EvaluationId = source.EvaluationId,
        SelectionKey = source.SelectionKey,
        FixtureId = source.FixtureId,
        EvaluationAsOfUtc = source.EvaluationAsOfUtc,
        FixtureStartUtc = fixtureStart,
        FixtureEndUtc = fixtureEnd,
        OutcomeAvailableUtc = outcomeAvailable,
        BotKey = source.BotKey,
        MarketFamily = source.MarketFamily,
        MarketType = source.MarketType,
        Scope = source.Scope,
        Side = source.Side,
        League = source.League,
        LineValue = source.LineValue,
        RobustnessScore = source.RobustnessScore,
        ExposureGroupKey = source.ExposureGroupKey,
        BaselineApproved = source.BaselineApproved,
        BaselineStake = source.BaselineStake,
        RobustDecision = source.RobustDecision,
        RobustRecommendedStake = source.RobustRecommendedStake,
        Odds = source.Odds,
        SettlementFactor = source.SettlementFactor,
        UnitProfitLoss = source.UnitProfitLoss,
        BaselineProbability = source.BaselineProbability,
        RobustProbability = source.RobustProbability,
        MarketProbability = source.MarketProbability,
        BinaryOutcome = source.BinaryOutcome,
        ClosingOdds = source.ClosingOdds,
        ClosingNoVigProbability = source.ClosingNoVigProbability,
        ClvOdds = source.ClvOdds,
        ClvProbability = source.ClvProbability,
        ClvLine = source.ClvLine,
        ThresholdGridEligible = source.ThresholdGridEligible,
        ThresholdGridStake = source.ThresholdGridStake,
        PointEdge = source.PointEdge,
        RobustEdge = 0.90m,
        PointExpectedValue = source.PointExpectedValue,
        RobustExpectedValue = source.RobustExpectedValue,
        PositiveEvStability = source.PositiveEvStability,
        ScenarioSideStability = source.ScenarioSideStability,
        NormalizedWorstCaseDistance = source.NormalizedWorstCaseDistance,
        NormalizedConsensusRange = source.NormalizedConsensusRange,
        NormalizedCoherenceGap = source.NormalizedCoherenceGap,
        CalibrationReliability = source.CalibrationReliability,
        ObservedMarketValue = source.ObservedMarketValue,
        BaselinePredictiveCdf = source.BaselinePredictiveCdf,
        RobustPredictiveCdf = source.RobustPredictiveCdf
    };

    private static long StableFixtureId(string value)
    {
        unchecked
        {
            long hash = 1469598103934665603L;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 1099511628211L;
            }
            return hash;
        }
    }

    private static ResolvedEvaluation InvalidCdfRow() => new()
    {
        EvaluationId = "invalid-cdf",
        SelectionKey = "invalid-cdf",
        FixtureId = 999,
        EvaluationAsOfUtc = At(2026, 1, 1),
        FixtureStartUtc = At(2026, 1, 2),
        FixtureEndUtc = At(2026, 1, 2, 2),
        OutcomeAvailableUtc = At(2026, 1, 2, 10),
        BaselineApproved = false,
        BaselineStake = 0m,
        RobustDecision = "Reject",
        Odds = 2m,
        SettlementFactor = 1m,
        ObservedMarketValue = 1m,
        BaselinePredictiveCdf = new AuditablePredictiveCdf
        {
            DistributionId = "invalid",
            Method = "DiscreteStepCdfV1",
            AsOfUtc = At(2026, 1, 1),
            EvidenceIds = ["invalid-evidence"],
            Points =
            [
                new PredictiveCdfPoint(0m, 0.25m),
                new PredictiveCdfPoint(1m, 0.90m)
            ]
        }
    };

    private static DateTimeOffset At(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static void Equal(int actual, int expected, string name)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
        }
    }

    private static void Close(decimal? actual, decimal expected, string name)
    {
        if (!actual.HasValue || Math.Abs(actual.Value - expected) > 0.000001m)
        {
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test failed: {name}.");
        }
    }

    private static void AssertThrows<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Self-test failed: {name}.");
    }
}
