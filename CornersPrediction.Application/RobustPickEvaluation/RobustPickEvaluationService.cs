using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Domain.RobustPickEvaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersistenceResidual = CornersPrediction.Application.RobustPickEvaluation.RobustResidualObservation;

namespace CornersPrediction.Application.RobustPickEvaluation;

public interface IRobustPickEvaluationService
{
    Task<RobustPickEvaluationExecution?> EvaluateAsync(
        RobustPickEvaluationInput input,
        bool persist,
        CancellationToken cancellationToken);

    Task<AppendRobustEvaluationResult> PersistAsync(
        RobustPickEvaluationExecution execution,
        long? botPickSelectionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Stable input captured at the current selector boundary.  Every timestamp is
/// explicit so a historical run cannot silently fall back to the wall clock.
/// </summary>
public sealed class RobustPickEvaluationInput
{
    public long? SourceEvaluationId { get; init; }
    public long? BotPickSelectionId { get; init; }
    public long? SourceOddsSnapshotId { get; init; }
    public required string EvaluationSubjectKey { get; init; }
    public required string BotKey { get; init; }
    public required string MarketFamily { get; init; }
    public required string MarketType { get; init; }
    public required string SelectedSide { get; init; }
    public required string League { get; init; }
    public required string HomeTeam { get; init; }
    public required string AwayTeam { get; init; }
    public required string Bookmaker { get; init; }
    public required string AutomationVersion { get; init; }
    /// <summary>
    /// Stable logical fixture identifier used by deterministic simulation and
    /// exposure controls. It may fall back to the local upcoming-odds identity.
    /// </summary>
    public long FixtureId { get; init; }
    /// <summary>
    /// Official external fixture identifier when one was resolved. This is the
    /// only fixture identifier persisted as external lineage.
    /// </summary>
    public long? ExternalFixtureId { get; init; }
    public DateTime FixtureStartUtc { get; init; }
    public DateTime PredictionAsOfUtc { get; init; }
    public DateTime EvaluationAsOfUtc { get; init; }
    public DateTime? QuoteTimestampUtc { get; init; }
    public decimal Line { get; init; }
    public decimal SelectedOdds { get; init; }
    public decimal? OverOdds { get; init; }
    public decimal? UnderOdds { get; init; }
    public decimal OriginalStake { get; init; }
    public decimal CurrentMinimumPointEdge { get; init; }
    public decimal CurrentMinimumPointExpectedValue { get; init; }
    public CurrentSystemDecision CurrentDecision { get; init; } = CurrentSystemDecision.Bet;
    public EvaluationMode? EvaluationModeOverride { get; init; }

    public decimal PrimaryPrediction { get; init; }
    public decimal? DirectPrediction { get; init; }
    public decimal? HomePrediction { get; init; }
    public decimal? AwayPrediction { get; init; }
    public decimal? ContextPrediction { get; init; }
    public decimal? ConfiguredModelMae { get; init; }
    public decimal RawProbability { get; init; }
    public decimal CalibratedProbability { get; init; }
    public decimal? ProbabilityBeforeIntelligence { get; init; }
    public decimal? ProbabilityLowerBound { get; init; }
    public decimal? ProbabilityUpperBound { get; init; }
    public decimal DataQualityScore { get; init; } = 0.5m;

    public string BaseModelVersion { get; init; } = "unknown";
    public DateTime? ModelTrainedThroughUtc { get; init; }
    public string? SelectorVersion { get; init; }
    public string? CalibrationVersion { get; init; }
    public string? IntelligenceVersion { get; init; }
    public string SettlementVersion { get; init; } = CanonicalSettlementAdapter.Version;

    public decimal? CalibrationEffectiveN { get; init; }
    public int CalibrationExactMarketN { get; init; }
    public int CalibrationFamilyN { get; init; }
    public int CalibrationGlobalN { get; init; }
    public CalibrationFallbackLevel CalibrationFallbackLevel { get; init; } = CalibrationFallbackLevel.Unavailable;
    public decimal? CalibrationEvidenceAgeDays { get; init; }
    public decimal? CalibrationError { get; init; }
    public decimal? CalibrationPriorWeight { get; init; }
    public string? CalibrationIntervalMethod { get; init; }
    public decimal? CalibrationConfidenceLevel { get; init; }
    public IReadOnlyList<ComponentValidationEvidence> ComponentValidation { get; init; } = [];
    public IReadOnlyDictionary<ScenarioType, ScenarioEvidenceSnapshot> ScenarioEvidence { get; init; } =
        new Dictionary<ScenarioType, ScenarioEvidenceSnapshot>();

    public EvidenceStatus IntelligenceEvidenceStatus { get; init; } = EvidenceStatus.NotApplicable;
    public string LineupStatus { get; init; } = nameof(EvidenceStatus.NotApplicable);
    public string FatigueDataStatus { get; init; } = nameof(EvidenceStatus.NotApplicable);
    public string GameStateModelStatus { get; init; } = nameof(EvidenceStatus.NotApplicable);
    public int ActionableFactCount { get; init; }
    public int IndependentSourceCount { get; init; }
    public int? IntelligenceSnapshotAgeMinutes { get; init; }
    public int? MaxOddsAgeSeconds { get; init; }
}

public sealed record RobustPickEvaluationExecution(
    AppendRobustPickEvaluationCommand Snapshot,
    RobustPickEvaluationResult Decision,
    AppendRobustEvaluationResult? Persistence,
    PredictionConsensusResult Consensus,
    PredictiveDistributionResult Distribution,
    CalibrationReliabilityResult Calibration,
    RobustValueEvaluationResult Value,
    RiskAdjustedStakeResult Stake,
    PortfolioPick ExposurePick,
    IReadOnlyList<string> WarningCodes,
    long DurationMilliseconds);

public sealed class RobustPickEvaluationService : IRobustPickEvaluationService
{
    private const string ReconciliationVersion = "validated-inverse-error-v1";
    private const int MaximumLiveResidualRows = 5_000;
    private static readonly TimeSpan ResidualEvidenceTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExposureEvidenceTimeout = TimeSpan.FromSeconds(5);
    private static readonly Meter Metrics = new("CornersPrediction.RobustPickEvaluation", "1.0.0");
    private static readonly Counter<long> EvaluationCounter = Metrics.CreateCounter<long>("robust_evaluations_total");
    private static readonly Counter<long> EvaluationFailedCounter = Metrics.CreateCounter<long>("robust_evaluations_failed_total");
    private static readonly Counter<long> ApproveCounter = Metrics.CreateCounter<long>("robust_decision_approve_total");
    private static readonly Counter<long> RejectCounter = Metrics.CreateCounter<long>("robust_decision_reject_total");
    private static readonly Counter<long> ReduceStakeCounter = Metrics.CreateCounter<long>("robust_decision_reduce_stake_total");
    private static readonly Counter<long> OddsStaleCounter = Metrics.CreateCounter<long>("robust_odds_stale_total");
    private static readonly Counter<long> LeakageRejectedCounter = Metrics.CreateCounter<long>("robust_data_leakage_rejected_total");
    private static readonly Counter<long> ShadowDisagreementCounter = Metrics.CreateCounter<long>("robust_shadow_disagreement_total");
    private static readonly Histogram<double> SimulationDuration = Metrics.CreateHistogram<double>("robust_simulation_duration_ms", "ms");
    private static readonly Histogram<double> ResidualEffectiveN = Metrics.CreateHistogram<double>("robust_residual_effective_n");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RobustPickEvaluationOptions _options;
    private readonly IRobustPickEvaluationRepository _repository;
    private readonly IPredictionConsensusService _consensus;
    private readonly IPredictionReconciliationService _reconciliation;
    private readonly IPredictiveDistributionService _distribution;
    private readonly IAsianValueCalculator _asianValue;
    private readonly IRobustMarketProbabilityService _marketProbability;
    private readonly ICalibrationReliabilityService _calibration;
    private readonly IRobustValueEvaluationService _robustValue;
    private readonly IRiskAdjustedStakeService _stake;
    private readonly IPortfolioExposureService _exposure;
    private readonly IRobustPickPolicyEvaluator _policy;
    private readonly IReadOnlyList<IScenarioProvider> _scenarioProviders;
    private readonly ILogger<RobustPickEvaluationService> _logger;
    private readonly object _requestCacheLock = new();
    private readonly Dictionary<ResidualCacheKey, Task<IReadOnlyList<PersistenceResidual>>> _residualCache = [];
    private readonly Dictionary<DateTime, Task<IReadOnlyList<OpenPortfolioExposureDto>>> _exposureCache = [];
    private readonly Dictionary<DateTime, List<SessionExposurePick>> _sessionExposure = [];

    public RobustPickEvaluationService(
        IOptions<RobustPickEvaluationOptions> options,
        IRobustPickEvaluationRepository repository,
        IPredictionConsensusService consensus,
        IPredictionReconciliationService reconciliation,
        IPredictiveDistributionService distribution,
        IAsianValueCalculator asianValue,
        IRobustMarketProbabilityService marketProbability,
        ICalibrationReliabilityService calibration,
        IRobustValueEvaluationService robustValue,
        IRiskAdjustedStakeService stake,
        IPortfolioExposureService exposure,
        IRobustPickPolicyEvaluator policy,
        IEnumerable<IScenarioProvider> scenarioProviders,
        ILogger<RobustPickEvaluationService> logger)
    {
        _options = options.Value;
        _repository = repository;
        _consensus = consensus;
        _reconciliation = reconciliation;
        _distribution = distribution;
        _asianValue = asianValue;
        _marketProbability = marketProbability;
        _calibration = calibration;
        _robustValue = robustValue;
        _stake = stake;
        _exposure = exposure;
        _policy = policy;
        _scenarioProviders = scenarioProviders
            .OrderBy(provider => provider.ScenarioType)
            .ToArray();
        _logger = logger;
    }

    public async Task<RobustPickEvaluationExecution?> EvaluateAsync(
        RobustPickEvaluationInput input,
        bool persist,
        CancellationToken cancellationToken)
    {
        try
        {
            return await EvaluateCoreAsync(input, persist, cancellationToken);
        }
        catch
        {
            EvaluationFailedCounter.Add(1);
            throw;
        }
    }

    private async Task<RobustPickEvaluationExecution?> EvaluateCoreAsync(
        RobustPickEvaluationInput input,
        bool persist,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!_options.Enabled)
        {
            return null;
        }

        ValidateInput(input);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.EvaluationTimeoutSeconds, 1, 600)));
        var token = timeout.Token;
        var timer = Stopwatch.StartNew();

        var family = ParseFamily(input.MarketFamily, input.MarketType);
        var scope = ParseScope(input.MarketType);
        var side = ParseSide(input.SelectedSide);
        var asOfUtc = EnsureUtc(input.EvaluationAsOfUtc);
        var fixtureStartUtc = EnsureUtc(input.FixtureStartUtc);
        var predictionAsOfUtc = EnsureUtc(input.PredictionAsOfUtc);
        var quoteUtc = input.QuoteTimestampUtc.HasValue
            ? EnsureUtc(input.QuoteTimestampUtc.Value)
            : (DateTime?)null;

        var effectivePolicy = await _repository.GetEffectivePolicyAsync(
            new RobustPolicyQuery(
                asOfUtc,
                input.BotKey,
                NormalizeFamilyName(family),
                input.MarketType,
                scope.ToString(),
                side.ToString(),
                input.League,
                input.Line,
                input.SelectedOdds),
            token);
        var mode = input.EvaluationModeOverride
            ?? ParseMode(effectivePolicy?.EvaluationMode ?? _options.Mode);
        if (mode == EvaluationMode.Disabled)
        {
            return null;
        }

        var policyOptions = ResolvePolicyOptions(effectivePolicy, input);
        var direct = input.DirectPrediction ?? input.PrimaryPrediction;
        var home = scope == MarketScope.Total ? input.HomePrediction : null;
        var away = scope == MarketScope.Total ? input.AwayPrediction : null;
        var components = BuildComponents(input, asOfUtc, direct, home, away);
        var reconciled = _reconciliation.Reconcile(
            components,
            input.ComponentValidation,
            new PredictionReconciliationOptions(),
            ReconciliationVersion);
        var reconciledPrediction = reconciled.ReconciledPrediction
            ?? direct;

        IReadOnlyList<PersistenceResidual> residualRows;
        var residualEvidenceAvailable = true;
        using (var residualTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            residualTimeout.CancelAfter(ResidualEvidenceTimeout);
            try
            {
                residualRows = await LoadResidualHistoryCachedAsync(
                    new RobustResidualHistoryQuery(
                        asOfUtc,
                        NormalizeFamilyName(family),
                        input.MarketType,
                        side.ToString(),
                        input.League,
                        _options.OutcomeAvailabilityLagHours,
                        MaximumLiveResidualRows),
                    residualTimeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                residualEvidenceAvailable = false;
                residualRows = [];
                _logger.LogWarning(
                    "Robust residual evidence timed out after {TimeoutSeconds}s. BotKey={BotKey}, Market={Market}, League={League}; evaluation continues fail-closed.",
                    ResidualEvidenceTimeout.TotalSeconds,
                    input.BotKey,
                    input.MarketType,
                    input.League);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                residualEvidenceAvailable = false;
                residualRows = [];
                _logger.LogWarning(
                    exception,
                    "Robust residual evidence was unavailable. BotKey={BotKey}, Market={Market}, League={League}; evaluation continues fail-closed.",
                    input.BotKey,
                    input.MarketType,
                    input.League);
            }
        }
        var missingTrainingMetadata = residualRows.Count(row => !row.ModelTrainedThroughUtc.HasValue);
        var domainResiduals = residualRows
            .Where(row => row.ModelTrainedThroughUtc.HasValue)
            .Select(row => MapResidual(row, family, scope, side))
            .ToArray();
        var distributionResult = _distribution.Build(
            new PredictiveDistributionRequest
            {
                FixtureId = input.FixtureId,
                EvaluationAsOfUtc = asOfUtc,
                MarketFamily = family,
                MarketType = input.MarketType,
                MarketScope = scope,
                Side = side,
                Line = input.Line,
                ReconciledPrediction = reconciledPrediction,
                Odds = input.SelectedOdds,
                League = input.League,
                ModelVersion = input.BaseModelVersion,
                RobustnessVersion = _options.Version
            },
            domainResiduals,
            BuildDistributionOptions(input),
            CanonicalSettlementAdapter.Instance,
            token);

        var errorScale = distributionResult.Distribution?.ErrorScale
            ?? input.ConfiguredModelMae
            ?? _options.Residuals.ErrorScaleEpsilon;
        var consensusResult = _consensus.Evaluate(new PredictionConsensusRequest
        {
            Line = input.Line,
            Side = side,
            DirectPrediction = direct,
            HomePrediction = home,
            AwayPrediction = away,
            ContextPrediction = input.ContextPrediction,
            ReconciledPrediction = reconciledPrediction,
            ErrorScale = errorScale,
            NormalizationEpsilon = _options.Residuals.ErrorScaleEpsilon,
            EvaluationAsOfUtc = asOfUtc
        });

        var noVig = _marketProbability.Calculate(new RobustMarketQuote(
            side,
            input.SelectedOdds,
            input.OverOdds,
            input.UnderOdds,
            input.Line));
        var marketImplied = 1m / input.SelectedOdds;
        var conservativeMarket = Math.Max(
            marketImplied,
            noVig.ConservativeSelectedProbability ?? marketImplied);
        var calibration = _calibration.Evaluate(
            new CalibrationReliabilityInput(
                input.RawProbability,
                input.CalibratedProbability,
                input.ProbabilityLowerBound,
                input.ProbabilityUpperBound,
                input.CalibrationEffectiveN,
                input.CalibrationExactMarketN,
                input.CalibrationFamilyN,
                input.CalibrationGlobalN,
                input.CalibrationFallbackLevel,
                input.CalibrationEvidenceAgeDays,
                input.CalibrationError,
                input.DataQualityScore,
                input.CalibrationVersion ?? "unavailable",
                input.CalibrationPriorWeight,
                input.CalibrationIntervalMethod,
                input.CalibrationConfidenceLevel),
            new CalibrationReliabilityOptions());

        var asianPoint = distributionResult.Distribution is null
            ? null
            : _asianValue.Calculate(
                input.SelectedOdds,
                AsianValueCalculator.FromDistribution(distributionResult.Distribution));
        var pointFairProbability = asianPoint?.ModelFairProbability
            ?? input.CalibratedProbability;
        var scenarioProviderResults = EvaluateScenarioProviders(
            new ScenarioProviderRequest
            {
                EvaluationAsOfUtc = asOfUtc,
                MarketFamily = family,
                MarketType = input.MarketType,
                BasePrediction = reconciledPrediction,
                BaseProbability = pointFairProbability,
                Evidence = input.ScenarioEvidence
            },
            token);
        var scenarioValues = BuildOuterScenarios(
            input,
            reconciledPrediction,
            side,
            distributionResult.Distribution,
            Math.Clamp(_options.OuterScenarioCount, 1, 20_000),
            scenarioProviderResults,
            calibration.LowerBound,
            calibration.UpperBound);
        var robustValue = _robustValue.Evaluate(
            pointFairProbability,
            conservativeMarket,
            input.SelectedOdds,
            scenarioValues);
        if (asianPoint is not null)
        {
            robustValue = robustValue with
            {
                PointExpectedValue = asianPoint.ExpectedValue,
                PointEdge = pointFairProbability - conservativeMarket
            };
        }

        var oddsAgeSeconds = quoteUtc.HasValue
            ? checked((int)Math.Clamp((asOfUtc - quoteUtc.Value).TotalSeconds, int.MinValue, int.MaxValue))
            : (int?)null;
        var maxOddsAge = input.MaxOddsAgeSeconds ?? ResolveMaxOddsAge(input.Bookmaker);
        var quoteIsTemporal = !quoteUtc.HasValue || quoteUtc.Value <= asOfUtc;
        var oddsFresh = quoteUtc.HasValue && oddsAgeSeconds is >= 0 && oddsAgeSeconds <= maxOddsAge;
        var oddsAvailabilityStatus = !quoteUtc.HasValue
            ? OddsAvailabilityStatus.SourceUnavailable
            : !quoteIsTemporal
                ? OddsAvailabilityStatus.SnapshotExpired
                : oddsFresh
                    ? OddsAvailabilityStatus.Available
                    : OddsAvailabilityStatus.Stale;
        var oddsReliability = quoteUtc.HasValue && quoteIsTemporal
            ? Math.Clamp(1m - Math.Max(0, oddsAgeSeconds ?? maxOddsAge) / (decimal)Math.Max(1, maxOddsAge), 0m, 1m)
            : 0m;
        if (noVig.Status == NoVigStatus.Available)
        {
            oddsReliability = Math.Min(1m, oddsReliability + 0.10m);
        }

        var scenarioStability = robustValue.ValidScenarioCount > 0
            ? robustValue.ScenarioSideStability
            : 0m;
        var stake = _stake.Recommend(
            input.OriginalStake,
            new RobustnessComponents(
                NormalizePositive(robustValue.RobustEdge, 0.05m),
                NormalizePositive(robustValue.RobustExpectedValue, 0.10m),
                robustValue.PositiveEvStability,
                calibration.ReliabilityScore,
                scenarioStability,
                consensusResult.MagnitudeAgreementScore,
                consensusResult.CoherenceScore,
                input.DataQualityScore,
                oddsReliability),
            BuildStakeOptions(input.OriginalStake));

        var exposureBucket = HourBucket(asOfUtc);
        IReadOnlyList<OpenPortfolioExposureDto> exposureRows = [];
        var exposureEvidenceAvailable = !_options.Exposure.Enabled;
        if (_options.Exposure.Enabled)
        {
            using var exposureTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            exposureTimeout.CancelAfter(ExposureEvidenceTimeout);
            try
            {
                exposureRows = await LoadExposureCachedAsync(asOfUtc, exposureTimeout.Token);
                exposureEvidenceAvailable = true;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Robust exposure evidence timed out after {TimeoutSeconds}s. BotKey={BotKey}, FixtureId={FixtureId}; evaluation continues fail-closed.",
                    ExposureEvidenceTimeout.TotalSeconds,
                    input.BotKey,
                    input.FixtureId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Robust exposure evidence was unavailable. BotKey={BotKey}, FixtureId={FixtureId}; evaluation continues fail-closed.",
                    input.BotKey,
                    input.FixtureId);
            }
        }
        IReadOnlyList<SessionExposurePick> requestExposure;
        lock (_requestCacheLock)
        {
            requestExposure = _sessionExposure.TryGetValue(exposureBucket, out var values)
                ? values.ToArray()
                : [];
        }
        var exposurePick = BuildPortfolioPick(
            input,
            family,
            stake.RecommendedStake,
            stake.RobustnessScore);
        var allocation = _exposure.Allocate(
            [exposurePick],
            exposureRows
                .Where(row => row.BotPickSelectionId != input.BotPickSelectionId)
                .Select(MapPortfolioPick)
                .Concat(requestExposure
                    .Where(session => session.BotPickSelectionId != input.BotPickSelectionId
                        && exposureRows.All(row => row.BotPickSelectionId != session.BotPickSelectionId))
                    .Select(session => session.Pick))
                .ToArray(),
            BuildExposureOptions())
            .Single();
        if (allocation.ApprovedStake < stake.RecommendedStake)
        {
            stake = stake with
            {
                RecommendedStake = allocation.ApprovedStake,
                StakeMultiplier = input.OriginalStake > 0m
                    ? allocation.ApprovedStake / input.OriginalStake
                    : 0m
            };
        }

        var currentTemporal = predictionAsOfUtc <= asOfUtc
            && predictionAsOfUtc < fixtureStartUtc
            && quoteIsTemporal;
        var modelTemporal = input.ModelTrainedThroughUtc.HasValue
            && EnsureUtc(input.ModelTrainedThroughUtc.Value) < fixtureStartUtc;
        var hasErrorScale = distributionResult.Distribution?.ErrorScale is > 0m
            || input.ConfiguredModelMae is > 0m;
        var exposureReasons = allocation.ReasonCodes;
        var decision = _policy.Evaluate(
            new RobustPickPolicyInput
            {
                Mode = mode,
                CurrentDecision = input.CurrentDecision,
                OriginalStake = input.OriginalStake,
                RiskAdjustedStake = stake.RecommendedStake,
                RobustnessScore = stake.RobustnessScore,
                DataIsValid = input.PrimaryPrediction >= 0m && input.DataQualityScore > 0m,
                TemporalDataIsValid = currentTemporal,
                ModelWasTrainedBeforeFixture = modelTemporal,
                MarketPriceAvailable = input.SelectedOdds > 1m,
                OddsAreFresh = oddsFresh,
                NoVigStatus = noVig.Status,
                ErrorScaleAvailable = hasErrorScale,
                ResidualEffectiveN = distributionResult.EffectiveObservationCount,
                SideAgreement = consensusResult.SideAgreement,
                NormalizedWorstCaseDistance = consensusResult.NormalizedWorstCaseDistance,
                NormalizedConsensusRange = consensusResult.NormalizedConsensusRange,
                NormalizedCoherenceGap = consensusResult.NormalizedCoherenceGap,
                CalibrationReliability = calibration.ReliabilityScore,
                PointEdge = robustValue.PointEdge,
                PointExpectedValue = robustValue.PointExpectedValue,
                RobustEdge = robustValue.RobustEdge,
                RobustExpectedValue = robustValue.RobustExpectedValue,
                PositiveEvStability = robustValue.PositiveEvStability,
                ScenarioSideStability = scenarioStability,
                DataQualityScore = input.DataQualityScore,
                ExposureAvailable = exposureEvidenceAvailable
                    && !exposureReasons.Contains(RobustReasonCode.ExposureLimitExceeded),
                CorrelatedExposureAvailable = exposureEvidenceAvailable
                    && !exposureReasons.Contains(RobustReasonCode.CorrelatedExposureLimitExceeded),
                ScenarioConflictRequiresReview = scenarioStability < policyOptions.MinScenarioSideStability,
                IntelligenceEvidenceStatus = input.IntelligenceEvidenceStatus,
                SnapshotExpired = input.IntelligenceEvidenceStatus == EvidenceStatus.SnapshotExpired,
                MarketAutomationNameMatches = MarketAutomationNameMatches(input.AutomationVersion, family)
            },
            policyOptions);

        var warningReasons = decision.Warnings
            .Concat(distributionResult.Warnings)
            .Concat(exposureReasons.Where(reason => !decision.RejectionReasons.Contains(reason)))
            .Concat(missingTrainingMetadata > 0 || !residualEvidenceAvailable || !exposureEvidenceAvailable
                ? [RobustReasonCode.EvidenceInsufficient]
                : [])
            .Distinct()
            .OrderBy(reason => reason)
            .ToArray();
        var warningCodes = warningReasons.Select(reason => reason.ToStableCode()).ToArray();
        var reasonCodes = decision.RejectionReasons.Select(reason => reason.ToStableCode()).ToArray();
        var policyVersion = effectivePolicy?.PolicyVersion ?? $"{_options.Version}-config";
        var persistenceComponents = BuildPersistenceComponents(
            components,
            reconciled,
            input,
            asOfUtc);
        var snapshot = BuildSnapshot(
            input,
            family,
            scope,
            side,
            asOfUtc,
            fixtureStartUtc,
            quoteUtc,
            oddsAgeSeconds,
            oddsAvailabilityStatus,
            oddsReliability,
            noVig,
            conservativeMarket,
            consensusResult,
            reconciled,
            distributionResult,
            calibration,
            asianPoint,
            robustValue,
            scenarioValues,
            scenarioProviderResults,
            decision,
            stake,
            reasonCodes,
            warningCodes,
            policyVersion,
            persistenceComponents,
            missingTrainingMetadata);
        exposurePick = exposurePick with
        {
            RequestedStake = decision.EffectiveStake,
            RobustnessScore = decision.RobustnessScore
        };

        AppendRobustEvaluationResult? persisted = null;
        if (persist)
        {
            persisted = await _repository.AppendAsync(snapshot, token);
            RegisterSessionExposure(input.BotPickSelectionId, snapshot.AsOfUtc, exposurePick);
        }

        timer.Stop();
        var metricTags = new TagList
        {
            { "bot", input.BotKey },
            { "market_family", NormalizeFamilyName(family) },
            { "market", input.MarketType },
            { "side", input.SelectedSide },
            { "mode", mode.ToString() }
        };
        EvaluationCounter.Add(1, metricTags);
        SimulationDuration.Record(timer.Elapsed.TotalMilliseconds, metricTags);
        ResidualEffectiveN.Record(Convert.ToDouble(distributionResult.EffectiveObservationCount), metricTags);
        switch (decision.RobustDecision)
        {
            case RobustDecision.Approve:
                ApproveCounter.Add(1, metricTags);
                break;
            case RobustDecision.Reject:
                RejectCounter.Add(1, metricTags);
                break;
            case RobustDecision.ReduceStake:
                ReduceStakeCounter.Add(1, metricTags);
                break;
        }
        if (!oddsFresh)
        {
            OddsStaleCounter.Add(1, metricTags);
        }
        if (decision.RejectionReasons.Contains(RobustReasonCode.LookaheadDataDetected)
            || decision.RejectionReasons.Contains(RobustReasonCode.ModelTrainedAfterFixture))
        {
            LeakageRejectedCounter.Add(1, metricTags);
        }
        var currentEquivalent = input.CurrentDecision == CurrentSystemDecision.Bet
            ? RobustDecision.Approve
            : RobustDecision.Reject;
        if (mode == EvaluationMode.Shadow
            && decision.RobustDecision != currentEquivalent)
        {
            ShadowDisagreementCounter.Add(1, metricTags);
        }
        _logger.LogInformation(
            "Robust evaluation {EvaluationVersion}: EvaluationId={EvaluationId} FixtureId={FixtureId} SelectionId={SelectionId} BotKey={BotKey} Market={Market} Side={Side} Mode={Mode} DurationMs={DurationMs} ResidualEffectiveN={ResidualEffectiveN} SimulationCount={SimulationCount} PointEdge={PointEdge} RobustEdge={RobustEdge} PointEV={PointEV} RobustEV={RobustEV} Decision={Decision} ReasonCodes={ReasonCodes}",
            _options.Version,
            persisted?.RobustEvaluationId,
            input.FixtureId,
            input.BotPickSelectionId,
            input.BotKey,
            input.MarketType,
            input.SelectedSide,
            mode,
            timer.ElapsedMilliseconds,
            distributionResult.EffectiveObservationCount,
            distributionResult.Distribution?.SimulationCount ?? 0,
            robustValue.PointEdge,
            robustValue.RobustEdge,
            robustValue.PointExpectedValue,
            robustValue.RobustExpectedValue,
            decision.RobustDecision,
            reasonCodes);

        return new RobustPickEvaluationExecution(
            snapshot,
            decision,
            persisted,
            consensusResult,
            distributionResult,
            calibration,
            robustValue,
            stake,
            exposurePick,
            warningCodes,
            timer.ElapsedMilliseconds);
    }

    public async Task<AppendRobustEvaluationResult> PersistAsync(
        RobustPickEvaluationExecution execution,
        long? botPickSelectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        execution.Snapshot.BotPickSelectionId = botPickSelectionId;
        var result = await _repository.AppendAsync(execution.Snapshot, cancellationToken);
        RegisterSessionExposure(botPickSelectionId, execution.Snapshot.AsOfUtc, execution.ExposurePick);
        return result;
    }

    private void RegisterSessionExposure(
        long? botPickSelectionId,
        DateTime asOfUtc,
        PortfolioPick exposurePick)
    {
        if (botPickSelectionId.HasValue && exposurePick.RequestedStake > 0m)
        {
            var bucket = HourBucket(asOfUtc);
            lock (_requestCacheLock)
            {
                if (!_sessionExposure.TryGetValue(bucket, out var values))
                {
                    values = [];
                    _sessionExposure[bucket] = values;
                }
                values.RemoveAll(value => value.BotPickSelectionId == botPickSelectionId.Value);
                values.Add(new SessionExposurePick(botPickSelectionId.Value, exposurePick));
            }
        }
    }

    private AppendRobustPickEvaluationCommand BuildSnapshot(
        RobustPickEvaluationInput input,
        MarketFamily family,
        MarketScope scope,
        SelectionSide side,
        DateTime asOfUtc,
        DateTime fixtureStartUtc,
        DateTime? quoteUtc,
        int? oddsAgeSeconds,
        OddsAvailabilityStatus oddsAvailabilityStatus,
        decimal oddsReliability,
        RobustMarketProbabilityResult noVig,
        decimal conservativeMarket,
        PredictionConsensusResult consensus,
        PredictionReconciliationResult reconciliation,
        PredictiveDistributionResult distribution,
        CalibrationReliabilityResult calibration,
        AsianValueResult? asianPoint,
        RobustValueEvaluationResult value,
        IReadOnlyList<RobustScenarioValue> scenarios,
        IReadOnlyList<ScenarioProviderResult> scenarioProviderResults,
        RobustPickEvaluationResult decision,
        RiskAdjustedStakeResult stake,
        IReadOnlyList<string> reasonCodes,
        IReadOnlyList<string> warningCodes,
        string policyVersion,
        IReadOnlyList<RobustEvaluationComponentSnapshot> components,
        int residualsWithoutTrainingMetadata)
    {
        var predictive = distribution.Distribution;
        var minutesToKickoff = checked((int)Math.Clamp(
            (fixtureStartUtc - asOfUtc).TotalMinutes,
            int.MinValue,
            int.MaxValue));
        var inputPayload = JsonSerializer.Serialize(new
        {
            input.EvaluationSubjectKey,
            input.BotKey,
            input.MarketFamily,
            input.MarketType,
            input.SelectedSide,
            input.Line,
            input.SelectedOdds,
            input.OverOdds,
            input.UnderOdds,
            input.SourceOddsSnapshotId,
            input.PredictionAsOfUtc,
            input.EvaluationAsOfUtc,
            input.QuoteTimestampUtc,
            input.BaseModelVersion,
            input.ModelTrainedThroughUtc,
            input.SelectorVersion,
            input.CalibrationVersion,
            input.CalibrationPriorWeight,
            input.CalibrationIntervalMethod,
            input.CalibrationConfidenceLevel,
            input.IntelligenceVersion,
            input.IntelligenceEvidenceStatus,
            input.ActionableFactCount,
            input.IndependentSourceCount,
            optionsVersion = _options.Version,
            policyVersion
        }, JsonOptions);
        var evaluationPayload = JsonSerializer.Serialize(new
        {
            marketScope = scope,
            reconciliation,
            consensus,
            distribution = new
            {
                distribution.FallbackLevel,
                distribution.RawObservationCount,
                distribution.EffectiveObservationCount,
                distribution.MinimumRequiredEffectiveN,
                distribution.TargetEffectiveN,
                distribution.ErrorScaleMethod,
                distribution.ResidualSourceScope,
                distribution.DeterministicSeed,
                residualsWithoutTrainingMetadata,
                predictive
            },
            calibration,
            value,
            scenarios,
            stake,
            decision,
            scenarioProviders = scenarioProviderResults,
            preMatch = new
            {
                input.LineupStatus,
                input.IntelligenceEvidenceStatus,
                oddsAvailabilityStatus,
                input.IntelligenceSnapshotAgeMinutes,
                input.ActionableFactCount,
                input.IndependentSourceCount,
                input.FatigueDataStatus,
                input.GameStateModelStatus
            }
        }, JsonOptions);

        return new AppendRobustPickEvaluationCommand
        {
            SourceEvaluationId = input.SourceEvaluationId,
            BotPickSelectionId = input.BotPickSelectionId,
            SourceOddsSnapshotId = input.SourceOddsSnapshotId,
            FixtureId = input.ExternalFixtureId is > 0 ? input.ExternalFixtureId : null,
            EvaluationSubjectKey = input.EvaluationSubjectKey,
            BotKey = input.BotKey,
            MarketFamily = NormalizeFamilyName(family),
            MarketType = input.MarketType,
            Side = side.ToString(),
            Line = input.Line,
            Odds = input.SelectedOdds,
            Bookmaker = input.Bookmaker,
            EvaluationVersion = _options.Version,
            AsOfUtc = asOfUtc,
            BaseModelVersion = input.BaseModelVersion,
            ModelTrainedThroughUtc = input.ModelTrainedThroughUtc,
            SelectorVersion = input.SelectorVersion,
            CalibrationVersion = input.CalibrationVersion,
            IntelligenceVersion = input.IntelligenceVersion,
            SettlementVersion = input.SettlementVersion,
            RobustnessVersion = _options.Version,
            PolicyVersion = policyVersion,
            DirectPrediction = consensus.DirectPrediction,
            HomePrediction = input.HomePrediction,
            AwayPrediction = input.AwayPrediction,
            ComponentsPrediction = consensus.ComponentsPrediction,
            ContextPrediction = consensus.ContextPrediction,
            ReconciledPrediction = consensus.ReconciledPrediction,
            ConsensusMinimum = consensus.ConsensusMinimum,
            ConsensusMaximum = consensus.ConsensusMaximum,
            ConsensusRange = consensus.ConsensusRange,
            CoherenceGap = consensus.CoherenceGap,
            DirectDistance = consensus.DirectDistance,
            ComponentsDistance = consensus.ComponentsDistance,
            ContextDistance = consensus.ContextDistance,
            ReconciledDistance = consensus.ReconciledDistance,
            WorstCasePrediction = consensus.WorstCasePrediction,
            WorstCaseDistance = consensus.WorstCaseDistance,
            ErrorScale = predictive?.ErrorScale ?? input.ConfiguredModelMae,
            NormalizedDirectDistance = consensus.DirectDistance.HasValue
                ? consensus.DirectDistance.Value / Math.Max(predictive?.ErrorScale ?? input.ConfiguredModelMae ?? _options.Residuals.ErrorScaleEpsilon, _options.Residuals.ErrorScaleEpsilon)
                : null,
            NormalizedWorstCaseDistance = consensus.NormalizedWorstCaseDistance,
            NormalizedConsensusRange = consensus.NormalizedConsensusRange,
            NormalizedCoherenceGap = consensus.NormalizedCoherenceGap,
            SideAgreement = consensus.SideAgreement,
            MagnitudeAgreementScore = consensus.MagnitudeAgreementScore,
            ProbabilityAgreementScore = consensus.ProbabilityAgreementScore,
            CoherenceScore = consensus.CoherenceScore,
            ScenarioSideStability = value.ScenarioSideStability,
            PositiveEvStability = value.PositiveEvStability,
            P01 = predictive?.P01,
            P05 = predictive?.P05,
            P10 = predictive?.P10,
            P25 = predictive?.P25,
            P50 = predictive?.P50,
            P75 = predictive?.P75,
            P90 = predictive?.P90,
            P95 = predictive?.P95,
            P99 = predictive?.P99,
            DistributionMean = predictive?.Mean,
            StandardDeviation = predictive?.StandardDeviation,
            MedianAbsoluteDeviation = predictive?.MedianAbsoluteDeviation,
            DistributionEffectiveN = distribution.EffectiveObservationCount,
            ResidualRawObservationCount = distribution.RawObservationCount,
            SimulationCount = predictive?.SimulationCount,
            DistributionMethod = predictive?.DistributionMethod,
            DistributionVersion = predictive?.DistributionVersion,
            HistogramJson = JsonSerializer.Serialize(predictive?.Histogram ?? new Dictionary<int, int>(), JsonOptions),
            PWin = predictive?.PWin,
            PHalfWin = predictive?.PHalfWin,
            PPush = predictive?.PPush,
            PHalfLoss = predictive?.PHalfLoss,
            PLoss = predictive?.PLoss,
            RawProbability = input.RawProbability,
            CalibratedProbability = input.CalibratedProbability,
            ProbabilityLowerBound = calibration.LowerBound,
            ProbabilityUpperBound = calibration.UpperBound,
            ModelFairOdds = asianPoint?.FairOdds,
            ModelFairProbability = asianPoint?.ModelFairProbability ?? input.CalibratedProbability,
            RobustModelFairProbability = value.RobustModelFairProbability,
            MarketImpliedProbability = 1m / input.SelectedOdds,
            MarketNoVigProbability = noVig.ConservativeSelectedProbability,
            ConservativeMarketProbability = conservativeMarket,
            PointEdge = value.PointEdge,
            RobustEdge = value.RobustEdge,
            PointExpectedValue = value.PointExpectedValue,
            RobustExpectedValue = value.RobustExpectedValue,
            ExpectedValueP10 = value.ExpectedValueP10,
            ExpectedValueP50 = value.ExpectedValueP50,
            ExpectedValueP90 = value.ExpectedValueP90,
            EdgeP10 = value.EdgeP10,
            EdgeP50 = value.EdgeP50,
            EdgeP90 = value.EdgeP90,
            CalibrationEffectiveN = calibration.EffectiveN,
            CalibrationExactMarketN = calibration.ExactMarketN,
            CalibrationFamilyN = calibration.FamilyN,
            CalibrationGlobalN = calibration.GlobalN,
            CalibrationReliability = calibration.ReliabilityScore,
            CalibrationSpecificityScore = calibration.SpecificityScore,
            CalibrationRecencyScore = calibration.RecencyScore,
            CalibrationErrorScore = calibration.CalibrationErrorScore,
            CalibrationFallbackLevel = calibration.FallbackLevel.ToString(),
            OddsEvaluated = input.SelectedOdds,
            OddsTaken = input.SelectedOdds,
            QuoteTimestampUtc = quoteUtc,
            OddsAgeSeconds = oddsAgeSeconds,
            MinutesToKickoff = minutesToKickoff,
            NoVigMethod = noVig.Method,
            OddsReliability = oddsReliability,
            LineupStatus = input.LineupStatus,
            IntelligenceEvidenceStatus = input.IntelligenceEvidenceStatus.ToString(),
            FatigueDataStatus = input.FatigueDataStatus,
            GameStateModelStatus = input.GameStateModelStatus,
            ScenarioCount = scenarios.Count,
            AdverseScenarioProbability = scenarios.Count == 0
                ? null
                : 1m - value.ScenarioSideStability,
            ScenarioStability = value.ScenarioSideStability,
            EvaluationMode = decision.Mode.ToString(),
            CurrentSystemDecision = decision.CurrentSystemDecision.ToString(),
            RobustDecision = decision.RobustDecision.ToString(),
            OriginalStake = input.OriginalStake,
            RecommendedStake = decision.RecommendedStake,
            StakeMultiplier = decision.StakeMultiplier,
            RobustnessScore = decision.RobustnessScore,
            RejectionReasonCodesJson = JsonSerializer.Serialize(reasonCodes, JsonOptions),
            WarningCodesJson = JsonSerializer.Serialize(warningCodes, JsonOptions),
            HumanReadableReason = decision.HumanReadableReason,
            InputPayloadJson = inputPayload,
            EvaluationPayloadJson = evaluationPayload,
            Components = components
        };
    }

    private IReadOnlyList<RobustScenarioValue> BuildOuterScenarios(
        RobustPickEvaluationInput input,
        decimal basePrediction,
        SelectionSide selectedSide,
        PredictiveDistribution? distribution,
        int count,
        IReadOnlyList<ScenarioProviderResult> providerResults,
        decimal? calibrationLowerBound,
        decimal? calibrationUpperBound)
    {
        var lower = Math.Clamp(calibrationLowerBound ?? input.CalibratedProbability, 0m, 1m);
        var upper = Math.Clamp(calibrationUpperBound ?? input.CalibratedProbability, 0m, 1m);
        if (lower > upper)
        {
            (lower, upper) = (upper, lower);
        }

        var calibrationEvidenceAvailable = input.CalibrationFallbackLevel != CalibrationFallbackLevel.Unavailable
            || input.CalibrationEffectiveN is > 0m;
        var scenarios = new List<RobustScenarioValue>(count + providerResults.Count);
        if (calibrationEvidenceAvailable)
        {
            for (var index = 0; index < count; index++)
            {
                // Deterministic midpoint grid: adding scenarios refines the same bounded uncertainty,
                // it never pulls entropy from process-randomized GetHashCode or wall-clock state.
                var position = (index + 0.5m) / count;
                var selectedProbability = lower + (upper - lower) * position;
                var (fairProbability, expectedValue) = ScenarioValue(
                    input.SelectedOdds,
                    selectedProbability,
                    distribution);

                scenarios.Add(new RobustScenarioValue(
                    $"calibration-{index + 1:D4}",
                    fairProbability,
                    expectedValue,
                    RetainsSelectedSide(basePrediction, input.Line, selectedSide),
                    1m / count,
                    EvidenceStatus.ReviewedNeutral,
                    true));
            }
        }

        foreach (var provider in providerResults.Where(result => result.IsUsable))
        {
            var providerBaseProbability = provider.ScenarioType == ScenarioType.Intelligence
                ? input.ProbabilityBeforeIntelligence ?? input.CalibratedProbability
                : input.CalibratedProbability;
            var selectedProbability = Math.Clamp(
                providerBaseProbability + provider.ProbabilityAdjustment,
                0m,
                1m);
            var scenarioPrediction = basePrediction + provider.PredictionAdjustment;
            var (fairProbability, expectedValue) = ScenarioValue(
                input.SelectedOdds,
                selectedProbability,
                distribution);
            scenarios.Add(new RobustScenarioValue(
                $"provider-{provider.ScenarioType}-{provider.ScenarioName}",
                fairProbability,
                expectedValue,
                RetainsSelectedSide(scenarioPrediction, input.Line, selectedSide),
                provider.ProbabilityWeight * provider.Confidence,
                provider.EvidenceStatus,
                true));
        }
        return scenarios;
    }

    private static bool RetainsSelectedSide(
        decimal prediction,
        decimal line,
        SelectionSide selectedSide) => selectedSide switch
    {
        SelectionSide.Over => prediction > line,
        SelectionSide.Under => prediction < line,
        _ => false
    };

    private IReadOnlyList<ScenarioProviderResult> EvaluateScenarioProviders(
        ScenarioProviderRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<ScenarioProviderResult>(_scenarioProviders.Count);
        foreach (var provider in _scenarioProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(provider.Evaluate(request));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Optional robust scenario provider failed. ScenarioType={ScenarioType} Market={Market}; continuing as SourceUnavailable",
                    provider.ScenarioType,
                    request.MarketType);
                results.Add(new ScenarioProviderResult(
                    provider.ScenarioType,
                    provider.ScenarioType.ToString(),
                    0m,
                    0m,
                    0m,
                    EvidenceStatus.SourceUnavailable,
                    0m,
                    [],
                    null,
                    null,
                    false,
                    "SCENARIO_PROVIDER_SOURCE_UNAVAILABLE"));
            }
        }
        return results;
    }

    private (decimal FairProbability, decimal ExpectedValue) ScenarioValue(
        decimal odds,
        decimal selectedProbability,
        PredictiveDistribution? distribution)
    {
        if (distribution is not null)
        {
            var reanchored = ReanchorDistribution(distribution, selectedProbability);
            var asian = _asianValue.Calculate(odds, reanchored);
            return (asian.ModelFairProbability ?? selectedProbability, asian.ExpectedValue);
        }

        return (
            selectedProbability,
            selectedProbability * (odds - 1m) - (1m - selectedProbability));
    }

    private static AsianSettlementProbabilities ReanchorDistribution(
        PredictiveDistribution distribution,
        decimal positiveReturnProbability)
    {
        var positiveMass = distribution.PWin + distribution.PHalfWin;
        var nonPositiveMass = distribution.PPush + distribution.PHalfLoss + distribution.PLoss;
        var winRatio = positiveMass > 0m ? distribution.PWin / positiveMass : 1m;
        var halfWinRatio = positiveMass > 0m ? distribution.PHalfWin / positiveMass : 0m;
        var remaining = 1m - positiveReturnProbability;
        var pushRatio = nonPositiveMass > 0m ? distribution.PPush / nonPositiveMass : 0m;
        var halfLossRatio = nonPositiveMass > 0m ? distribution.PHalfLoss / nonPositiveMass : 0m;
        var lossRatio = nonPositiveMass > 0m ? distribution.PLoss / nonPositiveMass : 1m;
        return new AsianSettlementProbabilities(
            positiveReturnProbability * winRatio,
            positiveReturnProbability * halfWinRatio,
            remaining * pushRatio,
            remaining * halfLossRatio,
            remaining * lossRatio);
    }

    private static IReadOnlyList<PredictionComponent> BuildComponents(
        RobustPickEvaluationInput input,
        DateTime asOfUtc,
        decimal direct,
        decimal? home,
        decimal? away)
    {
        var values = new List<PredictionComponent>
        {
            Component(PredictionComponentType.Direct, direct, input.CalibratedProbability,
                input.BaseModelVersion, asOfUtc, input.DataQualityScore)
        };
        if (home.HasValue && away.HasValue)
        {
            values.Add(Component(PredictionComponentType.HomeAwaySum, home.Value + away.Value,
                null, input.BaseModelVersion, asOfUtc, input.DataQualityScore));
        }
        if (input.ContextPrediction.HasValue && input.ContextPrediction >= 0m)
        {
            values.Add(Component(PredictionComponentType.Context, input.ContextPrediction.Value,
                null, "match-history-context", asOfUtc, input.DataQualityScore));
        }
        return values;
    }

    private static PredictionComponent Component(
        PredictionComponentType type,
        decimal value,
        decimal? probability,
        string sourceVersion,
        DateTime asOfUtc,
        decimal quality) => new(
            type,
            value,
            probability,
            1m,
            true,
            sourceVersion,
            asOfUtc,
            null,
            Math.Clamp(quality, 0m, 1m));

    private static IReadOnlyList<RobustEvaluationComponentSnapshot> BuildPersistenceComponents(
        IReadOnlyList<PredictionComponent> components,
        PredictionReconciliationResult reconciliation,
        RobustPickEvaluationInput input,
        DateTime asOfUtc)
    {
        var rows = new List<PredictionComponent>(components);
        if (reconciliation.ReconciledPrediction.HasValue)
        {
            rows.Add(Component(
                PredictionComponentType.Reconciled,
                reconciliation.ReconciledPrediction.Value,
                null,
                reconciliation.ReconciliationVersion,
                asOfUtc,
                input.DataQualityScore));
        }
        return rows.Select((component, index) => new RobustEvaluationComponentSnapshot
        {
            ComponentSequence = index + 1,
            ComponentType = component.ComponentType.ToString(),
            PredictedValue = component.PredictedValue,
            ProbabilityForSelection = component.ProbabilityForSelection,
            Weight = reconciliation.Weights.TryGetValue(component.ComponentType, out var weight)
                ? weight
                : component.ComponentType == PredictionComponentType.Reconciled ? 1m : 0m,
            IsUsable = component.IsUsable,
            SourceVersion = component.SourceVersion,
            AsOfUtc = component.AsOfUtc,
            ExclusionReason = component.ExclusionReason,
            DataQualityScore = component.DataQualityScore,
            MetadataJson = "{}"
        }).ToArray();
    }

    private EmpiricalResidualBootstrapOptions BuildDistributionOptions(RobustPickEvaluationInput input) => new()
    {
        OutcomeAvailabilityLag = TimeSpan.FromHours(_options.OutcomeAvailabilityLagHours),
        SimulationCount = Math.Clamp(_options.SimulationCount, 100, 100_000),
        ProbabilityLowerQuantile = _options.ProbabilityLowerQuantile,
        ProbabilityUpperQuantile = _options.ProbabilityUpperQuantile,
        MinimumEffectiveN = _options.Residuals.MinimumEffectiveN,
        TargetEffectiveN = _options.Residuals.TargetEffectiveN,
        RecencyHalfLifeDays = _options.Residuals.RecencyHalfLifeDays,
        UseLineSimilarity = _options.Residuals.UseLineSimilarity,
        UseOddsSimilarity = _options.Residuals.UseOddsSimilarity,
        Epsilon = _options.Residuals.ErrorScaleEpsilon,
        ConfiguredModelMae = input.ConfiguredModelMae
    };

    private RiskAdjustedStakeOptions BuildStakeOptions(decimal originalStake) => new()
    {
        AllowIncrease = false,
        HighRobustnessThreshold = _options.Stake.HighRobustnessThreshold,
        MediumRobustnessThreshold = _options.Stake.MediumRobustnessThreshold,
        MinimumRobustnessThreshold = _options.Stake.MinimumRobustnessThreshold,
        MaximumStake = originalStake
    };

    private PortfolioExposureOptions BuildExposureOptions() => new()
    {
        MaximumStakePerFixture = _options.Exposure.MaximumStakePerFixture,
        MaximumStakePerTeam = _options.Exposure.MaximumStakePerTeam,
        MaximumStakePerCorrelationCluster = _options.Exposure.MaximumStakePerCorrelationCluster,
        MaximumRelatedPicksPerFixture = _options.Exposure.MaximumRelatedPicksPerFixture
    };

    private RobustPickPolicyOptions ResolvePolicyOptions(
        RobustPolicySnapshot? snapshot,
        RobustPickEvaluationInput input)
    {
        var fallback = new RobustPickPolicyOptions
        {
            MinPointEdge = input.CurrentMinimumPointEdge,
            MinPointExpectedValue = input.CurrentMinimumPointExpectedValue,
            MinRobustEdge = _options.Policy.MinRobustEdge,
            MinRobustExpectedValue = _options.Policy.MinRobustExpectedValue,
            MinPositiveEvStability = _options.Policy.MinPositiveEvStability,
            MinScenarioSideStability = _options.Policy.MinScenarioSideStability,
            MinNormalizedWorstCaseDistance = _options.Policy.MinNormalizedWorstCaseDistance,
            MaxNormalizedConsensusRange = _options.Policy.MaxNormalizedConsensusRange,
            MaxNormalizedCoherenceGap = _options.Policy.MaxNormalizedCoherenceGap,
            MinCalibrationReliability = _options.Policy.MinCalibrationReliability,
            MinResidualEffectiveN = _options.Residuals.MinimumEffectiveN,
            RequireSideAgreement = _options.Policy.RequireSideAgreement
        };
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.ConfigurationJson))
        {
            return fallback;
        }
        try
        {
            var configured = JsonSerializer.Deserialize<RobustPickPolicyOptions>(snapshot.ConfigurationJson, JsonOptions);
            if (configured is null)
            {
                return fallback;
            }
            using var document = JsonDocument.Parse(snapshot.ConfigurationJson);
            var hasPointEdge = document.RootElement.EnumerateObject().Any(property =>
                property.Name.Equals(nameof(RobustPickPolicyOptions.MinPointEdge), StringComparison.OrdinalIgnoreCase));
            var hasPointEv = document.RootElement.EnumerateObject().Any(property =>
                property.Name.Equals(nameof(RobustPickPolicyOptions.MinPointExpectedValue), StringComparison.OrdinalIgnoreCase));
            return new RobustPickPolicyOptions
            {
                MinPointEdge = hasPointEdge ? configured.MinPointEdge : fallback.MinPointEdge,
                MinPointExpectedValue = hasPointEv
                    ? configured.MinPointExpectedValue
                    : fallback.MinPointExpectedValue,
                MinRobustEdge = configured.MinRobustEdge,
                MinRobustExpectedValue = configured.MinRobustExpectedValue,
                MinPositiveEvStability = configured.MinPositiveEvStability,
                MinScenarioSideStability = configured.MinScenarioSideStability,
                MinNormalizedWorstCaseDistance = configured.MinNormalizedWorstCaseDistance,
                MaxNormalizedConsensusRange = configured.MaxNormalizedConsensusRange,
                MaxNormalizedCoherenceGap = configured.MaxNormalizedCoherenceGap,
                MinCalibrationReliability = configured.MinCalibrationReliability,
                MinResidualEffectiveN = configured.MinResidualEffectiveN,
                MinDataQuality = configured.MinDataQuality,
                RequireSideAgreement = configured.RequireSideAgreement,
                RequireNoVig = configured.RequireNoVig,
                RequireIntelligence = configured.RequireIntelligence,
                RequireCoherence = configured.RequireCoherence,
                ManualReviewOnScenarioConflict = configured.ManualReviewOnScenarioConflict
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception,
                "Invalid robust policy JSON for PolicyVersion={PolicyVersion}; using validated application defaults",
                snapshot.PolicyVersion);
            return fallback;
        }
    }

    private async Task<IReadOnlyList<PersistenceResidual>> LoadResidualHistoryCachedAsync(
        RobustResidualHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var bucket = HourBucket(query.EvaluationAsOfUtc);
        var key = new ResidualCacheKey(
            bucket,
            query.MarketFamily.Trim().ToUpperInvariant(),
            query.MarketType?.Trim().ToUpperInvariant(),
            query.Side?.Trim().ToUpperInvariant(),
            query.League?.Trim().ToUpperInvariant(),
            query.OutcomeAvailabilityLagHours,
            query.MaximumRows);
        Task<IReadOnlyList<PersistenceResidual>> task;
        lock (_requestCacheLock)
        {
            if (!_residualCache.TryGetValue(key, out task!))
            {
                task = _repository.LoadResidualHistoryAsync(
                    query with { EvaluationAsOfUtc = bucket },
                    cancellationToken);
                _residualCache[key] = task;
            }
        }
        try
        {
            return await task;
        }
        catch
        {
            lock (_requestCacheLock)
            {
                if (_residualCache.TryGetValue(key, out var current) && ReferenceEquals(current, task))
                    _residualCache.Remove(key);
            }
            throw;
        }
    }

    private async Task<IReadOnlyList<OpenPortfolioExposureDto>> LoadExposureCachedAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var bucket = HourBucket(asOfUtc);
        Task<IReadOnlyList<OpenPortfolioExposureDto>> task;
        lock (_requestCacheLock)
        {
            if (!_exposureCache.TryGetValue(bucket, out task!))
            {
                task = _repository.LoadOpenExposureAsync(EnsureUtc(asOfUtc), cancellationToken);
                _exposureCache[bucket] = task;
            }
        }
        try
        {
            return await task;
        }
        catch
        {
            lock (_requestCacheLock)
            {
                if (_exposureCache.TryGetValue(bucket, out var current) && ReferenceEquals(current, task))
                    _exposureCache.Remove(bucket);
            }
            throw;
        }
    }

    private static DateTime HourBucket(DateTime value)
    {
        var utc = EnsureUtc(value);
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
    }

    private PortfolioPick BuildPortfolioPick(
        RobustPickEvaluationInput input,
        MarketFamily family,
        decimal requestedStake,
        decimal robustnessScore)
    {
        var direction = ParseSide(input.SelectedSide) switch
        {
            SelectionSide.Over => "HIGH_EVENT",
            SelectionSide.Under => "LOW_EVENT",
            _ => "NEUTRAL"
        };
        return new(
            input.EvaluationSubjectKey,
            input.FixtureId,
            NormalizeKey(input.HomeTeam),
            NormalizeKey(input.AwayTeam),
            NormalizeKey(input.League),
            family,
            input.BotKey,
            DateOnly.FromDateTime(input.FixtureStartUtc),
            $"{input.FixtureId}|{direction}",
            requestedStake,
            Math.Clamp(robustnessScore, 0m, 1m));
    }

    private static PortfolioPick MapPortfolioPick(OpenPortfolioExposureDto row)
    {
        var family = ParseFamily(row.MarketFamily, row.MarketType);
        return new PortfolioPick(
            row.BotPickSelectionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.FixtureId ?? row.BotPickSelectionId,
            NormalizeKey(row.HomeTeam),
            NormalizeKey(row.AwayTeam),
            NormalizeKey(row.League),
            family,
            row.BotKey,
            DateOnly.FromDateTime(row.MatchDate),
            string.IsNullOrWhiteSpace(row.CorrelationCluster)
                ? $"{row.FixtureId ?? row.BotPickSelectionId}|{(
                    row.Side.Equals("Over", StringComparison.OrdinalIgnoreCase)
                        ? "HIGH_EVENT"
                        : row.Side.Equals("Under", StringComparison.OrdinalIgnoreCase)
                            ? "LOW_EVENT"
                            : "NEUTRAL")}"
                : row.CorrelationCluster,
            row.Stake,
            row.RobustnessScore ?? 0m);
    }

    private static HistoricalResidualObservation MapResidual(
        PersistenceResidual row,
        MarketFamily requestedFamily,
        MarketScope requestedScope,
        SelectionSide requestedSide)
    {
        var family = ParseFamily(row.MarketFamily, row.MarketType);
        return new HistoricalResidualObservation(
            row.FixtureId ?? row.SourceEvaluationId,
            EnsureUtc(row.FixtureStartUtc),
            EnsureUtc(row.FixtureEndUtc),
            EnsureUtc(row.PredictionAsOfUtc),
            EnsureUtc(row.ModelTrainedThroughUtc!.Value),
            family,
            row.MarketType,
            ParseScope(row.MarketType),
            ParseSide(row.Side),
            row.League,
            row.Line,
            row.Odds,
            row.Prediction,
            row.ActualResult,
            Math.Clamp(row.DataQualityScore, 0m, 1m),
            row.ModelVersion,
            row.ResidualSource.Equals("AllCandidates", StringComparison.OrdinalIgnoreCase)
                ? ResidualSourceScope.AllCandidates
                : ResidualSourceScope.SelectedPicksOnly,
            row.OutcomeAvailableUtc.HasValue ? EnsureUtc(row.OutcomeAvailableUtc.Value) : null);
    }

    private int ResolveMaxOddsAge(string bookmaker)
    {
        if (_options.MaxOddsAgeSecondsBySource.TryGetValue(bookmaker, out var sourceAge))
        {
            return Math.Max(1, sourceAge);
        }
        return Math.Max(1, _options.DefaultMaxOddsAgeSeconds);
    }

    private static decimal NormalizePositive(decimal? value, decimal target)
    {
        if (!value.HasValue || target <= 0m)
        {
            return 0m;
        }
        return Math.Clamp(value.Value / target, 0m, 1m);
    }

    private static bool MarketAutomationNameMatches(string automationVersion, MarketFamily family)
    {
        var value = automationVersion.ToUpperInvariant();
        if (value.Contains("CORNER", StringComparison.Ordinal) && family != MarketFamily.Corners)
            return false;
        if (value.Contains("GOAL", StringComparison.Ordinal) && family != MarketFamily.Goals)
            return false;
        if (value.Contains("SHOT", StringComparison.Ordinal)
            && family is not MarketFamily.Shots and not MarketFamily.ShotsOnGoal)
            return false;
        return true;
    }

    private static string NormalizeFamilyName(MarketFamily family) => family switch
    {
        MarketFamily.Corners => "CORNERS",
        MarketFamily.Goals => "GOALS",
        MarketFamily.Shots => "SHOTS",
        MarketFamily.ShotsOnGoal => "SOG",
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    private static MarketFamily ParseFamily(string family, string marketType)
    {
        var value = string.IsNullOrWhiteSpace(family) ? marketType : family;
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        if (normalized.Contains("SHOTSONGOAL", StringComparison.Ordinal)
            || normalized.Contains("SHOTSONTARGET", StringComparison.Ordinal)
            || normalized is "SOG")
            return MarketFamily.ShotsOnGoal;
        if (normalized.Contains("SHOT", StringComparison.Ordinal))
            return MarketFamily.Shots;
        if (normalized.Contains("GOAL", StringComparison.Ordinal))
            return MarketFamily.Goals;
        if (normalized.Contains("CORNER", StringComparison.Ordinal))
            return MarketFamily.Corners;
        throw new ArgumentException($"Unsupported robust market family '{family}' for market '{marketType}'.");
    }

    private static MarketScope ParseScope(string marketType)
    {
        if (marketType.Contains("Home", StringComparison.OrdinalIgnoreCase))
            return MarketScope.Home;
        if (marketType.Contains("Away", StringComparison.OrdinalIgnoreCase))
            return MarketScope.Away;
        return MarketScope.Total;
    }

    private static SelectionSide ParseSide(string value) =>
        value.Equals("Over", StringComparison.OrdinalIgnoreCase)
            ? SelectionSide.Over
            : value.Equals("Under", StringComparison.OrdinalIgnoreCase)
                ? SelectionSide.Under
                : throw new ArgumentException("SelectedSide must be Over or Under.");

    private static EvaluationMode ParseMode(string value) =>
        Enum.TryParse<EvaluationMode>(value, true, out var mode)
            ? mode
            : throw new InvalidOperationException($"Unsupported RobustPickEvaluation mode '{value}'.");

    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void ValidateInput(RobustPickEvaluationInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EvaluationSubjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.BotKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.MarketType);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SelectedSide);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.BaseModelVersion);
        if (input.FixtureId <= 0
            || input.FixtureStartUtc == default
            || input.PredictionAsOfUtc == default
            || input.EvaluationAsOfUtc == default
            || input.Line < 0m
            || input.SelectedOdds <= 1m
            || input.OriginalStake < 0m
            || input.PrimaryPrediction < 0m
            || input.RawProbability is < 0m or > 1m
            || input.CalibratedProbability is < 0m or > 1m
            || input.DataQualityScore is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Robust evaluation input is outside its valid range.");
        }
    }

    private sealed record ResidualCacheKey(
        DateTime AsOfHourUtc,
        string MarketFamily,
        string? MarketType,
        string? Side,
        string? League,
        int OutcomeAvailabilityLagHours,
        int MaximumRows);

    private sealed record SessionExposurePick(long BotPickSelectionId, PortfolioPick Pick);
}

public sealed class CanonicalSettlementAdapter : ISettlementAdapter
{
    public const string Version = "automated-bot-pick-settlement-v1";
    public static readonly CanonicalSettlementAdapter Instance = new();

    private CanonicalSettlementAdapter()
    {
    }

    string ISettlementAdapter.SettlementVersion => Version;

    public SettlementOutcome Settle(decimal line, SelectionSide side, int actualResult)
    {
        var result = AutomatedBotPickSettlementCalculator.Calculate(
            side.ToString(),
            line,
            actualResult,
            odds: 2m,
            stake: 1m);
        return result.Factor switch
        {
            1m => SettlementOutcome.Win,
            0.5m => SettlementOutcome.HalfWin,
            0m => SettlementOutcome.Push,
            -0.5m => SettlementOutcome.HalfLoss,
            -1m => SettlementOutcome.Loss,
            _ => throw new InvalidOperationException($"Unsupported settlement factor {result.Factor}.")
        };
    }
}
