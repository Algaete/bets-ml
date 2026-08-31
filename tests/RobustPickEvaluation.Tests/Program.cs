using CornersPrediction.Domain.RobustPickEvaluation;

var tests = new (string Name, Action Execute)[]
{
    ("Golden shots consensus is exact", GoldenConsensus),
    ("Over worst case uses the minimum and detects conflict", OverWorstCaseAndConflict),
    ("Missing context does not break consensus", MissingContext),
    ("Normalization never divides by zero", ZeroErrorScale),
    ("Future consensus component is excluded", FutureConsensusComponent),
    ("Probability agreement is unavailable with one source", ProbabilityAgreementNeedsEvidence),
    ("Reconciliation falls back to direct without validation", ReconciliationFallsBackToDirect),
    ("Reconciliation uses inverse out-of-sample error", ReconciliationUsesInverseError),
    ("Unvalidated context receives no weight", UnvalidatedContextGetsNoWeight),
    ("Small reconciliation sample shrinks toward direct", SmallSampleShrinksToDirect),
    ("Residual bootstrap rejects future outcomes", ResidualsRejectFutureOutcomes),
    ("Residual bootstrap rejects models trained after fixture", ResidualsRejectLateModel),
    ("Outcome availability lag is enforced at the boundary", OutcomeLagBoundary),
    ("Explicit outcome availability timestamp is honored without double lag", ExplicitOutcomeAvailability),
    ("Residual hierarchy falls back without crossing family", ResidualFallbackHierarchy),
    ("Effective N uses weighted Kish formula", EffectiveNFormula),
    ("Residual simulation is deterministic and order independent", BootstrapIsDeterministic),
    ("Settlement probabilities sum to one and results are nonnegative", DistributionIsValid),
    ("Selected-picks-only residuals are explicitly warned", SelectionBiasIsWarned),
    ("Configured MAE is the final error scale fallback", ConfiguredMaeFallback),
    ("Asian EV includes half-win and half-loss factors", AsianEvUsesSettlementFactors),
    ("Asian fair odds handles pushes and quarter outcomes", AsianFairOdds),
    ("Asian value rejects invalid probability mass", AsianValueRejectsInvalidMass),
    ("Calibration reliability decreases with small effective N", CalibrationSmallSample),
    ("Calibration fallback interval widens with smaller effective N", CalibrationIntervalWidth),
    ("Global calibration has less specificity than exact market", CalibrationSpecificity),
    ("Missing calibration metadata is never favorable", CalibrationMissingMetadata),
    ("Robust outer scenarios use conservative weighted quantiles", RobustOuterScenarios),
    ("No robust scenario remains unavailable", RobustOuterMissingEvidence),
    ("Risk adjusted stake never increases", StakeNeverIncreases),
    ("Risk adjusted stake follows configured bands", StakeBands),
    ("Missing robustness components reduce the score", MissingStakeEvidence),
    ("Policy accumulates every rejection reason", PolicyAccumulatesReasons),
    ("Shadow mode leaves current decision and stake unchanged", ShadowDoesNotEnforce),
    ("Enforce mode rejects and zeroes stake", EnforceRejects),
    ("Insufficient intelligence is distinct from reviewed neutral", MissingEvidenceIsNotNeutral),
    ("Missing coherence is never treated as positive evidence", MissingCoherenceIsWarned),
    ("Two-way no-vig uses proportional and power conservatively", ConservativeNoVig),
    ("One-sided market probability remains explicitly unavailable", SingleSidedMarketProbability),
    ("Single-sided price is an explicit no-vig warning", NoVigWarning),
    ("Stale odds reject the robust decision", StaleOddsReject),
    ("Fixture exposure reduces stake to remaining capacity", FixtureExposure),
    ("Team exposure crosses home and away positions", TeamExposure),
    ("Correlation cluster has an independent limit", ClusterExposure),
    ("Portfolio keeps the most robust related pick", HighestRobustnessWins),
    ("Missing scenario evidence never becomes reviewed neutral", MissingScenarioEvidenceIsNotNeutral),
    ("Not-applicable and source-unavailable scenario states are preserved", ScenarioUnavailableStatesArePreserved),
    ("Expired and future scenario snapshots are unusable", ScenarioExpiryAndLookahead),
    ("Base scenario requires real model evidence", BaseScenarioRequiresEvidence),
    ("Validated scenario adjustments are propagated exactly", ValidatedScenarioAdjustment),
    ("Unvalidated adjustment is zeroed and rejected", UnvalidatedScenarioAdjustment),
    ("Reviewed-neutral scenario requires evidence and zero adjustment", ReviewedNeutralScenario),
    ("Game-state scenario stays disabled without event history", GameStateRequiresEvents),
    ("Scenario adjustment sign mismatch cannot become favorable", ScenarioSignMismatch),
    ("Reason codes are stable external contracts", StableReasonCodes)
};

foreach (var test in tests)
{
    test.Execute();
    Console.WriteLine($"PASS {test.Name}");
}

Console.WriteLine($"{tests.Length} robust pick evaluation tests passed.");

static void GoldenConsensus()
{
    var result = Consensus(new PredictionConsensusRequest
    {
        Line = 24.5m,
        Side = SelectionSide.Under,
        DirectPrediction = 22.13m,
        HomePrediction = 11.96m,
        AwayPrediction = 11.53m,
        ContextPrediction = 23.71m,
        ErrorScale = 4.5m
    });

    Close(result.ComponentsPrediction, 23.49m);
    Close(result.DirectDistance, 2.37m);
    Close(result.ComponentsDistance, 1.01m);
    Close(result.ContextDistance, 0.79m);
    Close(result.ConsensusRange, 1.58m);
    Close(result.CoherenceGap, 1.36m);
    Close(result.WorstCasePrediction, 23.71m);
    Close(result.WorstCaseDistance, 0.79m);
    Assert(result.SideAgreement);
}

static void OverWorstCaseAndConflict()
{
    var result = Consensus(new PredictionConsensusRequest
    {
        Line = 10.5m,
        Side = SelectionSide.Over,
        DirectPrediction = 12m,
        HomePrediction = 6m,
        AwayPrediction = 5m,
        ContextPrediction = 9m,
        ErrorScale = 2m
    });
    Close(result.WorstCasePrediction, 9m);
    Close(result.WorstCaseDistance, -1.5m);
    Assert(!result.SideAgreement);
}

static void MissingContext()
{
    var result = Consensus(new PredictionConsensusRequest
    {
        Line = 9.5m,
        Side = SelectionSide.Under,
        DirectPrediction = 8m,
        HomePrediction = 4m,
        AwayPrediction = 4.5m,
        ErrorScale = 1m
    });
    Assert(result.ContextPrediction is null);
    Assert(result.UsableComponents.Count == 2);
}

static void ZeroErrorScale()
{
    var result = Consensus(new PredictionConsensusRequest
    {
        Line = 9.5m,
        Side = SelectionSide.Under,
        DirectPrediction = 8m,
        ContextPrediction = 9m,
        ErrorScale = 0m,
        NormalizationEpsilon = 0.01m
    });
    Close(result.NormalizedConsensusRange, 100m);
}

static void FutureConsensusComponent()
{
    var asOf = Utc(2026, 8, 20, 10);
    var result = Consensus(new PredictionConsensusRequest
    {
        Line = 10m,
        Side = SelectionSide.Under,
        DirectPrediction = 8m,
        ErrorScale = 1m,
        EvaluationAsOfUtc = asOf,
        AdditionalComponents =
        [
            Component(PredictionComponentType.Scenario, 15m, asOf.AddMinutes(1))
        ]
    });
    Close(result.WorstCasePrediction, 8m);
    Assert(result.SideAgreement);
}

static void ProbabilityAgreementNeedsEvidence()
{
    var asOf = Utc(2026, 8, 20, 10);
    var result = Consensus(new PredictionConsensusRequest
    {
        Line = 10m,
        Side = SelectionSide.Under,
        ErrorScale = 1m,
        AdditionalComponents =
        [
            Component(PredictionComponentType.Direct, 8m, asOf, 0.6m)
        ]
    });
    Assert(result.ProbabilityAgreementScore is null);
}

static void ReconciliationFallsBackToDirect()
{
    var result = Reconcile(
        [Component(PredictionComponentType.Direct, 10m), Component(PredictionComponentType.Context, 12m)],
        []);
    Close(result.ReconciledPrediction, 10m);
    Close(result.Weights[PredictionComponentType.Direct], 1m);
    Assert(result.FallbackReason == ReconciliationFallbackReason.InsufficientOutOfSampleValidation);
}

static void ReconciliationUsesInverseError()
{
    var result = Reconcile(
        [Component(PredictionComponentType.Direct, 10m), Component(PredictionComponentType.HomeAwaySum, 14m)],
        [Evidence(PredictionComponentType.Direct, 1m, 150m), Evidence(PredictionComponentType.HomeAwaySum, 2m, 150m)]);
    Close(result.Weights.Values.Sum(), 1m);
    Assert(result.Weights[PredictionComponentType.Direct] > result.Weights[PredictionComponentType.HomeAwaySum]);
    Close(result.ReconciledPrediction, 10.8m, 0.0001m);
}

static void UnvalidatedContextGetsNoWeight()
{
    var result = Reconcile(
        [Component(PredictionComponentType.Direct, 10m), Component(PredictionComponentType.Context, 100m)],
        [Evidence(PredictionComponentType.Direct, 1m, 150m)]);
    Assert(!result.Weights.ContainsKey(PredictionComponentType.Context));
    Close(result.ReconciledPrediction, 10m);
}

static void SmallSampleShrinksToDirect()
{
    var result = Reconcile(
        [Component(PredictionComponentType.Direct, 10m), Component(PredictionComponentType.HomeAwaySum, 20m)],
        [Evidence(PredictionComponentType.Direct, 2m, 30m), Evidence(PredictionComponentType.HomeAwaySum, 1m, 30m)],
        new PredictionReconciliationOptions
        {
            MinimumValidationEffectiveN = 30m,
            TargetValidationEffectiveN = 300m,
            MaximumSingleSourceWeight = 0.8m
        });
    Assert(result.Weights[PredictionComponentType.Direct] > 0.8m);
    Assert(result.ReconciledPrediction < 12m);
}

static void ResidualsRejectFutureOutcomes()
{
    var request = DistributionRequest();
    var valid = Observation(1, request.EvaluationAsOfUtc.AddDays(-2));
    var future = Observation(2, request.EvaluationAsOfUtc.AddHours(-1));
    var result = Bootstrap(request, [valid, future], Options(minN: 0m));
    Assert(result.RawObservationCount == 1);
    Assert(result.Warnings.Contains(RobustReasonCode.LookaheadDataDetected));
}

static void ResidualsRejectLateModel()
{
    var request = DistributionRequest();
    var valid = Observation(1, request.EvaluationAsOfUtc.AddDays(-2));
    var late = Observation(2, request.EvaluationAsOfUtc.AddDays(-3)) with
    {
        ModelTrainedThroughUtc = request.EvaluationAsOfUtc.AddDays(-3).AddHours(-2)
    };
    var result = Bootstrap(request, [valid, late], Options(minN: 0m));
    Assert(result.RawObservationCount == 1);
    Assert(result.Warnings.Contains(RobustReasonCode.ModelTrainedAfterFixture));
}

static void OutcomeLagBoundary()
{
    var request = DistributionRequest();
    var boundaryEnd = request.EvaluationAsOfUtc.AddHours(-8);
    var available = Observation(1, boundaryEnd);
    var unavailable = Observation(2, boundaryEnd.AddSeconds(1));
    var result = Bootstrap(request, [available, unavailable], Options(minN: 0m));
    Assert(result.RawObservationCount == 1);
}

static void ExplicitOutcomeAvailability()
{
    var request = DistributionRequest();
    var end = request.EvaluationAsOfUtc.AddHours(-2);
    var available = Observation(1, end) with
    {
        OutcomeAvailableUtc = request.EvaluationAsOfUtc
    };
    var future = Observation(2, end.AddHours(-1)) with
    {
        OutcomeAvailableUtc = request.EvaluationAsOfUtc.AddSeconds(1)
    };
    var result = Bootstrap(request, [available, future], Options(minN: 0m));
    Assert(result.RawObservationCount == 1);
}

static void ResidualFallbackHierarchy()
{
    var request = DistributionRequest();
    var observations = new List<HistoricalResidualObservation>
    {
        Observation(1, request.EvaluationAsOfUtc.AddDays(-2))
    };
    observations.AddRange(Enumerable.Range(2, 35).Select(index =>
        Observation(index, request.EvaluationAsOfUtc.AddDays(-index - 2)) with
        {
            MarketType = "AwayTeamShots",
            MarketScope = MarketScope.Away,
            Side = SelectionSide.Over,
            League = "Other"
        }));
    var result = Bootstrap(request, observations, Options(minN: 25m));
    Assert(result.FallbackLevel == ResidualFallbackLevel.MarketFamily);
    Assert(result.EffectiveObservationCount >= 25m);
}

static void EffectiveNFormula()
{
    var actual = EmpiricalResidualBootstrapV1.EffectiveSampleSize([1m, 1m, 2m]);
    Close(actual, 16m / 6m);
}

static void BootstrapIsDeterministic()
{
    var request = DistributionRequest();
    var observations = Enumerable.Range(1, 12)
        .Select(index => Observation(index, request.EvaluationAsOfUtc.AddDays(-index - 1)) with
        {
            ActualResult = 15m + index % 4
        })
        .ToArray();
    var first = Bootstrap(request, observations, Options(minN: 0m));
    var second = Bootstrap(request, observations.Reverse().ToArray(), Options(minN: 0m));
    Assert(first.DeterministicSeed == second.DeterministicSeed);
    Close(first.Distribution!.P10, second.Distribution!.P10);
    Close(first.Distribution.P50, second.Distribution.P50);
    Close(first.Distribution.P90, second.Distribution.P90);
    Assert(first.Distribution!.Histogram.OrderBy(item => item.Key)
        .SequenceEqual(second.Distribution!.Histogram.OrderBy(item => item.Key)));
}

static void DistributionIsValid()
{
    var request = WithPrediction(DistributionRequest(), 0.2m);
    var observations = Enumerable.Range(1, 8)
        .Select(index => Observation(index, request.EvaluationAsOfUtc.AddDays(-index - 1)) with
        {
            HistoricalPreMatchPrediction = 10m,
            ActualResult = index % 2 == 0 ? 0m : 1m
        })
        .ToArray();
    var distribution = Bootstrap(request, observations, Options(minN: 0m)).Distribution!;
    Close(distribution.PWin + distribution.PHalfWin + distribution.PPush
        + distribution.PHalfLoss + distribution.PLoss, 1m);
    Assert(distribution.Histogram.Keys.Min() >= 0);
}

static void SelectionBiasIsWarned()
{
    var request = DistributionRequest();
    var result = Bootstrap(request,
        [Observation(1, request.EvaluationAsOfUtc.AddDays(-2)) with
        {
            SourceScope = ResidualSourceScope.SelectedPicksOnly
        }],
        Options(minN: 0m));
    Assert(result.ResidualSourceScope == ResidualSourceScope.SelectedPicksOnly);
    Assert(result.Warnings.Contains(RobustReasonCode.EvidenceInsufficient));
}

static void ConfiguredMaeFallback()
{
    var request = DistributionRequest();
    var observation = Observation(1, request.EvaluationAsOfUtc.AddDays(-2)) with
    {
        HistoricalPreMatchPrediction = 15m,
        ActualResult = 15m
    };
    var options = WithMae(Options(minN: 0m), 2.25m);
    var result = Bootstrap(request, [observation], options);
    Assert(result.ErrorScaleMethod == ErrorScaleMethod.ConfiguredModelMae);
    Close(result.Distribution!.ErrorScale, 2.25m);
}

static void AsianEvUsesSettlementFactors()
{
    var result = new AsianValueCalculator().Calculate(2m,
        new AsianSettlementProbabilities(0.2m, 0.2m, 0.1m, 0.2m, 0.3m));
    Close(result.ExpectedPositiveFactor, 0.3m);
    Close(result.ExpectedNegativeFactor, 0.4m);
    Close(result.ExpectedValue, -0.1m);
}

static void AsianFairOdds()
{
    var result = new AsianValueCalculator().Calculate(2m,
        new AsianSettlementProbabilities(0.4m, 0.2m, 0.1m, 0.1m, 0.2m));
    Close(result.ExpectedPositiveFactor, 0.5m);
    Close(result.ExpectedNegativeFactor, 0.25m);
    Close(result.FairOdds, 1.5m);
    Close(result.ModelFairProbability, 2m / 3m);
}

static void AsianValueRejectsInvalidMass()
{
    Throws<ArgumentException>(() => new AsianValueCalculator().Calculate(2m,
        new AsianSettlementProbabilities(0.5m, 0m, 0m, 0m, 0.4m)));
}

static void CalibrationSmallSample()
{
    var service = new CalibrationReliabilityService();
    var low = service.Evaluate(CalibrationInput(10m, CalibrationFallbackLevel.ExactMarket), new());
    var high = service.Evaluate(CalibrationInput(150m, CalibrationFallbackLevel.ExactMarket), new());
    Assert(low.ReliabilityScore < high.ReliabilityScore);
}

static void CalibrationSpecificity()
{
    var service = new CalibrationReliabilityService();
    var exact = service.Evaluate(CalibrationInput(150m, CalibrationFallbackLevel.ExactMarket), new());
    var global = service.Evaluate(CalibrationInput(150m, CalibrationFallbackLevel.Global), new());
    Assert(global.SpecificityScore < exact.SpecificityScore);
    Assert(global.ReliabilityScore < exact.ReliabilityScore);
}

static void CalibrationIntervalWidth()
{
    var service = new CalibrationReliabilityService();
    CalibrationReliabilityInput Input(decimal n) => new(
        0.60m, 0.60m, null, null, n, (int)n, 0, 0,
        CalibrationFallbackLevel.ExactMarket, 1m, 0.02m, 0.9m, "cal-v1");
    var low = service.Evaluate(Input(10m), new());
    var high = service.Evaluate(Input(150m), new());
    Assert(low.LowerBound.HasValue && low.UpperBound.HasValue);
    Assert(high.LowerBound.HasValue && high.UpperBound.HasValue);
    Assert(low.UpperBound - low.LowerBound > high.UpperBound - high.LowerBound);
    Assert(low.IntervalMethod == "WilsonEffectiveN");
    Close(low.ConfidenceLevel, 0.90m);
}

static void CalibrationMissingMetadata()
{
    var result = new CalibrationReliabilityService().Evaluate(
        new CalibrationReliabilityInput(
            0.6m, 0.6m, null, null, null, 0, 0, 0,
            CalibrationFallbackLevel.Unavailable, null, null, null, "missing"),
        new());
    Close(result.ReliabilityScore, 0m);
}

static void RobustOuterScenarios()
{
    var result = new RobustValueEvaluationService().Evaluate(0.6m, 0.52m, 2m,
    [
        new("adverse", 0.48m, -0.04m, false, 0.2m, EvidenceStatus.AppliedNegative, true),
        new("base", 0.60m, 0.20m, true, 0.6m, EvidenceStatus.ReviewedNeutral, true),
        new("favorable", 0.70m, 0.40m, true, 0.2m, EvidenceStatus.AppliedPositive, true)
    ]);
    Close(result.RobustModelFairProbability, 0.48m);
    Close(result.RobustEdge, -0.04m);
    Close(result.RobustExpectedValue, -0.04m);
    Close(result.PositiveEvStability, 0.8m);
    Close(result.ScenarioSideStability, 0.8m);
}

static void RobustOuterMissingEvidence()
{
    var result = new RobustValueEvaluationService().Evaluate(0.6m, 0.52m, 2m,
    [
        new("missing", 0.7m, 0.4m, true, 1m, EvidenceStatus.InsufficientEvidence, true)
    ]);
    Assert(result.RobustExpectedValue is null);
    Assert(result.Warnings.Contains(RobustReasonCode.EvidenceInsufficient));
}

static void StakeNeverIncreases()
{
    var result = new RiskAdjustedStakeService().Recommend(1m, FullRobustness(), new()
    {
        AllowIncrease = true,
        HighMultiplier = 1.5m
    });
    Close(result.RecommendedStake, 1m);
}

static void StakeBands()
{
    var components = UniformRobustness(0.82m);
    var result = new RiskAdjustedStakeService().Recommend(2m, components, new());
    Close(result.StakeMultiplier, 0.75m);
    Close(result.RecommendedStake, 1.5m);
}

static void MissingStakeEvidence()
{
    var missing = new RobustnessComponents(null, null, null, null, null, null, null, null, null);
    var result = new RiskAdjustedStakeService().Recommend(1m, missing, new());
    Close(result.RobustnessScore, 0m);
    Close(result.RecommendedStake, 0m);
}

static void PolicyAccumulatesReasons()
{
    var input = WithFailures(PassingPolicyInput());
    var result = new RobustPickPolicyEvaluator().Evaluate(input, new());
    Assert(result.RejectionReasons.Contains(RobustReasonCode.DataQualityTooLow));
    Assert(result.RejectionReasons.Contains(RobustReasonCode.LookaheadDataDetected));
    Assert(result.RejectionReasons.Contains(RobustReasonCode.ResidualSampleTooSmall));
    Assert(result.RejectionReasons.Contains(RobustReasonCode.SideDisagreement));
    Assert(result.RejectionReasons.Contains(RobustReasonCode.RobustEvNotPositive));
    Assert(result.RejectionReasons.Count >= 10);
}

static void ShadowDoesNotEnforce()
{
    var input = PassingPolicyInput() with
    {
        Mode = EvaluationMode.Shadow,
        RobustExpectedValue = -0.1m
    };
    var result = new RobustPickPolicyEvaluator().Evaluate(input, new());
    Assert(result.RobustDecision == RobustDecision.Reject);
    Assert(result.EffectiveDecision == RobustDecision.Approve);
    Close(result.EffectiveStake, input.OriginalStake);
    Assert(!result.ChangesCurrentBehavior);
}

static void EnforceRejects()
{
    var input = PassingPolicyInput() with
    {
        Mode = EvaluationMode.Enforce,
        RobustExpectedValue = -0.1m
    };
    var result = new RobustPickPolicyEvaluator().Evaluate(input, new());
    Assert(result.EffectiveDecision == RobustDecision.Reject);
    Close(result.EffectiveStake, 0m);
    Assert(result.ChangesCurrentBehavior);
}

static void MissingEvidenceIsNotNeutral()
{
    var missing = new RobustPickPolicyEvaluator().Evaluate(PassingPolicyInput() with
    {
        IntelligenceEvidenceStatus = EvidenceStatus.InsufficientEvidence
    }, new());
    var neutral = new RobustPickPolicyEvaluator().Evaluate(PassingPolicyInput() with
    {
        IntelligenceEvidenceStatus = EvidenceStatus.ReviewedNeutral
    }, new());
    Assert(missing.Warnings.Contains(RobustReasonCode.EvidenceInsufficient));
    Assert(!neutral.Warnings.Contains(RobustReasonCode.EvidenceInsufficient));
}

static void MissingCoherenceIsWarned()
{
    var result = new RobustPickPolicyEvaluator().Evaluate(PassingPolicyInput() with
    {
        NormalizedCoherenceGap = null
    }, new());
    Assert(result.Warnings.Contains(RobustReasonCode.EvidenceInsufficient));
}

static void NoVigWarning()
{
    var result = new RobustPickPolicyEvaluator().Evaluate(PassingPolicyInput() with
    {
        NoVigStatus = NoVigStatus.Unavailable
    }, new());
    Assert(result.Warnings.Contains(RobustReasonCode.NoVigUnavailable));
    Assert(result.RobustDecision == RobustDecision.Approve);
}

static void StaleOddsReject()
{
    var result = new RobustPickPolicyEvaluator().Evaluate(PassingPolicyInput() with
    {
        OddsAreFresh = false
    }, new());
    Assert(result.RejectionReasons.Contains(RobustReasonCode.OddsTooOld));
    Assert(result.RobustDecision == RobustDecision.Reject);
}

static void ConservativeNoVig()
{
    var service = new RobustMarketProbabilityService();
    var over = service.Calculate(new(
        SelectionSide.Over, 1.80m, 1.80m, 2.20m, 24.5m));
    var under = service.Calculate(new(
        SelectionSide.Under, 2.20m, 1.80m, 2.20m, 24.5m));
    Assert(over.Status == NoVigStatus.Available);
    Assert(over.Method.Contains("Proportional,Power", StringComparison.Ordinal));
    Close(over.ProportionalSelectedProbability + under.ProportionalSelectedProbability, 1m);
    Close(over.PowerSelectedProbability + under.PowerSelectedProbability, 1m);
    Assert(over.ConservativeSelectedProbability >= over.ProportionalSelectedProbability);
    Assert(over.ConservativeSelectedProbability >= over.PowerSelectedProbability);
}

static void SingleSidedMarketProbability()
{
    var result = new RobustMarketProbabilityService().Calculate(new(
        SelectionSide.Over, 2m, 2m, null, 9.5m));
    Assert(result.Status == NoVigStatus.Unavailable);
    Assert(result.ConservativeSelectedProbability is null);
    Close(result.SelectedRawImpliedProbability, 0.5m);
}

static void FixtureExposure()
{
    var candidate = Pick("new", 1, 1m, 0.8m);
    var existing = Pick("old", 1, 1m, 0.9m);
    var result = new PortfolioExposureService().Allocate([candidate], [existing], new()
    {
        MaximumStakePerFixture = 1.5m
    }).Single();
    Close(result.ApprovedStake, 0.5m);
    Assert(result.ReasonCodes.Contains(RobustReasonCode.ExposureLimitExceeded));
}

static void TeamExposure()
{
    var existing = Pick("old", 1, 1m, 0.9m) with { HomeTeamKey = "A", AwayTeamKey = "B" };
    var candidate = Pick("new", 2, 1m, 0.8m) with { HomeTeamKey = "C", AwayTeamKey = "A" };
    var result = new PortfolioExposureService().Allocate([candidate], [existing], new()
    {
        MaximumStakePerTeam = 1m
    }).Single();
    Close(result.ApprovedStake, 0m);
}

static void ClusterExposure()
{
    var existing = Pick("old", 1, 0.5m, 0.9m) with { CorrelationCluster = "high-tempo" };
    var candidate = Pick("new", 2, 0.5m, 0.8m) with { CorrelationCluster = "HIGH-TEMPO" };
    var result = new PortfolioExposureService().Allocate([candidate], [existing], new()
    {
        MaximumStakePerCorrelationCluster = 0.75m
    }).Single();
    Close(result.ApprovedStake, 0.25m);
    Assert(result.ReasonCodes.Contains(RobustReasonCode.CorrelatedExposureLimitExceeded));
}

static void HighestRobustnessWins()
{
    var high = Pick("high", 1, 1m, 0.95m);
    var low = Pick("low", 1, 1m, 0.75m);
    var results = new PortfolioExposureService().Allocate([low, high], [], new()
    {
        MaximumStakePerFixture = 1m,
        MaximumRelatedPicksPerFixture = 1
    });
    Close(results.Single(item => item.Pick.PickKey == "high").ApprovedStake, 1m);
    Assert(results.Single(item => item.Pick.PickKey == "low").IsRejected);
}

static void MissingScenarioEvidenceIsNotNeutral()
{
    var request = ScenarioRequest();
    foreach (var provider in ScenarioProviders())
    {
        var result = provider.Evaluate(request);
        Assert(!result.IsUsable);
        Assert(result.EvidenceStatus == EvidenceStatus.InsufficientEvidence);
        Assert(result.EvidenceStatus != EvidenceStatus.ReviewedNeutral);
        Close(result.ProbabilityWeight, 0m);
        Close(result.PredictionAdjustment, 0m);
        Close(result.ProbabilityAdjustment, 0m);
    }
}

static void ScenarioUnavailableStatesArePreserved()
{
    var notApplicable = new LineupScenarioProvider(new ScenarioDataReadinessEvaluator()).Evaluate(
        ScenarioRequest(applicable: [ScenarioType.Base]));
    Assert(!notApplicable.IsUsable);
    Assert(notApplicable.EvidenceStatus == EvidenceStatus.NotApplicable);

    var sourceUnavailableEvidence = ScenarioEvidence(
        ScenarioType.Intelligence,
        EvidenceStatus.SourceUnavailable) with
    {
        HasStructuredEvidence = false,
        EvidenceIds = [],
        Reason = "INTELLIGENCE_PROVIDER_DOWN"
    };
    var sourceUnavailable = new IntelligenceScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(sourceUnavailableEvidence));
    Assert(!sourceUnavailable.IsUsable);
    Assert(sourceUnavailable.EvidenceStatus == EvidenceStatus.SourceUnavailable);
    Assert(sourceUnavailable.Reason == "INTELLIGENCE_PROVIDER_DOWN");
    Close(sourceUnavailable.PredictionAdjustment, 0m);
}

static void ScenarioExpiryAndLookahead()
{
    var evaluationAsOf = Utc(2026, 8, 20, 12);
    var expiredEvidence = ScenarioEvidence(
        ScenarioType.Lineup,
        EvidenceStatus.AppliedPositive,
        evaluationAsOf) with
    {
        ExpiresUtc = evaluationAsOf
    };
    var expired = new LineupScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(expiredEvidence, evaluationAsOf: evaluationAsOf));
    Assert(!expired.IsUsable);
    Assert(expired.EvidenceStatus == EvidenceStatus.SnapshotExpired);
    Close(expired.ProbabilityAdjustment, 0m);

    var futureEvidence = ScenarioEvidence(
        ScenarioType.Lineup,
        EvidenceStatus.AppliedPositive,
        evaluationAsOf.AddMinutes(1));
    var future = new LineupScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(futureEvidence, evaluationAsOf: evaluationAsOf));
    Assert(!future.IsUsable);
    Assert(future.EvidenceStatus == EvidenceStatus.InsufficientEvidence);
    Assert(future.Reason == "LOOKAHEAD_SCENARIO_EVIDENCE_DETECTED");
}

static void BaseScenarioRequiresEvidence()
{
    var provider = new BaseScenarioProvider(new ScenarioDataReadinessEvaluator());
    var missing = provider.Evaluate(ScenarioRequest());
    Assert(!missing.IsUsable);

    var evidence = ScenarioEvidence(
        ScenarioType.Base,
        EvidenceStatus.ReviewedNeutral) with
    {
        ScenarioName = "Base model snapshot",
        ProbabilityWeight = 1m,
        IsAdjustmentValidated = false,
        PredictionAdjustment = 0m,
        ProbabilityAdjustment = 0m,
        AdjustmentVersion = null,
        EvidenceIds = ["model-snapshot-123"]
    };
    var result = provider.Evaluate(ScenarioRequest(evidence));
    Assert(result.IsUsable);
    Assert(result.EvidenceStatus == EvidenceStatus.ReviewedNeutral);
    Close(result.ProbabilityWeight, 1m);
    Close(result.PredictionAdjustment, 0m);
    Close(result.ProbabilityAdjustment, 0m);
}

static void ValidatedScenarioAdjustment()
{
    var evidence = ScenarioEvidence(
        ScenarioType.Lineup,
        EvidenceStatus.AppliedPositive) with
    {
        PredictionAdjustment = 0.40m,
        ProbabilityAdjustment = 0.03m,
        ProbabilityWeight = 0.25m,
        Confidence = 0.80m,
        AdjustmentVersion = "lineup-impact-oos-v1"
    };
    var result = new LineupScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(evidence));
    Assert(result.IsUsable);
    Assert(result.EvidenceStatus == EvidenceStatus.AppliedPositive);
    Close(result.PredictionAdjustment, 0.40m);
    Close(result.ProbabilityAdjustment, 0.03m);
    Close(result.ProbabilityWeight, 0.25m);
    Close(result.Confidence, 0.80m);
}

static void UnvalidatedScenarioAdjustment()
{
    var evidence = ScenarioEvidence(
        ScenarioType.Fatigue,
        EvidenceStatus.AppliedPositive) with
    {
        IsAdjustmentValidated = false,
        AdjustmentVersion = null,
        PredictionAdjustment = 2m,
        ProbabilityAdjustment = 0.10m
    };
    var result = new FatigueScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(evidence));
    Assert(!result.IsUsable);
    Assert(result.EvidenceStatus == EvidenceStatus.InsufficientEvidence);
    Close(result.PredictionAdjustment, 0m);
    Close(result.ProbabilityAdjustment, 0m);
}

static void ReviewedNeutralScenario()
{
    var evidence = ScenarioEvidence(
        ScenarioType.Intelligence,
        EvidenceStatus.ReviewedNeutral) with
    {
        IsAdjustmentValidated = false,
        AdjustmentVersion = null,
        PredictionAdjustment = null,
        ProbabilityAdjustment = null,
        EvidenceIds = ["intelligence-snapshot-reviewed"]
    };
    var result = new IntelligenceScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(evidence));
    Assert(result.IsUsable);
    Assert(result.EvidenceStatus == EvidenceStatus.ReviewedNeutral);
    Close(result.PredictionAdjustment, 0m);
    Close(result.ProbabilityAdjustment, 0m);
}

static void GameStateRequiresEvents()
{
    var evidence = ScenarioEvidence(
        ScenarioType.GameState,
        EvidenceStatus.AppliedNegative) with
    {
        PredictionAdjustment = -0.5m,
        ProbabilityAdjustment = -0.02m,
        HistoricalEventObservationCount = 0
    };
    var result = new GameStateScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(evidence));
    Assert(!result.IsUsable);
    Assert(result.EvidenceStatus == EvidenceStatus.InsufficientEvidence);
    Assert(result.Reason == "GAME_STATE_EVENT_HISTORY_UNAVAILABLE");
    Close(result.PredictionAdjustment, 0m);
}

static void ScenarioSignMismatch()
{
    var evidence = ScenarioEvidence(
        ScenarioType.MarketMovement,
        EvidenceStatus.AppliedNegative) with
    {
        PredictionAdjustment = 0.5m,
        ProbabilityAdjustment = 0.02m
    };
    var result = new MarketMovementScenarioProvider(new ScenarioDataReadinessEvaluator())
        .Evaluate(ScenarioRequest(evidence));
    Assert(!result.IsUsable);
    Assert(result.EvidenceStatus == EvidenceStatus.InsufficientEvidence);
    Assert(result.Reason == "SCENARIO_ADJUSTMENT_SIGN_MISMATCH");
    Close(result.PredictionAdjustment, 0m);
    Close(result.ProbabilityAdjustment, 0m);
}

static void StableReasonCodes()
{
    Assert(RobustReasonCode.RobustEvNotPositive.ToStableCode() == "ROBUST_EV_NOT_POSITIVE");
    Assert(RobustReasonCode.LookaheadDataDetected.ToStableCode() == "LOOKAHEAD_DATA_DETECTED");
    Assert(RobustReasonCode.ExposureLimitExceeded.ToStableCode() == "EXPOSURE_LIMIT_EXCEEDED");
}

static PredictionConsensusResult Consensus(PredictionConsensusRequest request) =>
    new PredictionConsensusService().Evaluate(request);

static IReadOnlyList<IScenarioProvider> ScenarioProviders()
{
    var readiness = new ScenarioDataReadinessEvaluator();
    return
    [
        new BaseScenarioProvider(readiness),
        new LineupScenarioProvider(readiness),
        new IntelligenceScenarioProvider(readiness),
        new FatigueScenarioProvider(readiness),
        new GameStateScenarioProvider(readiness),
        new MarketMovementScenarioProvider(readiness)
    ];
}

static ScenarioProviderRequest ScenarioRequest(
    ScenarioEvidenceSnapshot? evidence = null,
    DateTime? evaluationAsOf = null,
    IReadOnlyCollection<ScenarioType>? applicable = null)
{
    var items = evidence is null
        ? new Dictionary<ScenarioType, ScenarioEvidenceSnapshot>()
        : new Dictionary<ScenarioType, ScenarioEvidenceSnapshot>
        {
            [evidence.ScenarioType] = evidence
        };
    return new ScenarioProviderRequest
    {
        EvaluationAsOfUtc = evaluationAsOf ?? Utc(2026, 8, 20, 12),
        MarketFamily = MarketFamily.Shots,
        MarketType = "TotalShots",
        BasePrediction = 22m,
        BaseProbability = 0.60m,
        MinimumGameStateEventObservations = 10,
        ApplicableScenarioTypes = applicable ?? Enum.GetValues<ScenarioType>(),
        Evidence = items
    };
}

static ScenarioEvidenceSnapshot ScenarioEvidence(
    ScenarioType type,
    EvidenceStatus status,
    DateTime? asOf = null) => new(
        type,
        type.ToString(),
        status,
        true,
        true,
        0.5m,
        status == EvidenceStatus.AppliedNegative ? -0.25m : 0.25m,
        status == EvidenceStatus.AppliedNegative ? -0.01m : 0.01m,
        0.75m,
        [$"{type}-evidence-1"],
        asOf ?? Utc(2026, 8, 20, 10),
        Utc(2026, 8, 20, 14),
        "validated-adjustment-v1",
        100,
        null);

static PredictionReconciliationResult Reconcile(
    IReadOnlyCollection<PredictionComponent> components,
    IReadOnlyCollection<ComponentValidationEvidence> evidence,
    PredictionReconciliationOptions? options = null) =>
    new PredictionReconciliationService().Reconcile(components, evidence, options ?? new(), "test-v1");

static PredictionComponent Component(
    PredictionComponentType type,
    decimal value,
    DateTime? asOf = null,
    decimal? probability = null) =>
    new(type, value, probability, 1m, true, "test", asOf ?? Utc(2026, 8, 1), null, 1m);

static ComponentValidationEvidence Evidence(
    PredictionComponentType type,
    decimal error,
    decimal n) => new(type, error, n, "validation-v1");

static PredictiveDistributionRequest DistributionRequest() => new()
{
    FixtureId = 999,
    EvaluationAsOfUtc = Utc(2026, 8, 20, 12),
    MarketFamily = MarketFamily.Shots,
    MarketType = "TotalShots",
    MarketScope = MarketScope.Total,
    Side = SelectionSide.Under,
    Line = 24.5m,
    ReconciledPrediction = 22m,
    Odds = 1.90m,
    League = "Premier League",
    ModelVersion = "shots-v1",
    RobustnessVersion = "robust-v1"
};

static PredictiveDistributionRequest WithPrediction(
    PredictiveDistributionRequest source,
    decimal prediction) => new()
{
    FixtureId = source.FixtureId,
    EvaluationAsOfUtc = source.EvaluationAsOfUtc,
    MarketFamily = source.MarketFamily,
    MarketType = source.MarketType,
    MarketScope = source.MarketScope,
    Side = source.Side,
    Line = source.Line,
    ReconciledPrediction = prediction,
    Odds = source.Odds,
    League = source.League,
    ModelVersion = source.ModelVersion,
    RobustnessVersion = source.RobustnessVersion
};

static HistoricalResidualObservation Observation(long id, DateTime fixtureEnd) => new(
    id,
    fixtureEnd.AddHours(-2),
    fixtureEnd,
    fixtureEnd.AddHours(-3),
    fixtureEnd.AddDays(-30),
    MarketFamily.Shots,
    "TotalShots",
    MarketScope.Total,
    SelectionSide.Under,
    "Premier League",
    24.5m,
    1.90m,
    15m,
    16m,
    1m,
    "shots-v1",
    ResidualSourceScope.AllCandidates);

static EmpiricalResidualBootstrapOptions Options(decimal minN) => new()
{
    OutcomeAvailabilityLag = TimeSpan.FromHours(8),
    SimulationCount = 400,
    MinimumEffectiveN = minN,
    TargetEffectiveN = 150m,
    RecencyHalfLifeDays = 365m
};

static EmpiricalResidualBootstrapOptions WithMae(
    EmpiricalResidualBootstrapOptions source,
    decimal mae) => new()
{
    OutcomeAvailabilityLag = source.OutcomeAvailabilityLag,
    SimulationCount = source.SimulationCount,
    ProbabilityLowerQuantile = source.ProbabilityLowerQuantile,
    ProbabilityUpperQuantile = source.ProbabilityUpperQuantile,
    MinimumEffectiveN = source.MinimumEffectiveN,
    TargetEffectiveN = source.TargetEffectiveN,
    RecencyHalfLifeDays = source.RecencyHalfLifeDays,
    LineBandWidth = source.LineBandWidth,
    LineSimilarityScale = source.LineSimilarityScale,
    OddsSimilarityScale = source.OddsSimilarityScale,
    UseLineSimilarity = source.UseLineSimilarity,
    UseOddsSimilarity = source.UseOddsSimilarity,
    SameModelVersionWeight = source.SameModelVersionWeight,
    DifferentModelVersionWeight = source.DifferentModelVersionWeight,
    SameLeagueWeight = source.SameLeagueWeight,
    DifferentLeagueWeight = source.DifferentLeagueWeight,
    Epsilon = source.Epsilon,
    ConfiguredModelMae = mae
};

static PredictiveDistributionResult Bootstrap(
    PredictiveDistributionRequest request,
    IReadOnlyCollection<HistoricalResidualObservation> observations,
    EmpiricalResidualBootstrapOptions options) =>
    new EmpiricalResidualBootstrapV1().Build(
        request,
        observations,
        options,
        new TestSettlementAdapter());

static CalibrationReliabilityInput CalibrationInput(
    decimal n,
    CalibrationFallbackLevel fallback) => new(
        0.60m,
        0.58m,
        0.50m,
        0.66m,
        n,
        fallback == CalibrationFallbackLevel.ExactMarket ? (int)n : 0,
        fallback == CalibrationFallbackLevel.MarketFamily ? (int)n : 0,
        fallback == CalibrationFallbackLevel.Global ? (int)n : 0,
        fallback,
        10m,
        0.02m,
        0.9m,
        "cal-v1");

static RobustnessComponents FullRobustness() => UniformRobustness(1m);

static RobustnessComponents UniformRobustness(decimal value) =>
    new(value, value, value, value, value, value, value, value, value);

static RobustPickPolicyInput PassingPolicyInput() => new()
{
    Mode = EvaluationMode.Shadow,
    CurrentDecision = CurrentSystemDecision.Bet,
    OriginalStake = 1m,
    RiskAdjustedStake = 1m,
    RobustnessScore = 0.95m,
    ResidualEffectiveN = 100m,
    SideAgreement = true,
    NormalizedWorstCaseDistance = 0.50m,
    NormalizedConsensusRange = 0.40m,
    NormalizedCoherenceGap = 0.40m,
    CalibrationReliability = 0.80m,
    PointEdge = 0.05m,
    PointExpectedValue = 0.08m,
    RobustEdge = 0.02m,
    RobustExpectedValue = 0.03m,
    PositiveEvStability = 0.90m,
    ScenarioSideStability = 0.90m,
    DataQualityScore = 0.90m,
    IntelligenceEvidenceStatus = EvidenceStatus.ReviewedNeutral
};

static RobustPickPolicyInput WithFailures(RobustPickPolicyInput source) => new()
{
    Mode = source.Mode,
    CurrentDecision = source.CurrentDecision,
    OriginalStake = source.OriginalStake,
    RiskAdjustedStake = 0m,
    RobustnessScore = 0.2m,
    DataIsValid = false,
    TemporalDataIsValid = false,
    ModelWasTrainedBeforeFixture = false,
    MarketPriceAvailable = false,
    OddsAreFresh = false,
    NoVigStatus = NoVigStatus.Unavailable,
    ErrorScaleAvailable = false,
    ResidualEffectiveN = 1m,
    SideAgreement = false,
    NormalizedWorstCaseDistance = -1m,
    NormalizedConsensusRange = 2m,
    NormalizedCoherenceGap = 2m,
    CalibrationReliability = 0.1m,
    PointEdge = -0.1m,
    PointExpectedValue = -0.1m,
    RobustEdge = -0.2m,
    RobustExpectedValue = -0.2m,
    PositiveEvStability = 0.1m,
    ScenarioSideStability = 0.1m,
    DataQualityScore = 0.1m,
    ExposureAvailable = false,
    CorrelatedExposureAvailable = false,
    IntelligenceEvidenceStatus = EvidenceStatus.SourceUnavailable,
    SnapshotExpired = true,
    MarketAutomationNameMatches = false
};

static PortfolioPick Pick(string key, long fixtureId, decimal stake, decimal robustness) => new(
    key,
    fixtureId,
    "home",
    "away",
    "league",
    MarketFamily.Shots,
    "bot",
    new DateOnly(2026, 8, 20),
    "cluster",
    stake,
    robustness);

static DateTime Utc(int year, int month, int day, int hour = 0) =>
    new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

static void Close(decimal? actual, decimal expected, decimal tolerance = 0.000001m)
{
    if (!actual.HasValue)
    {
        throw new InvalidOperationException($"Expected {expected}, got null.");
    }
    if (Math.Abs(actual.Value - expected) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual.Value}.");
    }
}

static void Assert(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed.");
    }
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

sealed class TestSettlementAdapter : ISettlementAdapter
{
    public string SettlementVersion => "test-asian-v1";

    public SettlementOutcome Settle(decimal line, SelectionSide side, int actualResult)
    {
        var fraction = line - decimal.Floor(line);
        decimal factor;
        if (fraction == 0.25m)
        {
            factor = (Factor(decimal.Floor(line), side, actualResult)
                + Factor(decimal.Floor(line) + 0.5m, side, actualResult)) / 2m;
        }
        else if (fraction == 0.75m)
        {
            factor = (Factor(decimal.Floor(line) + 0.5m, side, actualResult)
                + Factor(decimal.Floor(line) + 1m, side, actualResult)) / 2m;
        }
        else
        {
            factor = Factor(line, side, actualResult);
        }

        return factor switch
        {
            1m => SettlementOutcome.Win,
            0.5m => SettlementOutcome.HalfWin,
            0m => SettlementOutcome.Push,
            -0.5m => SettlementOutcome.HalfLoss,
            -1m => SettlementOutcome.Loss,
            _ => throw new InvalidOperationException($"Unsupported settlement factor {factor}.")
        };
    }

    private static decimal Factor(decimal line, SelectionSide side, int actual)
    {
        if (actual == line)
        {
            return 0m;
        }
        var overWins = actual > line;
        return side == SelectionSide.Over == overWins ? 1m : -1m;
    }
}
