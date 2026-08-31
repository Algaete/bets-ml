using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornersPrediction.Application.Automation;
using CornersPrediction.Application.Automation.BotG;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Domain.Automation.BotG;
using CornersPrediction.Domain.FootballIntelligence;
using CornersPredictionApi.NewGenerationMl;

namespace AutomatedCornersBot.Api;

public sealed record BotGFixtureRunResult(
    IReadOnlyList<BotGCandidate> Candidates,
    BotGCandidate? SelectedForPublication,
    int PersistedCandidates)
{
    public int Approved => Candidates.Count(candidate => candidate.Decision == BotGDecisionStatus.Approved);
    public int Rejected => Candidates.Count(candidate => candidate.Decision == BotGDecisionStatus.Rejected);
    public int Abstained => Candidates.Count(candidate => candidate.Decision == BotGDecisionStatus.Abstain);
}

/// <summary>
/// Isolated G2026 runtime. It never calls the C-F decision engine and never publishes a pick;
/// publication is a separate, double-gated action owned by the outer runner.
/// </summary>
public sealed class BotGAutomationService
{
    private static readonly DateTime LegacyGoalsCutoffUtc =
        new(2026, 6, 11, 16, 36, 16, DateTimeKind.Utc);

    private readonly PredictionApiClient _predictionApiClient;
    private readonly NewGenerationPredictionService _newGenerationPredictionService;
    private readonly FeatureBuilder _legacyFeatureBuilder;
    private readonly IBotGFeatureBuilder _featureBuilder;
    private readonly IMarketProbabilityService _marketProbability;
    private readonly IBotGMetaModelService _metaModel;
    private readonly IBotGArtifactEvidenceProvider _artifactEvidence;
    private readonly IBotGCalibrationService _calibration;
    private readonly IBotGUncertaintyService _uncertainty;
    private readonly IBotGOodService _ood;
    private readonly IBotGExpectedValueService _expectedValue;
    private readonly IBotGAbstentionService _abstention;
    private readonly IBotGSelector _selector;
    private readonly IBotGCandidateRepository _candidateRepository;
    private readonly ILogger<BotGAutomationService> _logger;

    public BotGAutomationService(
        PredictionApiClient predictionApiClient,
        NewGenerationPredictionService newGenerationPredictionService,
        FeatureBuilder legacyFeatureBuilder,
        IBotGFeatureBuilder featureBuilder,
        IMarketProbabilityService marketProbability,
        IBotGMetaModelService metaModel,
        IBotGArtifactEvidenceProvider artifactEvidence,
        IBotGCalibrationService calibration,
        IBotGUncertaintyService uncertainty,
        IBotGOodService ood,
        IBotGExpectedValueService expectedValue,
        IBotGAbstentionService abstention,
        IBotGSelector selector,
        IBotGCandidateRepository candidateRepository,
        ILogger<BotGAutomationService> logger)
    {
        _predictionApiClient = predictionApiClient;
        _newGenerationPredictionService = newGenerationPredictionService;
        _legacyFeatureBuilder = legacyFeatureBuilder;
        _featureBuilder = featureBuilder;
        _marketProbability = marketProbability;
        _metaModel = metaModel;
        _artifactEvidence = artifactEvidence;
        _calibration = calibration;
        _uncertainty = uncertainty;
        _ood = ood;
        _expectedValue = expectedValue;
        _abstention = abstention;
        _selector = selector;
        _candidateRepository = candidateRepository;
        _logger = logger;
    }

    public async Task<BotGFixtureRunResult> EvaluateFixtureAsync(
        Guid runId,
        RecommendationBotDefinitionDto definition,
        IReadOnlyList<UpcomingOddsRecord> fixtureOdds,
        PredictionContextDto? context,
        PredictionContextDto? swappedContext,
        IReadOnlyList<TeamBi3InfoDto> teamInfo,
        bool neutralMatch,
        bool historicalMode,
        bool dryRun,
        MatchIntelligenceSnapshotPair? footballIntelligenceSnapshot,
        CancellationToken cancellationToken)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Bot G requires a non-empty audit RunId.", nameof(runId));
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fixtureOdds);
        ArgumentNullException.ThrowIfNull(teamInfo);
        if (!definition.UsesBotG)
            throw new ArgumentException("Bot G runtime requires GOALS_MARKET_ANCHORED.", nameof(definition));
        var configuration = BotGConfiguration.Validate(
            definition.GoalsMarketAnchoredConfiguration
            ?? throw new InvalidOperationException("Bot G configuration is missing."));
        var footballIntelligenceConfiguration = definition.FootballIntelligenceConfiguration;
        if (!configuration.Enabled)
            return new BotGFixtureRunResult([], null, 0);
        var goalRows = fixtureOdds
            .Where(row => TryMapMarket(row.MarketType, out _))
            .OrderBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.MarketType, StringComparer.Ordinal)
            .ThenBy(row => row.LineValue)
            .ToArray();
        if (goalRows.Length == 0)
            return new BotGFixtureRunResult([], null, 0);

        var fixtureId = ResolveFixtureGroupId(goalRows);
        var officialFixtureId = goalRows
            .Select(row => row.ApiFootballFixtureId)
            .FirstOrDefault(value => value is > 0);

        var predictionTimestampUtc = DateTime.UtcNow;
        Task<BotGModel2026Signals>? model2026SignalsTask = null;
        var candidates = new List<BotGCandidate>(goalRows.Length * 2);
        foreach (var sourceRow in goalRows)
        {
            var snapshotAvailable = HasImmutableOddsSnapshot(sourceRow);
            // Historical mode deliberately refuses the latest runtime snapshot: without an
            // as-of lookup it may post-date the simulated prediction even though it is immutable.
            var overOdds = snapshotAvailable && !historicalMode ? sourceRow.SnapshotOverOdds : null;
            var underOdds = snapshotAvailable && !historicalMode ? sourceRow.SnapshotUnderOdds : null;
            var twoSidedSnapshotAvailable = overOdds is > 1m && underOdds is > 1m;
            var oddsTimestampUtc = EnsureUtc(sourceRow.OddsCapturedAtUtc ?? sourceRow.UpdatedAtUtc);
            var fixtureDateUtc = ToUtcFromSantiago(sourceRow.MatchDate);
            var timestampsAreSafe = oddsTimestampUtc <= predictionTimestampUtc
                && predictionTimestampUtc < fixtureDateUtc;
            var inferenceCompletedBeforeKickoff = true;
            BotGBasePredictions? rowPredictions = null;
            string? basePredictionError = historicalMode
                ? "The online runner does not use mutable upcoming-odds rows for historical inference; use the walk-forward backtester with as-of snapshots."
                : context is null
                    ? "Prediction context was unavailable."
                    : null;
            if (!historicalMode && context is not null
                && snapshotAvailable && twoSidedSnapshotAvailable && timestampsAreSafe)
            {
                try
                {
                    // The legacy GOALS model consumes BettingLine/GoalsLine, so its signal must be
                    // produced from this exact immutable market row rather than an arbitrary first line.
                    model2026SignalsTask ??= BuildModel2026SignalsAsync(
                        sourceRow,
                        neutralMatch && swappedContext is not null,
                        cancellationToken);
                    var model2026Signals = await model2026SignalsTask;
                    rowPredictions = await BuildBasePredictionsAsync(
                        sourceRow,
                        context,
                        swappedContext,
                        teamInfo,
                        neutralMatch,
                        model2026Signals,
                        predictionTimestampUtc,
                        cancellationToken);
                    if (DateTime.UtcNow >= fixtureDateUtc)
                    {
                        rowPredictions = null;
                        inferenceCompletedBeforeKickoff = false;
                        basePredictionError = "Base-model inference did not complete before kickoff.";
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    basePredictionError = exception.Message;
                    inferenceCompletedBeforeKickoff = DateTime.UtcNow < fixtureDateUtc;
                    _logger.LogWarning(
                        exception,
                        "Bot G base signals unavailable. Fixture={Fixture}, League={League}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}, MarketType={MarketType}, Line={Line}",
                        fixtureId,
                        sourceRow.EffectiveLeague,
                        sourceRow.EffectiveHomeTeam,
                        sourceRow.EffectiveAwayTeam,
                        sourceRow.MarketType,
                        sourceRow.LineValue);
                }
            }

            foreach (var side in new[] { BotGSelection.Over, BotGSelection.Under })
            {
                var quote = new BotGMarketQuote(
                    fixtureId,
                    fixtureDateUtc,
                    predictionTimestampUtc,
                    oddsTimestampUtc,
                    sourceRow.EffectiveLeague,
                    ResolveSeason(context, predictionTimestampUtc),
                    sourceRow.EffectiveHomeTeam,
                    sourceRow.EffectiveAwayTeam,
                    sourceRow.Source,
                    MapMarket(sourceRow.MarketType),
                    side,
                    sourceRow.LineValue,
                    overOdds,
                    underOdds,
                    sourceRow.OddsSnapshotId);

                BotGCandidate candidate;
                if (historicalMode)
                {
                    candidate = BuildUnavailableCandidate(
                        quote,
                        sourceRow,
                        configuration,
                        BotGDecisionReason.FeatureTemporalLeakage,
                        basePredictionError!);
                }
                else if (!snapshotAvailable)
                {
                    candidate = BuildUnavailableCandidate(
                        quote,
                        sourceRow,
                        configuration,
                        BotGDecisionReason.FeatureTemporalLeakage,
                        "No immutable CornerOddsSnapshots row was available at prediction time.");
                }
                else if (!twoSidedSnapshotAvailable)
                {
                    candidate = BuildUnavailableCandidate(
                        quote,
                        sourceRow,
                        configuration,
                        BotGDecisionReason.NoVigUnavailable,
                        "The immutable snapshot did not contain valid two-sided decimal odds.");
                }
                else if (!timestampsAreSafe || !inferenceCompletedBeforeKickoff || DateTime.UtcNow >= fixtureDateUtc)
                {
                    candidate = BuildUnavailableCandidate(
                        quote,
                        sourceRow,
                        configuration,
                        BotGDecisionReason.FeatureTemporalLeakage,
                        "The snapshot/prediction/kickoff timestamps failed strict temporal ordering.");
                }
                else if (rowPredictions is null || context is null)
                {
                    candidate = BuildUnavailableCandidate(
                        quote,
                        sourceRow,
                        configuration,
                        BotGDecisionReason.ModelUnavailable,
                        basePredictionError ?? "Both legacy and Models 2026 signals are required.");
                }
                else
                {
                    candidate = EvaluateCandidate(
                        quote,
                        sourceRow,
                        context,
                        rowPredictions,
                        configuration,
                        neutralMatch,
                        footballIntelligenceConfiguration,
                        footballIntelligenceSnapshot);
                }

                candidate = candidate with
                {
                    RunId = runId,
                    OfficialFixtureId = officialFixtureId,
                    AutomationVersion = BuildAutomationVersion(configuration.ConfigurationVersion),
                    BaseModelTrainedThroughUtc = MaxTimestamp(
                        rowPredictions?.LegacyTrainedThroughUtc,
                        rowPredictions?.Model2026TrainedThroughUtc),
                    StakeUnits = configuration.Stake
                };
                candidates.Add(candidate with { GSelectionScore = _selector.Score(candidate, configuration) });
            }
        }

        candidates = ApplyProbabilityMonotonicityGate(candidates)
            .Select(candidate => candidate with { GSelectionScore = _selector.Score(candidate, configuration) })
            .ToList();
        var winner = _selector.SelectBestPerFixture(candidates, configuration).SingleOrDefault();
        var winnerSignature = winner is null ? null : CandidateSignature(winner);
        if (winner is not null)
        {
            candidates = candidates.Select(candidate =>
            {
                if (candidate.CandidateUuid == winner.CandidateUuid)
                    return candidate with { GSelectionScore = winner.GSelectionScore };
                if (candidate.Decision != BotGDecisionStatus.Approved)
                    return candidate;
                return candidate with
                {
                    Decision = BotGDecisionStatus.Rejected,
                    DecisionReason = BotGDecisionReason.LowerRankedCandidate,
                    DecisionReasons = candidate.DecisionReasons
                        .Append(BotGDecisionReason.LowerRankedCandidate)
                        .Distinct()
                        .ToArray(),
                    Published = false,
                    Shadow = configuration.ShadowMode
                };
            }).ToList();
            winner = candidates.Single(candidate => candidate.CandidateUuid == winner.CandidateUuid);
        }

        var persisted = 0;
        if (!dryRun)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                candidates[index] = await _candidateRepository.UpsertAsync(candidates[index], cancellationToken);
                persisted++;
            }
            if (winner is not null)
                winner = candidates.Single(candidate => CandidateSignature(candidate) == winnerSignature);
        }

        foreach (var candidate in candidates)
        {
            _logger.LogInformation(
                "Bot G candidate evaluated. BotKey={BotKey}, FixtureId={FixtureId}, CandidateId={CandidateId}, MarketType={MarketType}, Selection={Selection}, Line={Line}, Bookmaker={Bookmaker}, MarketProbability={MarketProbability}, RawProbability={RawProbability}, CalibratedProbability={CalibratedProbability}, ConservativeProbability={ConservativeProbability}, RawEdge={RawEdge}, ConservativeEdge={ConservativeEdge}, RawExpectedValue={RawExpectedValue}, ConservativeExpectedValue={ConservativeExpectedValue}, Uncertainty={Uncertainty}, Ood={Ood}, Decision={Decision}, Reason={Reason}, Shadow={Shadow}",
                configuration.BotKey,
                candidate.FixtureId,
                candidate.CandidateId,
                candidate.MarketType,
                candidate.Selection,
                candidate.Line,
                candidate.Bookmaker,
                candidate.NoVigMarketProbability,
                candidate.CandidateProbability,
                candidate.CalibratedProbability,
                candidate.ConservativeProbability,
                candidate.Edge,
                candidate.ConservativeEdge,
                candidate.ExpectedValue,
                candidate.ConservativeExpectedValue,
                candidate.ProbabilityUncertainty,
                candidate.OutOfDistributionScore,
                candidate.Decision,
                candidate.DecisionReason,
                candidate.Shadow);
        }

        return new BotGFixtureRunResult(candidates, winner, persisted);
    }

    public async Task<BotGCandidate> MarkPublishedAsync(
        BotGCandidate candidate,
        long publishedSelectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (publishedSelectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(publishedSelectionId));
        if (candidate.Decision != BotGDecisionStatus.Approved)
            throw new InvalidOperationException("Only an approved Bot G candidate can be marked as published.");
        return await _candidateRepository.UpsertAsync(candidate with
        {
            Published = true,
            Shadow = false,
            PublishedSelectionId = publishedSelectionId
        }, cancellationToken);
    }

    private BotGCandidate EvaluateCandidate(
        BotGMarketQuote quote,
        UpcomingOddsRecord sourceRow,
        PredictionContextDto context,
        BotGBasePredictions predictions,
        BotGConfiguration configuration,
        bool neutralMatch,
        FootballIntelligenceAdjustmentConfiguration footballIntelligenceConfiguration,
        MatchIntelligenceSnapshotPair? footballIntelligenceSnapshot)
    {
        try
        {
            var market = _marketProbability.Calculate(quote);
            if (!market.IsAvailable)
                return BuildUnavailableCandidate(
                    quote, sourceRow, configuration, BotGDecisionReason.NoVigUnavailable,
                    market.UnavailableReason ?? "Strict two-sided no-vig was unavailable.", predictions);

            var features = _featureBuilder.Build(new BotGFeatureBuildInput(
                quote,
                predictions,
                MapHistory(context.HomeGeneralMatches, quote.HomeTeam, quote.PredictionTimestampUtc),
                neutralMatch
                    ? []
                    : MapHistory(context.HomeAsHomeMatches, quote.HomeTeam, quote.PredictionTimestampUtc),
                MapHistory(context.AwayGeneralMatches, quote.AwayTeam, quote.PredictionTimestampUtc),
                neutralMatch
                    ? []
                    : MapHistory(context.AwayAsAwayMatches, quote.AwayTeam, quote.PredictionTimestampUtc),
                market), configuration);
            var vector = features.ToNumericVector();
            var meta = _metaModel.Predict(new BotGMetaModelInput(
                configuration.FeatureSchemaVersion,
                quote.PredictionTimestampUtc,
                quote.MarketType,
                quote.Selection,
                quote.Bookmaker,
                market.SelectedNoVigProbability,
                vector,
                configuration,
                predictions.LegacyModelVersion,
                predictions.Model2026Version,
                quote.League,
                quote.Line));
            var candidateProbability = meta.IsAvailable
                ? meta.Probability
                : market.SelectedNoVigProbability;
            var calibrated = _calibration.Calibrate(new BotGCalibrationInput(
                quote.PredictionTimestampUtc,
                quote.MarketType,
                quote.Selection,
                quote.Bookmaker,
                candidateProbability,
                _artifactEvidence.CalibrationProfiles), configuration);
            var probabilityBeforeFootballIntelligence = calibrated.IsAvailable
                ? calibrated.CalibratedProbability
                : candidateProbability;
            var footballIntelligence = FootballIntelligenceAdjustmentCalculator.Calculate(
                quote.PredictionTimestampUtc,
                quote.MarketType.ToString(),
                quote.Selection.ToString(),
                probabilityBeforeFootballIntelligence,
                footballIntelligenceSnapshot,
                footballIntelligenceConfiguration);
            var finalProbability = footballIntelligence.ProbabilityAfter;
            var uncertainty = _uncertainty.Estimate(new BotGUncertaintyInput(
                finalProbability,
                meta.EnsembleDispersion,
                calibrated.EffectiveSampleSize), configuration);
            var ood = _ood.Evaluate(new BotGOodInput(
                vector,
                _artifactEvidence.OodReferenceFeatures), configuration);
            var distribution = ResolveOutcomeDistribution(quote.Line, finalProbability, meta.SettlementDistribution);
            var distributionAvailable = distribution is not null;
            var distributionBeforeFootballIntelligence = ResolveOutcomeDistribution(
                quote.Line,
                probabilityBeforeFootballIntelligence,
                meta.SettlementDistribution);
            var expectedValueBeforeFootballIntelligence = distributionBeforeFootballIntelligence is not null
                ? _expectedValue.Calculate(
                    quote.SelectedOdds!.Value,
                    distributionBeforeFootballIntelligence).ExpectedProfitPerUnit
                : 0d;
            var rawEv = distributionAvailable
                ? _expectedValue.Calculate(quote.SelectedOdds!.Value, distribution!)
                : EmptyExpectedValue(finalProbability);
            var conservativeEv = distributionAvailable
                ? _expectedValue.Calculate(
                    quote.SelectedOdds!.Value,
                    _expectedValue.Reanchor(distribution!, uncertainty.ConservativeProbability))
                : EmptyExpectedValue(uncertainty.ConservativeProbability);
            var edge = BotGConservativeMetrics.Edge(finalProbability, market.SelectedNoVigProbability);
            var conservativeEdge = BotGConservativeMetrics.ConservativeEdge(
                uncertainty,
                market.SelectedNoVigProbability);
            var decision = _abstention.Decide(new BotGDecisionInput
            {
                Quote = quote,
                MarketProbability = market,
                MetaPrediction = meta,
                Calibration = calibrated,
                Uncertainty = uncertainty,
                OutOfDistribution = ood,
                SettlementDistributionAvailable = distributionAvailable,
                FinalProbability = finalProbability,
                ConservativeEdge = conservativeEdge,
                ConservativeExpectedValue = conservativeEv.ExpectedProfitPerUnit,
                DataQualityScore = features.DataQualityScore,
                ContextAgreementScore = features.ContextAgreementScore,
                ModelDisagreement = features.ModelDisagreement,
                HistoricalMatches = features.HistoryCount
            }, configuration);

            var snapshot = SerializeSnapshot(new
            {
                features,
                lineage = new
                {
                    sourceRow.PartidoProximoCuotaId,
                    quote.SourceOddsId,
                    immutableOddsSnapshot = true,
                    quote.OddsTimestampUtc,
                    quote.PredictionTimestampUtc,
                    quote.FixtureDateUtc,
                    neutralMatch,
                    predictions.LegacyModelVersion,
                    predictions.LegacyTrainedThroughUtc,
                    predictions.Model2026Version,
                    predictions.Model2026TrainedThroughUtc,
                    meta.ModelVersion,
                    meta.TrainedThroughUtc
                },
                decision.Explanation,
                metaUnavailableReason = meta.UnavailableReason,
                calibrationUnavailableReason = calibrated.UnavailableReason,
                oodUnavailableReason = ood.UnavailableReason,
                outcomeDistribution = distribution,
                footballIntelligence = new
                {
                    enabled = footballIntelligenceConfiguration.Enabled,
                    footballIntelligenceConfiguration.Version,
                    probabilityBeforeFootballIntelligence,
                    expectedValueBeforeFootballIntelligence,
                    result = footballIntelligence
                },
                configuration = new
                {
                    footballIntelligence = footballIntelligenceConfiguration
                }
            });
            return BuildCandidate(
                quote,
                configuration,
                sourceRow,
                predictions,
                features,
                market,
                meta,
                calibrated,
                uncertainty,
                ood,
                rawEv,
                conservativeEv,
                finalProbability,
                edge,
                conservativeEdge,
                decision,
                snapshot);
        }
        catch (BotGTemporalLeakageException exception)
        {
            return BuildUnavailableCandidate(
                quote, sourceRow, configuration, BotGDecisionReason.FeatureTemporalLeakage,
                exception.Message, predictions);
        }
        catch (ArgumentException exception)
        {
            return BuildUnavailableCandidate(
                quote, sourceRow, configuration, BotGDecisionReason.InvalidInput,
                exception.Message, predictions);
        }
    }

    private static BotGCandidate BuildCandidate(
        BotGMarketQuote quote,
        BotGConfiguration configuration,
        UpcomingOddsRecord sourceRow,
        BotGBasePredictions predictions,
        BotGFeatures features,
        BotGMarketProbabilityResult market,
        BotGMetaModelPrediction meta,
        BotGCalibrationResult calibration,
        BotGUncertaintyResult uncertainty,
        BotGOodResult ood,
        BotGExpectedValueResult rawEv,
        BotGExpectedValueResult conservativeEv,
        double finalProbability,
        double edge,
        double conservativeEdge,
        BotGDecision decision,
        string snapshot) => new()
    {
        FixtureId = quote.FixtureId,
        FixtureDateUtc = quote.FixtureDateUtc,
        PredictionTimestampUtc = quote.PredictionTimestampUtc,
        OddsTimestampUtc = quote.OddsTimestampUtc,
        SourceOddsId = quote.SourceOddsId,
        BotKey = configuration.BotKey,
        ConfigurationVersion = configuration.ConfigurationVersion,
        FeatureSchemaVersion = configuration.FeatureSchemaVersion,
        League = quote.League,
        Season = quote.Season,
        HomeTeam = quote.HomeTeam,
        AwayTeam = quote.AwayTeam,
        Bookmaker = quote.Bookmaker,
        MarketType = quote.MarketType,
        Selection = quote.Selection,
        Line = quote.Line,
        OverOdds = quote.OverOdds,
        UnderOdds = quote.UnderOdds,
        SelectedOdds = quote.SelectedOdds ?? 0m,
        RawImpliedProbability = market.SelectedRawImpliedProbability,
        NoVigMarketProbability = market.SelectedNoVigProbability,
        LegacyPrediction = features.LegacyPrediction,
        Prediction2026 = features.Prediction2026,
        ContextPrediction = features.ContextPrediction,
        HistoricalMean = features.Overall.Last20.Mean,
        HistoricalMedian = features.Overall.Last20.Median,
        HistoricalStandardDeviation = features.Overall.Last20.StandardDeviation,
        PredictionMinusLine = features.PredictionMinusLine,
        LegacyMinusMarketEquivalent = features.LegacyPrediction - features.Line,
        Model2026MinusMarketEquivalent = features.Prediction2026 - features.Line,
        CandidateProbability = meta.IsAvailable ? meta.Probability : market.SelectedNoVigProbability,
        CalibratedProbability = calibration.CalibratedProbability,
        FinalProbability = finalProbability,
        ProbabilityLowerBound = uncertainty.ProbabilityLowerBound,
        ProbabilityUpperBound = uncertainty.ProbabilityUpperBound,
        ProbabilityUncertainty = uncertainty.ProbabilityUncertainty,
        ConservativeProbability = uncertainty.ConservativeProbability,
        Edge = edge,
        ConservativeEdge = conservativeEdge,
        ExpectedValue = rawEv.ExpectedProfitPerUnit,
        ConservativeExpectedValue = conservativeEv.ExpectedProfitPerUnit,
        DataQualityScore = features.DataQualityScore,
        ContextAgreementScore = features.ContextAgreementScore,
        CalibrationReliability = calibration.Reliability,
        OutOfDistributionScore = ood.Score,
        ModelDisagreement = features.ModelDisagreement,
        Decision = decision.Status,
        DecisionReason = decision.PrimaryReason,
        DecisionReasons = decision.Reasons,
        Published = false,
        Shadow = configuration.ShadowMode,
        BaseModelVersion = $"legacy:{predictions.LegacyModelVersion}|2026:{predictions.Model2026Version}",
        MetaModelVersion = meta.ModelVersion,
        CalibrationVersion = calibration.Version,
        UncertaintyVersion = uncertainty.Version,
        OodVersion = ood.Version,
        FeatureSnapshotJson = snapshot
    };

    private BotGCandidate BuildUnavailableCandidate(
        BotGMarketQuote quote,
        UpcomingOddsRecord sourceRow,
        BotGConfiguration configuration,
        BotGDecisionReason reason,
        string explanation,
        BotGBasePredictions? predictions = null)
    {
        var market = _marketProbability.Calculate(quote);
        var probability = market.IsAvailable ? market.SelectedNoVigProbability : 0.5d;
        var uncertainty = _uncertainty.Estimate(new BotGUncertaintyInput(probability, 0d, 0d), configuration);
        var decision = new BotGDecision(BotGDecisionStatus.Abstain, reason, [reason], explanation);
        return new BotGCandidate
        {
            FixtureId = quote.FixtureId,
            FixtureDateUtc = quote.FixtureDateUtc,
            PredictionTimestampUtc = quote.PredictionTimestampUtc,
            OddsTimestampUtc = quote.OddsTimestampUtc,
            SourceOddsId = quote.SourceOddsId,
            BotKey = configuration.BotKey,
            ConfigurationVersion = configuration.ConfigurationVersion,
            FeatureSchemaVersion = configuration.FeatureSchemaVersion,
            League = quote.League,
            Season = quote.Season,
            HomeTeam = quote.HomeTeam,
            AwayTeam = quote.AwayTeam,
            Bookmaker = quote.Bookmaker,
            MarketType = quote.MarketType,
            Selection = quote.Selection,
            Line = quote.Line,
            OverOdds = quote.OverOdds,
            UnderOdds = quote.UnderOdds,
            SelectedOdds = quote.SelectedOdds ?? 0m,
            RawImpliedProbability = market.SelectedRawImpliedProbability,
            NoVigMarketProbability = market.SelectedNoVigProbability,
            LegacyPrediction = predictions?.LegacyFor(quote.MarketType) ?? 0d,
            Prediction2026 = predictions?.Model2026For(quote.MarketType) ?? 0d,
            CandidateProbability = probability,
            CalibratedProbability = probability,
            FinalProbability = probability,
            ProbabilityLowerBound = uncertainty.ProbabilityLowerBound,
            ProbabilityUpperBound = uncertainty.ProbabilityUpperBound,
            ProbabilityUncertainty = uncertainty.ProbabilityUncertainty,
            ConservativeProbability = uncertainty.ConservativeProbability,
            Edge = 0d,
            ConservativeEdge = uncertainty.ConservativeProbability - probability,
            DataQualityScore = 0d,
            CalibrationReliability = 0d,
            OutOfDistributionScore = 1d,
            Decision = decision.Status,
            DecisionReason = decision.PrimaryReason,
            DecisionReasons = decision.Reasons,
            Published = false,
            Shadow = configuration.ShadowMode,
            BaseModelVersion = predictions is null
                ? string.Empty
                : $"legacy:{predictions.LegacyModelVersion}|2026:{predictions.Model2026Version}",
            UncertaintyVersion = uncertainty.Version,
            FeatureSnapshotJson = SerializeSnapshot(new
            {
                unavailableReason = explanation,
                decision = decision.Status.ToString(),
                reason = reason.ToString(),
                sourceRow.PartidoProximoCuotaId,
                quote.SourceOddsId,
                immutableOddsSnapshotPresent = HasImmutableOddsSnapshot(sourceRow),
                snapshotUsedForInference = quote.OverOdds.HasValue || quote.UnderOdds.HasValue,
                quote.OddsTimestampUtc,
                quote.PredictionTimestampUtc,
                quote.FixtureDateUtc
            })
        };
    }

    private async Task<BotGModel2026Signals> BuildModel2026SignalsAsync(
        UpcomingOddsRecord representative,
        bool includeSwapped,
        CancellationToken cancellationToken)
    {
        var normal = await _newGenerationPredictionService.PredictAllAsync(
            BuildNewGenerationRequest(representative, swapTeams: false),
            cancellationToken);
        var swapped = includeSwapped
            ? await _newGenerationPredictionService.PredictAllAsync(
                BuildNewGenerationRequest(representative, swapTeams: true),
                cancellationToken)
            : null;
        return new BotGModel2026Signals(normal, swapped);
    }

    private async Task<BotGBasePredictions> BuildBasePredictionsAsync(
        UpcomingOddsRecord representative,
        PredictionContextDto context,
        PredictionContextDto? swappedContext,
        IReadOnlyList<TeamBi3InfoDto> teamInfo,
        bool neutralMatch,
        BotGModel2026Signals model2026Signals,
        DateTime predictionTimestampUtc,
        CancellationToken cancellationToken)
    {
        var safeContext = FilterContextAsOf(
            context,
            predictionTimestampUtc,
            representative.EffectiveHomeTeam,
            representative.EffectiveAwayTeam);
        var safeSwappedContext = swappedContext is null
            ? null
            : FilterContextAsOf(
                swappedContext,
                predictionTimestampUtc,
                representative.EffectiveAwayTeam,
                representative.EffectiveHomeTeam);
        var runtimeOdds = WithSnapshotOdds(representative);
        var legacyFeatures = _legacyFeatureBuilder.Build(runtimeOdds, safeContext, teamInfo);
        var legacy = (await _predictionApiClient.PredictMultiMarketAsync(legacyFeatures, cancellationToken)).Goals
            ?? throw new InvalidOperationException("The legacy API returned no GOALS prediction.");
        var normal2026 = model2026Signals.Normal;

        MarketPredictionDto? swappedLegacy = null;
        var swapped2026 = model2026Signals.Swapped;
        if (neutralMatch && safeSwappedContext is not null)
        {
            var swappedOdds = SwapMatchSides(runtimeOdds);
            var swappedFeatures = _legacyFeatureBuilder.Build(swappedOdds, safeSwappedContext, teamInfo);
            swappedLegacy = (await _predictionApiClient.PredictMultiMarketAsync(swappedFeatures, cancellationToken)).Goals;
            if (swapped2026 is null)
                throw new InvalidOperationException("The neutral Bot G run requires swapped Models 2026 signals.");
        }

        var legacyHome = RequireFinite(legacy.HomePrediction, "legacy home goals");
        var legacyAway = RequireFinite(legacy.AwayPrediction, "legacy away goals");
        var legacyTotal = RequireFinite(
            legacy.TotalDirectPrediction ?? legacy.FinalPrediction,
            "legacy total goals");
        if (swappedLegacy is not null)
        {
            legacyHome = Average(legacyHome, RequireFinite(swappedLegacy.AwayPrediction, "swapped legacy away goals"));
            legacyAway = Average(legacyAway, RequireFinite(swappedLegacy.HomePrediction, "swapped legacy home goals"));
            legacyTotal = Average(legacyTotal, RequireFinite(
                swappedLegacy.TotalDirectPrediction ?? swappedLegacy.FinalPrediction,
                "swapped legacy total goals"));
        }

        var home2026 = RequirePrediction(normal2026, NewGenerationModelDefinitions.HomeGoals);
        var away2026 = RequirePrediction(normal2026, NewGenerationModelDefinitions.AwayGoals);
        var total2026 = RequirePrediction(normal2026, NewGenerationModelDefinitions.TotalGoals);
        var homeValue = home2026.PredictionClipped;
        var awayValue = away2026.PredictionClipped;
        var totalValue = total2026.PredictionClipped;
        if (swapped2026 is not null)
        {
            homeValue = Average(homeValue, RequirePrediction(swapped2026, NewGenerationModelDefinitions.AwayGoals).PredictionClipped);
            awayValue = Average(awayValue, RequirePrediction(swapped2026, NewGenerationModelDefinitions.HomeGoals).PredictionClipped);
            totalValue = Average(totalValue, RequirePrediction(swapped2026, NewGenerationModelDefinitions.TotalGoals).PredictionClipped);
        }

        var cutoffs = new[] { home2026, away2026, total2026 }
            .Select(value => ParseUtc(value.TrainedThrough, $"{value.Target} trainedThrough"))
            .ToArray();
        return new BotGBasePredictions
        {
            LegacyTotal = legacyTotal,
            LegacyHome = legacyHome,
            LegacyAway = legacyAway,
            Model2026Total = totalValue,
            Model2026Home = homeValue,
            Model2026Away = awayValue,
            LegacyModelVersion = "goals_v1",
            Model2026Version = string.Join("+", new[] { home2026, away2026, total2026 }
                .Select(value => value.ModelVersion ?? value.Target)
                .Distinct(StringComparer.Ordinal)),
            LegacyTrainedThroughUtc = LegacyGoalsCutoffUtc,
            Model2026TrainedThroughUtc = cutoffs.Max()
        };
    }

    private BotGOutcomeDistribution? ResolveOutcomeDistribution(
        decimal line,
        double finalProbability,
        BotGOutcomeDistribution? artifactDistribution)
    {
        if (!BotGAsianSettlementCalculator.RequiresFiveStateDistribution(line))
            return BotGOutcomeDistribution.Binary(finalProbability);
        if (artifactDistribution is null)
            return null;
        return _expectedValue.Reanchor(artifactDistribution, finalProbability);
    }

    private static BotGExpectedValueResult EmptyExpectedValue(double probability) =>
        new(BotGOutcomeDistribution.Binary(probability), 0d, probability, 1d - probability);

    private static PredictionContextDto FilterContextAsOf(
        PredictionContextDto context,
        DateTime predictionTimestampUtc,
        string homeTeam,
        string awayTeam)
    {
        var latestSafeDate = SantiagoDate(predictionTimestampUtc);
        IReadOnlyList<MatchHistoryItemDto> Safe(
            IReadOnlyList<MatchHistoryItemDto>? rows,
            string team) => (rows ?? [])
                .Where(row => row.MatchDate < latestSafeDate)
                .Where(row => NamesEqual(row.HomeTeam, team) || NamesEqual(row.AwayTeam, team))
                .ToArray();
        return context with
        {
            HomeGeneralMatches = Safe(context.HomeGeneralMatches, homeTeam),
            HomeAsHomeMatches = Safe(context.HomeAsHomeMatches, homeTeam),
            AwayGeneralMatches = Safe(context.AwayGeneralMatches, awayTeam),
            AwayAsAwayMatches = Safe(context.AwayAsAwayMatches, awayTeam)
        };
    }

    private static IReadOnlyList<BotGHistoryObservation> MapHistory(
        IReadOnlyList<MatchHistoryItemDto>? rows,
        string team,
        DateTime predictionTimestampUtc)
    {
        if (predictionTimestampUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Bot G history mapping requires an explicit UTC prediction timestamp.");
        // MatchHistory currently exposes DateOnly.  Only a strictly earlier calendar date is
        // guaranteed to have been available before an intraday prediction; same-day rows are
        // conservatively excluded because their actual kickoff/outcome timestamp is unknown here.
        var latestSafeDate = SantiagoDate(predictionTimestampUtc);
        return (rows ?? [])
            .Where(row => row.MatchDate < latestSafeDate)
            .Select(row =>
            {
                var teamWasHome = NamesEqual(row.HomeTeam, team);
                var teamWasAway = NamesEqual(row.AwayTeam, team);
                if (!teamWasHome && !teamWasAway)
                    return null;
                var valueFor = teamWasHome ? row.HomeGoals : row.AwayGoals;
                var valueAgainst = teamWasHome ? row.AwayGoals : row.HomeGoals;
                return new BotGHistoryObservation(
                    row.Id,
                    DateTime.SpecifyKind(row.MatchDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                    valueFor,
                    valueAgainst);
            })
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();
    }

    private static UpcomingOddsRecord WithSnapshotOdds(UpcomingOddsRecord row) => row with
    {
        OverOdds = row.SnapshotOverOdds,
        UnderOdds = row.SnapshotUnderOdds
    };

    private static UpcomingOddsRecord SwapMatchSides(UpcomingOddsRecord row) => row with
    {
        HomeTeam = row.AwayTeam,
        AwayTeam = row.HomeTeam,
        StandardizedHomeTeam = row.StandardizedAwayTeam,
        StandardizedAwayTeam = row.StandardizedHomeTeam,
        HomeTeamGender = row.AwayTeamGender,
        AwayTeamGender = row.HomeTeamGender,
        MarketType = row.MarketType switch
        {
            "GoalsHomeTeam" => "GoalsAwayTeam",
            "GoalsAwayTeam" => "GoalsHomeTeam",
            _ => row.MarketType
        }
    };

    private static NewGenerationPredictionRequest BuildNewGenerationRequest(
        UpcomingOddsRecord row,
        bool swapTeams) => new(
            row.EffectiveLeague,
            Season: null,
            DateOnly.FromDateTime(row.MatchDate),
            swapTeams ? row.EffectiveAwayTeam : row.EffectiveHomeTeam,
            swapTeams ? row.EffectiveHomeTeam : row.EffectiveAwayTeam,
            HomeFormation: null,
            AwayFormation: null,
            IsKnockout: false);

    private static NewGenerationPredictionResult RequirePrediction(
        NewGenerationBatchPredictionResult result,
        string target) => result.Predictions.FirstOrDefault(value => value.Target.Equals(target, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Models 2026 returned no {target} prediction.");

    private static double RequireFinite(double? value, string name)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0d)
            throw new InvalidOperationException($"{name} is missing or invalid.");
        return value.Value;
    }

    private static DateTime ParseUtc(string? value, string name)
    {
        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new InvalidOperationException($"{name} is missing or invalid.");
        return parsed.UtcDateTime;
    }

    private static bool TryMapMarket(string sourceMarketType, out BotGMarketType market)
    {
        switch (sourceMarketType)
        {
            case "GoalsTotal": market = BotGMarketType.TotalGoals; return true;
            case "GoalsHomeTeam": market = BotGMarketType.HomeTeamGoals; return true;
            case "GoalsAwayTeam": market = BotGMarketType.AwayTeamGoals; return true;
            default: market = default; return false;
        }
    }

    private static BotGMarketType MapMarket(string sourceMarketType) =>
        TryMapMarket(sourceMarketType, out var market)
            ? market
            : throw new ArgumentException($"Bot G does not support {sourceMarketType}.");

    private static string ResolveSeason(PredictionContextDto? context, DateTime predictionTimestampUtc) => context is null
        ? string.Empty
        : (context.HomeGeneralMatches ?? []).Concat(context.AwayGeneralMatches ?? [])
            .Where(row => row.MatchDate < SantiagoDate(predictionTimestampUtc))
            .OrderByDescending(row => row.MatchDate)
            .Select(row => row.Season)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static long ResolveFixtureGroupId(IReadOnlyList<UpcomingOddsRecord> rows)
    {
        var officialIds = rows
            .Where(row => row.ApiFootballFixtureId is > 0)
            .Select(row => row.ApiFootballFixtureId!.Value)
            .Distinct()
            .ToArray();
        if (officialIds.Length > 1)
            throw new ArgumentException("Bot G received conflicting official fixture IDs in one odds group.");

        // FixtureIdentity is deliberately provider-independent and stable if an
        // official id is enriched later. The provider id is stored separately and
        // is used for verified settlement only.
        var fallbackIds = rows.Select(ResolveFallbackFixtureId).Distinct().ToArray();
        if (fallbackIds.Length != 1)
            throw new ArgumentException("Bot G received odds that do not share one canonical fallback fixture identity.");
        return fallbackIds[0];
    }

    private static long ResolveFallbackFixtureId(UpcomingOddsRecord row)
    {
        var identity = string.Join("|",
            ToUtcFromSantiago(row.MatchDate).ToString("O"),
            Normalize(row.EffectiveLeague),
            Normalize(row.EffectiveHomeTeam),
            Normalize(row.EffectiveAwayTeam));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var value = BitConverter.ToInt64(hash, 0) & long.MaxValue;
        return value == 0 ? 1 : value;
    }

    private static bool HasImmutableOddsSnapshot(UpcomingOddsRecord row) =>
        row.OddsSnapshotId is > 0 && row.OddsCapturedAtUtc.HasValue;

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime ToUtcFromSantiago(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc) return value;
        if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, SantiagoTimeZone);
    }

    private static DateOnly SantiagoDate(DateTime utcTimestamp)
    {
        if (utcTimestamp.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Bot G requires an explicit UTC timestamp before deriving the Santiago date.");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcTimestamp, SantiagoTimeZone));
    }

    private static bool NamesEqual(string left, string right) =>
        Normalize(left).Equals(Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static double Average(double left, double right) => (left + right) / 2d;

    private static string CandidateSignature(BotGCandidate candidate) => string.Join(
        "|",
        candidate.FixtureId,
        Normalize(candidate.Bookmaker),
        candidate.MarketType,
        candidate.Selection,
        candidate.Line,
        candidate.ConfigurationVersion);

    private static IReadOnlyList<BotGCandidate> ApplyProbabilityMonotonicityGate(
        IReadOnlyList<BotGCandidate> candidates)
    {
        const double tolerance = 1e-9d;
        var violatingIds = new HashSet<Guid>();
        var comparable = candidates
            .Where(candidate => candidate.Decision != BotGDecisionStatus.Abstain)
            .Where(candidate => double.IsFinite(candidate.FinalProbability))
            .GroupBy(candidate => new
            {
                candidate.FixtureId,
                Bookmaker = Normalize(candidate.Bookmaker),
                candidate.MarketType,
                candidate.Selection
            });
        foreach (var group in comparable)
        {
            var ordered = group.OrderBy(candidate => candidate.Line).ToArray();
            var violation = ordered.Zip(ordered.Skip(1)).Any(pair =>
                group.Key.Selection == BotGSelection.Over
                    ? pair.Second.FinalProbability > pair.First.FinalProbability + tolerance
                    : pair.Second.FinalProbability < pair.First.FinalProbability - tolerance);
            if (violation)
            {
                foreach (var candidate in ordered)
                    violatingIds.Add(candidate.CandidateUuid);
            }
        }

        if (violatingIds.Count == 0) return candidates;
        return candidates.Select(candidate => !violatingIds.Contains(candidate.CandidateUuid)
            ? candidate
            : candidate with
            {
                Decision = BotGDecisionStatus.Abstain,
                DecisionReason = BotGDecisionReason.PredictionMonotonicityViolation,
                DecisionReasons = candidate.DecisionReasons
                    .Append(BotGDecisionReason.PredictionMonotonicityViolation)
                    .Distinct()
                    .ToArray(),
                Published = false
            }).ToArray();
    }

    private static DateTime? MaxTimestamp(DateTime? left, DateTime? right) =>
        left.HasValue && right.HasValue
            ? (left.Value >= right.Value ? left : right)
            : left ?? right;

    private static string BuildAutomationVersion(string configurationVersion)
    {
        var value = $"{configurationVersion.Trim()}-G2026";
        if (value.Length <= 50) return value;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
        return $"{value[..35]}-{hash}-G2026";
    }

    private static string SerializeSnapshot(object value) => JsonSerializer.Serialize(value, SnapshotOptions);

    private sealed record BotGModel2026Signals(
        NewGenerationBatchPredictionResult Normal,
        NewGenerationBatchPredictionResult? Swapped);

    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly TimeZoneInfo SantiagoTimeZone = ResolveSantiagoTimeZone();

    private static TimeZoneInfo ResolveSantiagoTimeZone()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
