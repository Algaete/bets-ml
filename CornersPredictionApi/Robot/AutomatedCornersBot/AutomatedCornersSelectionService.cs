using System.Diagnostics;
using System.Text.Json;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Application.Automation;
using CornersPrediction.Application.Automation.BotC;
using CornersPrediction.Application.Automation.BotD;
using CornersPrediction.Application.Automation.BotE;
using CornersPrediction.Application.Automation.BotG;
using CornersPrediction.Domain.Automation.BotG;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Application.Teams;
using CornersPrediction.Application.MatchHistory;
using CornersPrediction.Application.RobustPickEvaluation;
using CornersPredictionApi.CompetitionFiltering;
using CornersPredictionApi.NewGenerationMl;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RobustCalibrationFallbackLevel = CornersPrediction.Domain.RobustPickEvaluation.CalibrationFallbackLevel;
using RobustCurrentSystemDecision = CornersPrediction.Domain.RobustPickEvaluation.CurrentSystemDecision;
using RobustEvidenceStatus = CornersPrediction.Domain.RobustPickEvaluation.EvidenceStatus;
using RobustDecision = CornersPrediction.Domain.RobustPickEvaluation.RobustDecision;
using RobustEvaluationMode = CornersPrediction.Domain.RobustPickEvaluation.EvaluationMode;
using RobustScenarioEvidenceSnapshot = CornersPrediction.Domain.RobustPickEvaluation.ScenarioEvidenceSnapshot;
using RobustScenarioType = CornersPrediction.Domain.RobustPickEvaluation.ScenarioType;

namespace AutomatedCornersBot.Api;

public sealed class AutomatedCornersSelectionService
{
    private const string PerformanceScorecardsCacheKey = "automated-bot-performance-scorecards-v1";
    private static readonly TimeZoneInfo SantiagoTimeZone = ResolveSantiagoTimeZone();
    private readonly AutomatedBotOptions _options;
    private readonly SqlAutomationRepository _repository;
    private readonly IRecommendationBotDefinitionRepository _botDefinitionRepository;
    private readonly PredictionApiClient _predictionApiClient;
    private readonly IGetPredictionContextUseCase _predictionContextUseCase;
    private readonly NewGenerationPredictionService _newGenerationPredictionService;
    private readonly IBotCPickDecisionEngine _botCPickDecisionEngine;
    private readonly BotGAutomationService _botGAutomationService;
    private readonly IIntelligenceSnapshotRepository _intelligenceSnapshotRepository;
    private readonly FeatureBuilder _featureBuilder;
    private readonly CompetitionEligibilityPolicy _competitionPolicy;
    private readonly IAutomatedBotPerformanceService _performanceService;
    private readonly IRobustPickEvaluationService _robustPickEvaluationService;
    private readonly RobustPickEvaluationOptions _robustOptions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AutomatedCornersSelectionService> _logger;

    public AutomatedCornersSelectionService(
        IOptions<AutomatedBotOptions> options,
        SqlAutomationRepository repository,
        IRecommendationBotDefinitionRepository botDefinitionRepository,
        PredictionApiClient predictionApiClient,
        IGetPredictionContextUseCase predictionContextUseCase,
        NewGenerationPredictionService newGenerationPredictionService,
        IBotCPickDecisionEngine botCPickDecisionEngine,
        BotGAutomationService botGAutomationService,
        IIntelligenceSnapshotRepository intelligenceSnapshotRepository,
        FeatureBuilder featureBuilder,
        CompetitionEligibilityPolicy competitionPolicy,
        IAutomatedBotPerformanceService performanceService,
        IRobustPickEvaluationService robustPickEvaluationService,
        IOptions<RobustPickEvaluationOptions> robustOptions,
        IMemoryCache cache,
        ILogger<AutomatedCornersSelectionService> logger)
    {
        _options = options.Value;
        _repository = repository;
        _botDefinitionRepository = botDefinitionRepository;
        _predictionApiClient = predictionApiClient;
        _predictionContextUseCase = predictionContextUseCase;
        _newGenerationPredictionService = newGenerationPredictionService;
        _botCPickDecisionEngine = botCPickDecisionEngine;
        _botGAutomationService = botGAutomationService;
        _intelligenceSnapshotRepository = intelligenceSnapshotRepository;
        _featureBuilder = featureBuilder;
        _competitionPolicy = competitionPolicy;
        _performanceService = performanceService;
        _robustPickEvaluationService = robustPickEvaluationService;
        _robustOptions = robustOptions.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AutomatedRunResponse> RunAsync(
        RunAutomatedCornersRequest? request,
        CancellationToken cancellationToken)
    {
        var effectiveRequest = request ?? new RunAutomatedCornersRequest(
            null, null, null, null, null, null, null, false, null, null, false, 1, 100, null, false, false, null, false);
        var chileNow = GetChileNow();
        if (effectiveRequest.HistoricalBacktest && effectiveRequest.HistoricalBackfill)
        {
            throw new ArgumentException("HistoricalBacktest and HistoricalBackfill cannot both be enabled.");
        }
        if (effectiveRequest.HistoricalBacktest && !effectiveRequest.DryRun)
        {
            throw new ArgumentException("Historical backtests must run with DryRun=true.");
        }
        if (effectiveRequest.HistoricalBackfill && effectiveRequest.DryRun)
        {
            throw new ArgumentException("Historical backfills persist Pending picks and must run with DryRun=false.");
        }
        var historicalMode = effectiveRequest.HistoricalBacktest || effectiveRequest.HistoricalBackfill;
        if (historicalMode &&
            (effectiveRequest.DateFrom is null || effectiveRequest.DateTo is null))
        {
            throw new ArgumentException("Historical runs require DateFrom and DateTo.");
        }

        var minimumMatchDate = historicalMode
            ? DateTime.MinValue
            : chileNow.AddMinutes(Math.Max(0, _options.MinimumLeadTimeMinutes));
        var dateFrom = effectiveRequest.DateFrom ?? DateOnly.FromDateTime(chileNow);
        var dateTo = effectiveRequest.DateTo ?? dateFrom.AddDays(7);
        if (dateTo < dateFrom)
        {
            throw new ArgumentException("DateTo cannot be earlier than DateFrom.");
        }

        var stake = effectiveRequest.Stake ?? _options.DefaultStake;
        if (stake <= 0)
        {
            throw new ArgumentException("Stake must be greater than zero.");
        }

        var minEdge = effectiveRequest.MinEdge ?? _options.MinEdge;
        var minExpectedValue = effectiveRequest.MinExpectedValue ?? _options.MinExpectedValue;
        var minDistanceToLine = effectiveRequest.MinDistanceToLine ?? _options.MinDistanceToLine;
        var maxContextDifference = effectiveRequest.MaxContextDifference ?? _options.MaxContextDifference;
        var allowDisagreement = effectiveRequest.AllowModelDisagreement ?? _options.AllowModelDisagreement;
        var requestedMarketFamilies = ParseMarketFamilies(effectiveRequest.MarketFamilies);
        var requestedBotKeys = ParseBotKeys(
            effectiveRequest.BotKeys,
            effectiveRequest.OnlyBotC,
            effectiveRequest.RunBotC ?? true);
        var botDefinitions = effectiveRequest.RunAllEnabledBots
            ? (await _botDefinitionRepository.GetAllAsync(cancellationToken))
                .Where(definition =>
                    definition.IsEnabled &&
                    !RecommendationBotLifecycle.IsRetired(definition.BotKey))
                .OrderBy(definition => definition.BotKey, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : await _botDefinitionRepository.GetByKeysAsync(requestedBotKeys, cancellationToken);
        if (effectiveRequest.RunAllEnabledBots)
        {
            requestedBotKeys = botDefinitions
                .Select(definition => definition.BotKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var foundBotKeys = botDefinitions
            .Select(definition => definition.BotKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingBotKeys = requestedBotKeys.Where(key => !foundBotKeys.Contains(key)).ToArray();
        if (missingBotKeys.Length > 0)
        {
            throw new ArgumentException($"Unknown bot keys: {string.Join(", ", missingBotKeys)}.");
        }

        var disabledBotKeys = botDefinitions
            .Where(definition =>
                !definition.IsEnabled ||
                RecommendationBotLifecycle.IsRetired(definition.BotKey))
            .Select(definition => definition.BotKey)
            .ToArray();
        if (disabledBotKeys.Length > 0)
        {
            throw new ArgumentException($"Disabled bot keys cannot be executed: {string.Join(", ", disabledBotKeys)}.");
        }

        // G is intentionally removed before BuildBotProfile. Passing GOALS_MARKET_ANCHORED
        // through the legacy profile builder would make it behave like Bot A and could publish
        // without the market-anchor/abstention pipeline.
        var botGDefinitions = botDefinitions.Where(definition => definition.UsesBotG).ToArray();
        var standardBotDefinitions = botDefinitions.Where(definition => !definition.UsesBotG).ToArray();
        var allBotProfiles = standardBotDefinitions
            .Select(definition => BuildBotProfile(
                definition,
                minEdge,
                minExpectedValue,
                minDistanceToLine,
                maxContextDifference,
                allowDisagreement))
            .ToArray();
        var botProfiles = allBotProfiles.Where(profile => !profile.UsesPickSelector).ToArray();
        var legacySelectorProfiles = allBotProfiles
            .Where(profile => profile.UsesPickSelector && !profile.UsesNewGenerationModels)
            .ToArray();
        var newGenerationProfiles = allBotProfiles.Where(profile => profile.UsesNewGenerationModels).ToArray();
        var selectorProfiles = legacySelectorProfiles.Concat(newGenerationProfiles).ToArray();
        var expectedAutomationVersionCount = allBotProfiles.Count(profile => profile.PublishEnabled)
            + botGDefinitions.Count(definition => definition.PublishEnabled);
        if (allBotProfiles.Length == 0 && botGDefinitions.Length == 0)
        {
            throw new ArgumentException("None of the requested bots are enabled.");
        }
        var enforceLiveProductionGate = !effectiveRequest.DryRun && !historicalMode;
        var productionScorecards = await LoadProductionScorecardsAsync(
            enforceLiveProductionGate
            && (allBotProfiles.Any(profile => profile.PublishEnabled)
                || botGDefinitions.Any(definition => definition.PublishEnabled)),
            cancellationToken);
        var runId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var batchNumber = Math.Max(1, effectiveRequest.BatchNumber);
        var batchSize = NormalizeBatchSize(effectiveRequest.BatchSize);

        var fetchedOddsRows = await _repository.GetUpcomingOddsAsync(
            dateFrom,
            dateTo,
            minimumMatchDate,
            effectiveRequest.League,
            effectiveRequest.ExcludeExistingSelections
                && allBotProfiles.All(profile => profile.PublishEnabled)
                && botGDefinitions.All(definition => definition.PublishEnabled),
            Math.Max(1, expectedAutomationVersionCount),
            resolveApiFootballFixtureId: true,
            cancellationToken: cancellationToken);
        var eligibleOddsRows = fetchedOddsRows
            .Where(row => _competitionPolicy.IsEligible(
                row.EffectiveLeague,
                row.SourceUrl,
                row.HomeTeamGender,
                row.AwayTeamGender))
            .Where(row => historicalMode || row.MatchDate > minimumMatchDate)
            .Where(row => requestedMarketFamilies.Count == 0 || requestedMarketFamilies.Contains(MarketFamily(row.MarketType)))
            .ToArray();
        var availableOddsRows = eligibleOddsRows.Length;
        var batchOffset = (batchNumber - 1) * batchSize;
        var allGroupedMatches = eligibleOddsRows
            .GroupBy(BuildMatchIdentity)
            .ToArray();
        // G evaluates complete line curves and ranks globally within a fixture. Never
        // split one fixture across batches, even for a live/shadow run.
        var batchCompleteFixtures = historicalMode || botGDefinitions.Length > 0;
        var totalBatchItems = batchCompleteFixtures
            ? allGroupedMatches.Length
            : availableOddsRows;
        var totalBatches = CalculateTotalBatches(totalBatchItems, batchSize);
        var oddsRows = SelectBatchOddsRows(
            eligibleOddsRows,
            batchOffset,
            batchSize,
            batchCompleteFixtures);
        var processedBatchItems = batchCompleteFixtures
            ? oddsRows.GroupBy(BuildMatchIdentity).Count()
            : oddsRows.Length;
        var batchStart = processedBatchItems == 0 ? 0 : batchOffset + 1;
        var batchEnd = processedBatchItems == 0 ? 0 : batchOffset + processedBatchItems;
        var calibrationHistoryBySourceBot = new Dictionary<string, IReadOnlyList<BotECalibrationObservation>>(
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<long, MatchIntelligenceSnapshotPair> intelligenceSnapshotsByFixture =
            new Dictionary<long, MatchIntelligenceSnapshotPair>();
        if (oddsRows.Length > 0)
        {
            var calibrationSourceBots = selectorProfiles
                .Select(profile => profile.SelectorConfiguration?.EmpiricalCalibration)
                .Where(configuration => configuration?.Enabled == true)
                .Select(configuration => configuration!.SourceBotKey.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var latestCandidateDate = oddsRows.Max(row => EnsureUtc(row.MatchDate));
            foreach (var sourceBotKey in calibrationSourceBots)
            {
                try
                {
                    var observations = await _repository.GetBotECalibrationHistoryAsync(
                        sourceBotKey,
                        latestCandidateDate,
                        cancellationToken);
                    calibrationHistoryBySourceBot[sourceBotKey] = observations;
                    _logger.LogInformation(
                        "Empirical calibration evidence loaded. SourceBot={SourceBot}, Observations={Observations}, AsOf={AsOf}",
                        sourceBotKey,
                        observations.Count,
                        latestCandidateDate);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    calibrationHistoryBySourceBot[sourceBotKey] = Array.Empty<BotECalibrationObservation>();
                    _logger.LogError(
                        exception,
                        "Empirical calibration evidence could not be loaded. SourceBot={SourceBot}",
                        sourceBotKey);
                }
            }

            if (botDefinitions.Any(definition => definition.FootballIntelligenceConfiguration.Enabled))
            {
                var fixtureIds = oddsRows
                    .Where(row => row.ApiFootballFixtureId.HasValue)
                    .Select(row => row.ApiFootballFixtureId!.Value)
                    .Distinct()
                    .ToArray();
                if (fixtureIds.Length > 0)
                {
                    try
                    {
                        intelligenceSnapshotsByFixture = await _intelligenceSnapshotRepository.GetLatestPairsAsync(
                            fixtureIds,
                            oddsRows.Max(row => ToUtcFromSantiago(row.MatchDate)),
                            cancellationToken);
                        _logger.LogInformation(
                            "Pre-match intelligence snapshots loaded. RequestedFixtures={RequestedFixtures}, AvailableFixtures={AvailableFixtures}",
                            fixtureIds.Length,
                            intelligenceSnapshotsByFixture.Count);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(
                            exception,
                            "Pre-match intelligence snapshots could not be loaded. Intelligence-enabled bots will continue with neutral adjustment");
                    }
                }
            }
        }

        _logger.LogInformation(
            "Filtro y lote del bot aplicados. OddsRows={OddsRows}, Eligible={Eligible}, Excluded={Excluded}, Batch={BatchNumber}/{TotalBatches}, BatchRange={BatchStart}-{BatchEnd}",
            fetchedOddsRows.Count,
            availableOddsRows,
            fetchedOddsRows.Count - availableOddsRows,
            batchNumber,
            totalBatches,
            batchStart,
            batchEnd);

        var groupedMatches = oddsRows
            .GroupBy(BuildMatchIdentity)
            .ToArray();

        var selections = new List<AutomatedSelectionResult>();
        var skipped = new List<SkippedMatchResult>();
        var errors = new List<ErrorMatchResult>();
        var insertedRows = 0;
        var updatedRows = 0;
        var botGCandidatesEvaluated = 0;
        var botGApprovedCandidates = 0;
        var botGRejectedCandidates = 0;
        var botGAbstainedCandidates = 0;
        var teamInfoCache = new Dictionary<string, IReadOnlyList<TeamBi3InfoDto>>(StringComparer.OrdinalIgnoreCase);
        var newGenerationPredictionCache = new Dictionary<string, NewGenerationBatchPredictionResult>(StringComparer.OrdinalIgnoreCase);
        var progressLogEveryMatches = Math.Max(1, _options.ProgressLogEveryMatches);

        _logger.LogInformation(
            "Automated corners bot run started. RunId={RunId}, DateFrom={DateFrom}, DateTo={DateTo}, OddsRows={OddsRows}, GroupedMatches={GroupedMatches}, DryRun={DryRun}, HistoricalMode={HistoricalMode}, HistoricalBackfill={HistoricalBackfill}, OverUnderEnabled={OverUnderEnabled}",
            runId,
            dateFrom,
            dateTo,
            oddsRows.Length,
            groupedMatches.Length,
            effectiveRequest.DryRun,
            historicalMode,
            effectiveRequest.HistoricalBackfill,
            _options.EnableOverUnderPrediction);

        for (var matchIndex = 0; matchIndex < groupedMatches.Length; matchIndex++)
        {
            var matchGroup = groupedMatches[matchIndex];
            var representative = matchGroup.First();
            var league = representative.EffectiveLeague;
            var homeTeam = representative.EffectiveHomeTeam;
            var awayTeam = representative.EffectiveAwayTeam;
            var teamGender = NormalizeGender(representative.HomeTeamGender);
            var isNeutralMatch = IsNeutralOrInternationalMatch(representative);
            var currentMarketFamily = MarketFamily(representative.MarketType);
            var applicableBotGDefinitions = currentMarketFamily.Equals("GOALS", StringComparison.OrdinalIgnoreCase)
                ? botGDefinitions
                    .Where(definition => definition.IsLeagueAllowed(currentMarketFamily, league))
                    .ToArray()
                : Array.Empty<RecommendationBotDefinitionDto>();
            var hasLeagueEligibleStandardBot = allBotProfiles.Any(profile =>
                profile.MarketFamilies.Contains(currentMarketFamily)
                && profile.IsLeagueAllowed(currentMarketFamily, league));

            if (applicableBotGDefinitions.Length == 0 && !hasLeagueEligibleStandardBot)
            {
                skipped.Add(new SkippedMatchResult(
                    league,
                    homeTeam,
                    awayTeam,
                    representative.MatchDate,
                    $"Todos los bots solicitados excluyen {league} para {currentMarketFamily}."));
                continue;
            }

            if (matchIndex == 0 || (matchIndex + 1) % progressLogEveryMatches == 0)
            {
                _logger.LogInformation(
                    "Automated corners bot progress. RunId={RunId}, ProcessedMatches={ProcessedMatches}/{TotalMatches}, ElapsedSeconds={ElapsedSeconds:0.0}, Current={League}: {HomeTeam} vs {AwayTeam}, OddsLines={OddsLines}",
                    runId,
                    matchIndex + 1,
                    groupedMatches.Length,
                    stopwatch.Elapsed.TotalSeconds,
                    league,
                    homeTeam,
                    awayTeam,
                    matchGroup.Count());
            }

            try
            {
                var predictionContext = await GetPredictionContextAsync(
                    league,
                    homeTeam,
                    awayTeam,
                    teamGender,
                    DateOnly.FromDateTime(representative.MatchDate),
                    cancellationToken);

                PredictionContextDto? swappedPredictionContext = null;
                if (predictionContext is not null && isNeutralMatch)
                {
                    swappedPredictionContext = await GetPredictionContextAsync(
                        league,
                        awayTeam,
                        homeTeam,
                        teamGender,
                        DateOnly.FromDateTime(representative.MatchDate),
                        cancellationToken);
                }

                IReadOnlyList<TeamBi3InfoDto> teamInfo = Array.Empty<TeamBi3InfoDto>();
                if (predictionContext is not null)
                {
                    if (teamInfoCache.TryGetValue($"{league}|{teamGender}", out var cachedTeamInfo))
                    {
                        teamInfo = cachedTeamInfo;
                    }
                    else
                    {
                        teamInfo = await _predictionApiClient.GetTeamInfoAsync(league, teamGender, cancellationToken);
                        teamInfoCache[$"{league}|{teamGender}"] = teamInfo;
                    }
                }

                foreach (var botGDefinition in applicableBotGDefinitions)
                {
                    var botGResult = await _botGAutomationService.EvaluateFixtureAsync(
                        runId,
                        botGDefinition,
                        matchGroup.ToArray(),
                        predictionContext,
                        swappedPredictionContext,
                        teamInfo,
                        isNeutralMatch,
                        historicalMode,
                        effectiveRequest.DryRun,
                        ResolveIntelligenceSnapshot(matchGroup, intelligenceSnapshotsByFixture),
                        cancellationToken);
                    botGCandidatesEvaluated += botGResult.Candidates.Count;
                    botGApprovedCandidates += botGResult.Approved;
                    botGRejectedCandidates += botGResult.Rejected;
                    botGAbstainedCandidates += botGResult.Abstained;

                    var botGConfiguration = botGDefinition.GoalsMarketAnchoredConfiguration!;
                    if (botGResult.SelectedForPublication is not null
                        && botGDefinition.PublishEnabled
                        && botGConfiguration.PublishEnabled
                        && !botGConfiguration.ShadowMode
                        && !historicalMode
                        && !effectiveRequest.DryRun)
                    {
                        var sourceOdds = FindBotGSourceOdds(matchGroup, botGResult.SelectedForPublication);
                        var eligibility = AutomatedBotProductionEligibilityPolicy.Evaluate(
                            productionScorecards,
                            botGResult.SelectedForPublication.BotKey,
                            botGResult.SelectedForPublication.MarketFamily,
                            botGResult.SelectedForPublication.MarketType.ToString(),
                            botGResult.SelectedForPublication.Selection.ToString(),
                            botGResult.SelectedForPublication.Bookmaker,
                            botGResult.SelectedForPublication.AutomationVersion,
                            botGResult.SelectedForPublication.Line,
                            botGResult.SelectedForPublication.OddsTimestampUtc,
                            botGResult.SelectedForPublication.PredictionTimestampUtc,
                            immutableOddsSnapshotAvailable: sourceOdds.OddsSnapshotId is > 0
                                && sourceOdds.OddsCapturedAtUtc.HasValue
                                && sourceOdds.SnapshotOverOdds is > 1m
                                && sourceOdds.SnapshotUnderOdds is > 1m);
                        if (!eligibility.CanPublish)
                        {
                            skipped.Add(new SkippedMatchResult(
                                league,
                                homeTeam,
                                awayTeam,
                                representative.MatchDate,
                                $"G2026: candidato aprobado en shadow, pero no promovible: {eligibility.Reason}"));
                            continue;
                        }
                        var publishedCandidate = ToPublishedBotGCandidate(
                            sourceOdds,
                            predictionContext!,
                            botGResult.SelectedForPublication);
                        var published = await PersistCandidateAsync(
                            botGResult.SelectedForPublication.RunId,
                            BuildBotGPublishProfile(botGDefinition, botGResult.SelectedForPublication),
                            publishedCandidate,
                            baseStake: 1m,
                            dryRun: false,
                            maximumStakeUnits: eligibility.MaxStakeUnits,
                            cancellationToken);
                        if (published.RejectedByRobustLayer || published.Result is null)
                        {
                            skipped.Add(new SkippedMatchResult(
                                league,
                                homeTeam,
                                awayTeam,
                                representative.MatchDate,
                                $"G2026: rechazado por la capa robusta: {published.RobustReason}"));
                            continue;
                        }
                        insertedRows += published.Inserted ? 1 : 0;
                        updatedRows += published.Updated ? 1 : 0;
                        selections.Add(published.Result);
                    }
                }

                if (predictionContext is null)
                {
                    skipped.Add(new SkippedMatchResult(league, homeTeam, awayTeam, representative.MatchDate, "Prediction context was empty."));
                    continue;
                }

                if (!HasEnoughPredictionHistory(predictionContext, isNeutralMatch)
                    || (isNeutralMatch && !HasEnoughPredictionHistory(swappedPredictionContext, true)))
                {
                    var historyReason = BuildHistoryAvailabilityReason(
                        predictionContext,
                        swappedPredictionContext,
                        isNeutralMatch);
                    await PersistPendingBotCEvaluationsAsync(
                        runId,
                        selectorProfiles.Where(profile =>
                            profile.MarketFamilies.Contains(currentMarketFamily)
                            && profile.IsLeagueAllowed(currentMarketFamily, league)),
                        matchGroup,
                        historyReason,
                        effectiveRequest.DryRun,
                        cancellationToken);
                    skipped.Add(new SkippedMatchResult(
                        league,
                        homeTeam,
                        awayTeam,
                        representative.MatchDate,
                        historyReason));
                    continue;
                }

                var predictionBundles = new List<PredictionBundle>();
                var legacyPredictionCache = new Dictionary<string, PredictionResultDto>(StringComparer.Ordinal);

                // In Bot-C-only backtests there is no reason to run the legacy A/B
                // prediction endpoints. Bot C builds its own 2026 prediction batch
                // below, so skipping these calls preserves the decision while making
                // historical simulations substantially faster.
                var applicableLegacyProfiles = botProfiles
                    .Where(profile => profile.MarketFamilies.Contains(currentMarketFamily)
                        && profile.IsLeagueAllowed(currentMarketFamily, league))
                    .ToArray();
                var applicableLegacySelectorProfiles = legacySelectorProfiles
                    .Where(profile => profile.MarketFamilies.Contains(currentMarketFamily)
                        && profile.IsLeagueAllowed(currentMarketFamily, league))
                    .ToArray();
                var applicableNewGenerationProfiles = newGenerationProfiles
                    .Where(profile => profile.MarketFamilies.Contains(currentMarketFamily)
                        && profile.IsLeagueAllowed(currentMarketFamily, league))
                    .ToArray();
                IEnumerable<UpcomingOddsRecord> legacyOddsRows = applicableLegacyProfiles.Length == 0
                    && applicableLegacySelectorProfiles.Length == 0
                    ? Array.Empty<UpcomingOddsRecord>()
                    : matchGroup
                        .Where(row => applicableLegacySelectorProfiles.Length > 0
                            || !IsNewGenerationOnlyMarket(row.MarketType))
                        .OrderBy(row => row.LineValue);
                foreach (var odds in legacyOddsRows)
                {
                    predictionBundles.Add(await BuildPredictionBundleAsync(
                        odds,
                        predictionContext,
                        swappedPredictionContext,
                        teamInfo,
                        isNeutralMatch,
                        legacyPredictionCache,
                        cancellationToken));
                }

                var matchHadSelection = false;
                foreach (var botProfile in predictionBundles.Count == 0
                             ? Array.Empty<BotVariantProfile>()
                             : applicableLegacyProfiles)
                {
                    AutomatedSelectionCandidate? bestCandidate = null;
                    string? bestRejectedReason = null;

                    foreach (var predictionBundle in predictionBundles)
                    {
                        var candidateOrReason = EvaluateCandidate(
                            predictionBundle.Odds,
                            predictionBundle.PredictionContext,
                            predictionBundle.CornersPrediction,
                            predictionBundle.OverUnderPrediction,
                            predictionBundle.Features,
                            botProfile,
                            predictionBundle.IsNeutralAdjusted,
                            ResolveIntelligenceSnapshot(
                                [predictionBundle.Odds],
                                intelligenceSnapshotsByFixture));

                        if (candidateOrReason.candidate is null)
                        {
                            bestRejectedReason = candidateOrReason.reason;
                            continue;
                        }

                        if (bestCandidate is null || candidateOrReason.candidate.SelectionScore > bestCandidate.SelectionScore)
                        {
                            bestCandidate = candidateOrReason.candidate;
                        }
                    }

                    if (bestCandidate is null)
                    {
                        skipped.Add(new SkippedMatchResult(
                            league,
                            homeTeam,
                            awayTeam,
                            representative.MatchDate,
                            $"{botProfile.Key}: {bestRejectedReason ?? "No line passed the bot thresholds."}"));
                        continue;
                    }

                    matchHadSelection = true;
                    if (!effectiveRequest.DryRun && !botProfile.PublishEnabled)
                    {
                        skipped.Add(new SkippedMatchResult(
                            league,
                            homeTeam,
                            awayTeam,
                            representative.MatchDate,
                            $"{botProfile.Key}: candidato aprobado, pero PublishEnabled está deshabilitado."));
                        continue;
                    }
                    decimal? maximumStakeUnits = null;
                    if (enforceLiveProductionGate)
                    {
                        var eligibility = EvaluateProductionEligibility(
                            productionScorecards,
                            botProfile,
                            bestCandidate);
                        if (!eligibility.CanPublish)
                        {
                            skipped.Add(new SkippedMatchResult(
                                league,
                                homeTeam,
                                awayTeam,
                                representative.MatchDate,
                                $"{botProfile.Key}: candidato aprobado y conservado en monitoreo; {eligibility.Reason}"));
                            continue;
                        }

                        maximumStakeUnits = eligibility.MaxStakeUnits;
                    }
                    var persisted = await PersistCandidateAsync(
                        runId,
                        botProfile,
                        bestCandidate,
                        stake,
                        effectiveRequest.DryRun,
                        maximumStakeUnits,
                        cancellationToken);
                    if (persisted.RejectedByRobustLayer || persisted.Result is null)
                    {
                        skipped.Add(new SkippedMatchResult(
                            league,
                            homeTeam,
                            awayTeam,
                            representative.MatchDate,
                            $"{botProfile.Key}: rechazado por la capa robusta: {persisted.RobustReason}"));
                        continue;
                    }
                    insertedRows += persisted.Inserted ? 1 : 0;
                    updatedRows += persisted.Updated ? 1 : 0;
                    selections.Add(persisted.Result);
                }

                if (predictionBundles.Count > 0 && applicableLegacySelectorProfiles.Length > 0)
                {
                    var legacySelectorResult = await EvaluateSelectorProfilesAsync(
                        runId,
                        applicableLegacySelectorProfiles,
                        predictionBundles,
                        calibrationHistoryBySourceBot,
                        intelligenceSnapshotsByFixture,
                        league,
                        homeTeam,
                        awayTeam,
                        representative.MatchDate,
                        stake,
                        effectiveRequest.DryRun,
                        enforceLiveProductionGate,
                        productionScorecards,
                        cancellationToken);
                    matchHadSelection |= legacySelectorResult.HadSelection;
                    insertedRows += legacySelectorResult.InsertedRows;
                    updatedRows += legacySelectorResult.UpdatedRows;
                    selections.AddRange(legacySelectorResult.Selections);
                    skipped.AddRange(legacySelectorResult.Skipped);
                }

                if (applicableNewGenerationProfiles.Length > 0)
                {
                    try
                    {
                        var newGenerationBundles = await BuildNewGenerationPredictionBundlesAsync(
                            matchGroup.OrderBy(row => row.LineValue).ToArray(),
                            predictionContext,
                            swappedPredictionContext,
                            teamInfo,
                            isNeutralMatch,
                            newGenerationPredictionCache,
                            cancellationToken);
                        foreach (var newGenerationProfile in applicableNewGenerationProfiles)
                        {
                            AutomatedSelectionCandidate? bestCandidate = null;
                            BotCEvaluation? bestEvaluation = null;
                            string? bestRejectedReason = null;
                            var botCEvaluations = new List<BotCEvaluation>();

                            foreach (var predictionBundle in newGenerationBundles)
                            {
                                var evaluation = EvaluateBotCCandidate(
                                    predictionBundle,
                                    newGenerationProfile,
                                    calibrationHistoryBySourceBot,
                                    intelligenceSnapshotsByFixture);
                                botCEvaluations.Add(evaluation);
                                if (evaluation.Candidate is null)
                                {
                                    bestRejectedReason = evaluation.Decision.Summary;
                                    continue;
                                }

                                if (bestCandidate is null || evaluation.Candidate.SelectionScore > bestCandidate.SelectionScore)
                                {
                                    bestCandidate = evaluation.Candidate;
                                    bestEvaluation = evaluation;
                                }
                            }

                            if (!effectiveRequest.DryRun)
                            {
                                // Persist the decision audit before any productive write. If
                                // publication fails, the approved/rejected evidence is still
                                // present and no unaudited pick can be created.
                                await PersistSelectorEvaluationsAsync(
                                    runId,
                                    newGenerationProfile,
                                    botCEvaluations,
                                    bestEvaluation,
                                    publishedSelectionId: null,
                                    winnerOnly: false,
                                    cancellationToken);
                            }

                            long? publishedSelectionId = null;
                            if (bestCandidate is null)
                            {
                                skipped.Add(new SkippedMatchResult(
                                    league,
                                    homeTeam,
                                    awayTeam,
                                    representative.MatchDate,
                                    $"{newGenerationProfile.Key}: {bestRejectedReason ?? "No line passed the Models 2026 thresholds."}"));
                            }
                            else
                            {
                                matchHadSelection = true;
                                var publishEligibility = !enforceLiveProductionGate
                                    ? new AutomatedBotProductionEligibility(true, "Dry-run.")
                                    : EvaluateProductionEligibility(
                                        productionScorecards,
                                        newGenerationProfile,
                                        bestCandidate);
                                if (effectiveRequest.DryRun
                                    || (newGenerationProfile.PublishEnabled
                                        && (!enforceLiveProductionGate || publishEligibility.CanPublish)))
                                {
                                    var persisted = await PersistCandidateAsync(
                                        runId,
                                        newGenerationProfile,
                                        bestCandidate,
                                        stake,
                                        effectiveRequest.DryRun,
                                        enforceLiveProductionGate ? publishEligibility.MaxStakeUnits : null,
                                        cancellationToken);
                                    if (persisted.RejectedByRobustLayer || persisted.Result is null)
                                    {
                                        skipped.Add(new SkippedMatchResult(
                                            league,
                                            homeTeam,
                                            awayTeam,
                                            representative.MatchDate,
                                            $"{newGenerationProfile.Key}: rechazado por la capa robusta: {persisted.RobustReason}"));
                                    }
                                    else
                                    {
                                        insertedRows += persisted.Inserted ? 1 : 0;
                                        updatedRows += persisted.Updated ? 1 : 0;
                                        selections.Add(persisted.Result);
                                        publishedSelectionId = effectiveRequest.DryRun
                                            ? null
                                            : persisted.Result.Selection.AutomatedCornerBetSelectionId;
                                    }
                                }
                                else
                                {
                                    var reason = !newGenerationProfile.PublishEnabled
                                        ? "publicación deshabilitada"
                                        : publishEligibility.Reason;
                                    skipped.Add(new SkippedMatchResult(
                                        league,
                                        homeTeam,
                                        awayTeam,
                                        representative.MatchDate,
                                        $"{newGenerationProfile.Key}: candidato aprobado y auditado; {reason}."));
                                }
                            }

                            if (!effectiveRequest.DryRun && publishedSelectionId.HasValue)
                            {
                                await PersistSelectorEvaluationsAsync(
                                    runId,
                                    newGenerationProfile,
                                    botCEvaluations,
                                    bestEvaluation,
                                    publishedSelectionId,
                                    winnerOnly: true,
                                    cancellationToken);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (ArgumentException exception)
                        when (exception.Message.StartsWith(
                            "Pre-match history is insufficient.",
                            StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "Models 2026 bots skipped {League} {HomeTeam} vs {AwayTeam}: {Reason}",
                            league,
                            homeTeam,
                            awayTeam,
                            exception.Message);
                        skipped.Add(new SkippedMatchResult(
                            league,
                            homeTeam,
                            awayTeam,
                            representative.MatchDate,
                            $"Models 2026: {exception.Message}"));
                        await PersistPendingBotCEvaluationsAsync(
                            runId,
                            applicableNewGenerationProfiles,
                            matchGroup,
                            exception.Message,
                            effectiveRequest.DryRun,
                            cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(
                            exception,
                            "Models 2026 bots failed for {League} {HomeTeam} vs {AwayTeam}",
                            league,
                            homeTeam,
                            awayTeam);
                        errors.Add(new ErrorMatchResult(
                            league,
                            homeTeam,
                            awayTeam,
                            representative.MatchDate,
                            $"Models 2026: {exception.Message}"));
                    }
                }

                if (!matchHadSelection)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Automated selection failed for {League} {HomeTeam} vs {AwayTeam}",
                    league,
                    homeTeam,
                    awayTeam);

                errors.Add(new ErrorMatchResult(league, homeTeam, awayTeam, representative.MatchDate, exception.Message));
            }
        }

        stopwatch.Stop();
        var botCounts = botDefinitions.ToDictionary(
            definition => definition.BotKey,
            definition => definition.UsesBotG
                ? selections.Count(result => result.Selection.AutomationVersion.EndsWith(
                    "-G2026",
                    StringComparison.OrdinalIgnoreCase))
                : selections.Count(result => string.Equals(
                    result.Selection.AutomationVersion,
                    allBotProfiles.Single(profile => profile.Key.Equals(
                        definition.BotKey,
                        StringComparison.OrdinalIgnoreCase)).AutomationVersion,
                    StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        _logger.LogInformation(
            "Automated corners bot run finished. RunId={RunId}, ElapsedSeconds={ElapsedSeconds:0.0}, Selected={Selected}, Skipped={Skipped}, Errors={Errors}, Inserted={Inserted}, Updated={Updated}",
            runId,
            stopwatch.Elapsed.TotalSeconds,
            selections.Count,
            skipped.Count,
            errors.Count,
            insertedRows,
            updatedRows);

        return new AutomatedRunResponse(
            RunId: runId,
            DateFrom: dateFrom,
            DateTo: dateTo,
            AvailableOddsRows: availableOddsRows,
            BatchNumber: batchNumber,
            BatchSize: batchSize,
            BatchStart: batchStart,
            BatchEnd: batchEnd,
            TotalBatches: totalBatches,
            TotalOddsRows: oddsRows.Length,
            TotalMatches: groupedMatches.Length,
            SelectedMatches: selections.Count,
            InsertedRows: insertedRows,
            UpdatedRows: updatedRows,
            SkippedMatches: skipped.Count,
            ErrorMatches: errors.Count,
            BotCounts: botCounts,
            Selections: selections,
            Skipped: skipped,
            Errors: errors,
            BotGCandidatesEvaluated: botGCandidatesEvaluated,
            BotGApprovedCandidates: botGApprovedCandidates,
            BotGRejectedCandidates: botGRejectedCandidates,
            BotGAbstainedCandidates: botGAbstainedCandidates);
    }

    public async Task<AutomatedOddsAvailabilityResponse> GetAvailabilityAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var chileNow = GetChileNow();
        var minimumMatchDate = chileNow.AddMinutes(Math.Max(0, _options.MinimumLeadTimeMinutes));
        var dateFrom = DateOnly.FromDateTime(chileNow);
        var dateTo = dateFrom.AddDays(7);
        var effectiveBatchSize = NormalizeBatchSize(batchSize);

        var fetchedOddsRows = await _repository.GetUpcomingOddsAsync(
            dateFrom,
            dateTo,
            minimumMatchDate,
            league: null,
            excludeExistingSelections: false,
            expectedAutomationVersionCount: 1,
            resolveApiFootballFixtureId: false,
            cancellationToken: cancellationToken);
        var eligibleOddsRows = fetchedOddsRows
            .Where(row => row.MatchDate > minimumMatchDate)
            .Where(row => _competitionPolicy.IsEligible(
                row.EffectiveLeague,
                row.SourceUrl,
                row.HomeTeamGender,
                row.AwayTeamGender))
            .ToArray();

        return new AutomatedOddsAvailabilityResponse(
            DateFrom: dateFrom,
            DateTo: dateTo,
            TotalOddsRows: eligibleOddsRows.Length,
            TotalMatches: eligibleOddsRows
                .GroupBy(BuildMatchIdentity)
                .Count(),
            BatchSize: effectiveBatchSize,
            TotalBatches: CalculateTotalBatches(eligibleOddsRows.Length, effectiveBatchSize));
    }

    private static int NormalizeBatchSize(int batchSize) =>
        Math.Clamp(batchSize <= 0 ? 100 : batchSize, 1, 100);

    private static int CalculateTotalBatches(int totalRows, int batchSize) =>
        totalRows == 0 ? 0 : (int)Math.Ceiling(totalRows / (double)batchSize);

    private static DateTime GetChileNow()
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SantiagoTimeZone);

    private static TimeZoneInfo ResolveSantiagoTimeZone()
    {
        foreach (var timeZoneId in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private async Task<SelectorProfilesRunResult> EvaluateSelectorProfilesAsync(
        Guid runId,
        IReadOnlyList<BotVariantProfile> profiles,
        IReadOnlyList<PredictionBundle> bundles,
        IReadOnlyDictionary<string, IReadOnlyList<BotECalibrationObservation>> calibrationHistoryBySourceBot,
        IReadOnlyDictionary<long, MatchIntelligenceSnapshotPair> intelligenceSnapshotsByFixture,
        string league,
        string homeTeam,
        string awayTeam,
        DateTime matchDate,
        decimal stake,
        bool dryRun,
        bool enforceLiveProductionGate,
        IReadOnlyCollection<AutomatedBotPerformanceScorecard> productionScorecards,
        CancellationToken cancellationToken)
    {
        var selections = new List<AutomatedSelectionResult>();
        var skipped = new List<SkippedMatchResult>();
        var inserted = 0;
        var updated = 0;

        foreach (var profile in profiles)
        {
            AutomatedSelectionCandidate? bestCandidate = null;
            BotCEvaluation? bestEvaluation = null;
            var evaluations = new List<BotCEvaluation>();
            string? rejectedReason = null;
            foreach (var bundle in bundles)
            {
                var evaluation = EvaluateBotCCandidate(
                    bundle,
                    profile,
                    calibrationHistoryBySourceBot,
                    intelligenceSnapshotsByFixture);
                evaluations.Add(evaluation);
                if (evaluation.Candidate is null)
                {
                    rejectedReason = evaluation.Decision.Summary;
                }
                else if (bestCandidate is null || evaluation.Candidate.SelectionScore > bestCandidate.SelectionScore)
                {
                    bestCandidate = evaluation.Candidate;
                    bestEvaluation = evaluation;
                }
            }

            if (!dryRun)
            {
                await PersistSelectorEvaluationsAsync(
                    runId,
                    profile,
                    evaluations,
                    bestEvaluation,
                    publishedSelectionId: null,
                    winnerOnly: false,
                    cancellationToken);
            }

            long? publishedSelectionId = null;
            if (bestCandidate is null)
            {
                skipped.Add(new SkippedMatchResult(league, homeTeam, awayTeam, matchDate,
                    $"{profile.Key}: {rejectedReason ?? "Ninguna línea superó el selector."}"));
            }
            else
            {
                var publishEligibility = !enforceLiveProductionGate
                    ? new AutomatedBotProductionEligibility(true, "Dry-run.")
                    : EvaluateProductionEligibility(productionScorecards, profile, bestCandidate);
                if (dryRun
                    || (profile.PublishEnabled
                        && (!enforceLiveProductionGate || publishEligibility.CanPublish)))
                {
                    var persisted = await PersistCandidateAsync(
                        runId,
                        profile,
                        bestCandidate,
                        stake,
                        dryRun,
                        enforceLiveProductionGate ? publishEligibility.MaxStakeUnits : null,
                        cancellationToken);
                    if (persisted.RejectedByRobustLayer || persisted.Result is null)
                    {
                        skipped.Add(new SkippedMatchResult(
                            league,
                            homeTeam,
                            awayTeam,
                            matchDate,
                            $"{profile.Key}: rechazado por la capa robusta: {persisted.RobustReason}"));
                    }
                    else
                    {
                        inserted += persisted.Inserted ? 1 : 0;
                        updated += persisted.Updated ? 1 : 0;
                        selections.Add(persisted.Result);
                        publishedSelectionId = dryRun
                            ? null
                            : persisted.Result.Selection.AutomatedCornerBetSelectionId;
                    }
                }
                else
                {
                    var reason = !profile.PublishEnabled
                        ? "publicación deshabilitada"
                        : publishEligibility.Reason;
                    skipped.Add(new SkippedMatchResult(
                        league,
                        homeTeam,
                        awayTeam,
                        matchDate,
                        $"{profile.Key}: candidato aprobado y auditado; {reason}."));
                }
            }

            if (!dryRun && publishedSelectionId.HasValue)
            {
                await PersistSelectorEvaluationsAsync(
                    runId,
                    profile,
                    evaluations,
                    bestEvaluation,
                    publishedSelectionId,
                    winnerOnly: true,
                    cancellationToken);
            }
        }

        return new SelectorProfilesRunResult(selections.Count > 0, inserted, updated, selections, skipped);
    }

    private async Task PersistSelectorEvaluationsAsync(
        Guid runId,
        BotVariantProfile profile,
        IReadOnlyCollection<BotCEvaluation> evaluations,
        BotCEvaluation? winner,
        long? publishedSelectionId,
        bool winnerOnly,
        CancellationToken cancellationToken)
    {
        foreach (var evaluation in evaluations)
        {
            var isWinner = ReferenceEquals(evaluation, winner);
            if (winnerOnly && !isWinner)
                continue;

            var storedDecision = evaluation.Decision;
            if (storedDecision.Decision == "Approved" && !isWinner)
            {
                storedDecision = storedDecision with
                {
                    Decision = "Rejected",
                    DecisionReasons = storedDecision.DecisionReasons
                        .Append("REJECTED_LOWER_RANKED_CANDIDATE")
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    Summary = $"Rejected: otra línea o mercado aprobado obtuvo un score superior. {storedDecision.Summary}"
                };
            }

            await _repository.UpsertBotCEvaluationAsync(
                new PersistBotCEvaluationCommand(
                    runId,
                    profile.Key,
                    profile.AutomationVersion,
                    evaluation.Bundle.Odds,
                    MapSelectionMarketType(evaluation.Bundle.Odds.MarketType),
                    BaseModelName(profile),
                    BaseModelVersion(
                        profile,
                        evaluation.Bundle.CornersPrediction,
                        evaluation.Bundle.Odds.MarketType),
                    storedDecision,
                    BaseModelTrainedThrough(profile, evaluation.Bundle.CornersPrediction),
                    isWinner ? publishedSelectionId : null),
                cancellationToken);
        }
    }

    private BotVariantProfile BuildBotProfile(
        RecommendationBotDefinitionDto definition,
        double minEdge,
        double minExpectedValue,
        double minDistanceToLine,
        double maxContextDifference,
        bool allowModelDisagreement)
    {
        var lift = Math.Max(0, _options.ConservativeProbabilityLift);
        var isConservative = definition.BaseStrategy.Equals(
            RecommendationBotBaseStrategies.LegacyConservative,
            StringComparison.OrdinalIgnoreCase);
        var usesPickSelector = definition.UsesPickSelector;
        var usesNewGenerationModels = definition.UsesNewGenerationModels;
        var defaultStakeMultiplier = isConservative
            ? Convert.ToDecimal(Math.Clamp(_options.ConservativeStakeMultiplier, 0.01d, 1d))
            : 1m;
        var markets = definition.MarketFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var footballIntelligence = definition.FootballIntelligenceConfiguration;
        var configurationLineage = usesPickSelector
            ? definition.SelectorConfiguration?.ToJson()
            : footballIntelligence.Enabled
                ? definition.StrategyConfigurationJson
                : null;

        return new BotVariantProfile(
            Key: definition.BotKey,
            AutomationVersion: BuildAutomationVersion(
                definition.BotKey,
                configurationLineage),
            DisplayName: definition.DisplayName,
            UsesPickSelector: usesPickSelector,
            UsesNewGenerationModels: usesNewGenerationModels,
            MarketFamilies: markets,
            MinEdge: definition.MinEdge ?? (isConservative ? minEdge * (1d + lift) : minEdge),
            MinExpectedValue: definition.MinExpectedValue ?? (isConservative ? minExpectedValue * (1d + lift) : minExpectedValue),
            MinDistanceToLine: definition.MinDistanceToLine ?? (isConservative ? minDistanceToLine * (1d + lift) : minDistanceToLine),
            MaxContextDifference: definition.MaxContextDifference ?? (isConservative
                ? maxContextDifference * Math.Max(0.10d, 1d - lift)
                : maxContextDifference),
            AllowModelDisagreement: definition.AllowModelDisagreement ?? (!isConservative && !usesNewGenerationModels && allowModelDisagreement),
            MinOddsExclusive: definition.MinOddsExclusive ?? (isConservative ? _options.ConservativeMinOdds : null),
            MinProbabilityLiftOverImplied: definition.MinProbabilityLiftOverImplied ?? (isConservative ? lift : 0),
            StakeMultiplier: definition.StakeMultiplier ?? defaultStakeMultiplier,
            SelectorConfiguration: definition.SelectorConfiguration,
            FootballIntelligence: footballIntelligence)
        {
            PublishEnabled = definition.PublishEnabled,
            LeagueFilters = definition.LeagueFilters
        };
    }

    private static UpcomingOddsRecord FindBotGSourceOdds(
        IEnumerable<UpcomingOddsRecord> rows,
        BotGCandidate candidate)
    {
        var expectedSourceMarket = candidate.MarketType switch
        {
            BotGMarketType.TotalGoals => "GoalsTotal",
            BotGMarketType.HomeTeamGoals => "GoalsHomeTeam",
            BotGMarketType.AwayTeamGoals => "GoalsAwayTeam",
            _ => throw new ArgumentOutOfRangeException(nameof(candidate.MarketType))
        };
        var match = rows.FirstOrDefault(row =>
            row.OddsSnapshotId == candidate.SourceOddsId
            && row.Source.Equals(candidate.Bookmaker, StringComparison.OrdinalIgnoreCase)
            && row.MarketType.Equals(expectedSourceMarket, StringComparison.Ordinal)
            && row.LineValue == candidate.Line)
            ?? rows.FirstOrDefault(row =>
                row.Source.Equals(candidate.Bookmaker, StringComparison.OrdinalIgnoreCase)
                && row.MarketType.Equals(expectedSourceMarket, StringComparison.Ordinal)
                && row.LineValue == candidate.Line)
            ?? throw new InvalidOperationException("The approved Bot G candidate lost its immutable source quote.");
        return match with
        {
            ApiFootballFixtureId = match.ApiFootballFixtureId ?? candidate.OfficialFixtureId,
            OverOdds = candidate.OverOdds,
            UnderOdds = candidate.UnderOdds
        };
    }

    private static BotVariantProfile BuildBotGPublishProfile(
        RecommendationBotDefinitionDto definition,
        BotGCandidate candidate)
    {
        var configuration = definition.GoalsMarketAnchoredConfiguration
            ?? throw new InvalidOperationException("Bot G configuration was unavailable at publication time.");
        if (!definition.PublishEnabled || !configuration.PublishEnabled || configuration.ShadowMode)
            throw new InvalidOperationException("Bot G publication is disabled. The candidate must remain shadow.");
        return new BotVariantProfile(
            Key: BotGConfiguration.DefaultBotKey,
            AutomationVersion: candidate.AutomationVersion,
            DisplayName: definition.DisplayName,
            UsesPickSelector: false,
            UsesNewGenerationModels: false,
            MarketFamilies: new HashSet<string>(["GOALS"], StringComparer.OrdinalIgnoreCase),
            MinEdge: configuration.Thresholds.MinimumConservativeEdge,
            MinExpectedValue: configuration.Thresholds.MinimumConservativeExpectedValue,
            MinDistanceToLine: 0d,
            MaxContextDifference: double.MaxValue,
            AllowModelDisagreement: false,
            MinOddsExclusive: configuration.Thresholds.MinimumOdds,
            MinProbabilityLiftOverImplied: 0d,
            StakeMultiplier: configuration.Stake,
            SelectorConfiguration: null,
            FootballIntelligence: definition.FootballIntelligenceConfiguration)
        {
            PublishEnabled = true,
            LeagueFilters = definition.LeagueFilters
        };
    }

    private static AutomatedSelectionCandidate ToPublishedBotGCandidate(
        UpcomingOddsRecord odds,
        PredictionContextDto context,
        BotGCandidate candidate)
    {
        if (candidate.Decision != BotGDecisionStatus.Approved)
            throw new InvalidOperationException("Only an approved Bot G candidate can be published.");
        var quantitySignal = (candidate.LegacyPrediction + candidate.Prediction2026) / 2d;
        var contextDifference = Math.Abs(quantitySignal - candidate.ContextPrediction);
        var prediction = new PredictionResultDto
        {
            PredictedTotalCorners = quantitySignal,
            PredTotalDirect = candidate.MarketType == BotGMarketType.TotalGoals ? quantitySignal : null,
            PredHomeCorners = candidate.MarketType == BotGMarketType.HomeTeamGoals ? quantitySignal : null,
            PredAwayCorners = candidate.MarketType == BotGMarketType.AwayTeamGoals ? quantitySignal : null,
            BettingLine = Convert.ToDouble(candidate.Line),
            DistanceToLine = Math.Abs(quantitySignal - Convert.ToDouble(candidate.Line)),
            RecommendedSide = candidate.Selection.ToString().ToUpperInvariant(),
            Confidence = "MARKET_ANCHORED",
            Message = "Bot G2026 market-anchored probability with calibrated conservative EV.",
            ModelDifference = candidate.ModelDisagreement,
            ModelConsensus = candidate.ModelDisagreement <= 0.5d ? "HIGH" : "LOW",
            Mae = 0d,
            Rmse = 0d,
            ModelGeneration = "G2026",
            ModelVersion = candidate.BaseModelVersion,
            TrainedThrough = candidate.BaseModelTrainedThroughUtc?.ToString("O"),
            FeatureSet = candidate.FeatureSchemaVersion
        };
        var featureSnapshot = JsonSerializer.Deserialize<JsonElement>(candidate.FeatureSnapshotJson);
        var decisionReason = JsonSerializer.Serialize(new
        {
            botProfile = candidate.BotKey,
            automationVersion = candidate.AutomationVersion,
            strategy = "Goals Market Anchored",
            decision = candidate.Decision.ToString(),
            probabilityEdge = candidate.ConservativeEdge,
            expectedValue = candidate.ConservativeExpectedValue,
            featureSnapshot,
            model = new
            {
                name = "Bot G market-anchored ensemble",
                version = candidate.BaseModelVersion,
                trainedThrough = candidate.BaseModelTrainedThroughUtc
            }
        });
        return new AutomatedSelectionCandidate
        {
            BotGCandidateId = candidate.CandidateId > 0
                ? candidate.CandidateId
                : throw new InvalidOperationException("Bot G publication requires a persisted audit candidate."),
            Odds = odds,
            CornersPrediction = prediction,
            PredictionContext = context,
            Features = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["botGCandidateId"] = candidate.CandidateId,
                ["configurationVersion"] = candidate.ConfigurationVersion,
                ["marketNoVigProbability"] = candidate.NoVigMarketProbability,
                ["candidateProbability"] = candidate.CandidateProbability,
                ["calibratedProbability"] = candidate.CalibratedProbability,
                ["conservativeProbability"] = candidate.ConservativeProbability,
                ["uncertainty"] = candidate.ProbabilityUncertainty,
                ["ood"] = candidate.OutOfDistributionScore
            },
            SelectedSide = candidate.Selection.ToString(),
            SelectedOdds = candidate.SelectedOdds,
            ModelProbability = candidate.ConservativeProbability,
            ImpliedProbability = candidate.RawImpliedProbability,
            ProbabilityEdge = candidate.ConservativeEdge,
            ExpectedValue = candidate.ConservativeExpectedValue,
            KellyFraction = 0d,
            DistanceToLine = Math.Abs(quantitySignal - Convert.ToDouble(candidate.Line)),
            ContextDifference = contextDifference,
            SelectionScore = candidate.GSelectionScore,
            DecisionReason = decisionReason,
            SelectionStatus = "Pending"
        };
    }

    private static string BaseModelName(BotVariantProfile profile) =>
        profile.UsesNewGenerationModels ? "Models 2026" : "Legacy ML artifacts";

    private static string BaseModelVersion(
        BotVariantProfile profile,
        PredictionResultDto prediction,
        string marketType) =>
        profile.SelectorConfiguration?.BaseModelVersionOverride
        ?? prediction.ModelVersion
        ?? marketType switch
        {
            "CornersTotal" or "CornersHomeTeam" or "CornersAwayTeam" => "legacy-corners-filtered-v1",
            "GoalsTotal" or "GoalsHomeTeam" or "GoalsAwayTeam" => "goals_v1",
            "ShotsTotal" or "ShotsHomeTeam" or "ShotsAwayTeam" => "shots_v3_catboost",
            _ => "sog_v1"
        };

    private static DateTime? BaseModelTrainedThrough(BotVariantProfile profile, PredictionResultDto prediction) =>
        profile.SelectorConfiguration?.BaseModelTrainedThroughUtc
        ?? ParseTrainedThroughUtc(prediction.TrainedThrough);

    private string BuildAutomationVersion(string botKey, string? experimentConfiguration = null)
    {
        var experimentToken = string.IsNullOrWhiteSpace(experimentConfiguration)
            ? string.Empty
            : $"-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(experimentConfiguration.Trim())))[..8]}";
        var value = $"{_options.AutomationVersion}{experimentToken}-{botKey}";
        if (value.Length <= 50)
        {
            return value;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)))[..8];
        return $"{value[..41]}-{hash}";
    }

    private async Task<PersistCandidateResult> PersistCandidateAsync(
        Guid runId,
        BotVariantProfile botProfile,
        AutomatedSelectionCandidate candidate,
        decimal baseStake,
        bool dryRun,
        decimal? maximumStakeUnits,
        CancellationToken cancellationToken)
    {
        var profileStake = CalculateProfileStake(baseStake, botProfile);
        if (!dryRun && maximumStakeUnits.HasValue)
        {
            if (maximumStakeUnits.Value <= 0m)
            {
                throw new InvalidOperationException(
                    $"Bot {botProfile.Key} cannot publish without a positive production stake cap.");
            }

            profileStake = Math.Min(profileStake, maximumStakeUnits.Value);
        }
        if (!dryRun && !botProfile.PublishEnabled)
        {
            throw new InvalidOperationException(
                $"Bot {botProfile.Key} cannot publish because PublishEnabled is false.");
        }

        RobustPickEvaluationExecution? robustEvaluation = null;
        if (_robustOptions.Enabled)
        {
            try
            {
                robustEvaluation = await _robustPickEvaluationService.EvaluateAsync(
                    BuildRobustEvaluationInput(
                        runId,
                        botProfile,
                        candidate,
                        profileStake,
                        botPickSelectionId: null),
                    persist: false,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Robust pre-publication evaluation failed. BotKey={BotKey}, Match={HomeTeam} vs {AwayTeam}, Market={Market}, Line={Line}, ConfiguredMode={Mode}",
                    botProfile.Key,
                    candidate.Odds.EffectiveHomeTeam,
                    candidate.Odds.EffectiveAwayTeam,
                    candidate.Odds.MarketType,
                    candidate.Odds.LineValue,
                    _robustOptions.Mode);
                if (_robustOptions.Mode.Equals("Enforce", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Robust evaluation is in Enforce mode and failed before publication.",
                        exception);
                }
            }
        }

        if (robustEvaluation?.Decision.Mode == RobustEvaluationMode.Enforce)
        {
            if (robustEvaluation.Decision.EffectiveDecision is RobustDecision.Reject or RobustDecision.ManualReview
                || robustEvaluation.Decision.EffectiveStake <= 0m)
            {
                if (!dryRun)
                {
                    await _robustPickEvaluationService.PersistAsync(
                        robustEvaluation,
                        botPickSelectionId: null,
                        cancellationToken);
                }

                return new PersistCandidateResult(
                    Result: null,
                    Inserted: false,
                    Updated: false,
                    RejectedByRobustLayer: true,
                    RobustReason: robustEvaluation.Decision.HumanReadableReason);
            }

            profileStake = Math.Min(profileStake, robustEvaluation.Decision.EffectiveStake);
        }

        if (dryRun)
        {
            return new PersistCandidateResult(
                new AutomatedSelectionResult(
                    "DRY_RUN",
                    ToPersistedSelection(runId, botProfile.AutomationVersion, candidate, profileStake)),
                Inserted: false,
                Updated: false,
                RejectedByRobustLayer: false,
                RobustReason: robustEvaluation?.Decision.HumanReadableReason);
        }

        var upsert = await _repository.UpsertSelectionAsync(
            new PersistSelectionCommand
            {
                BotGCandidateId = candidate.BotGCandidateId,
                RunId = runId,
                BotKey = botProfile.Key,
                AutomationVersion = botProfile.AutomationVersion,
                Odds = candidate.Odds,
                SelectedSide = candidate.SelectedSide,
                SelectedOdds = candidate.SelectedOdds,
                Stake = profileStake,
                ImpliedProbability = candidate.ImpliedProbability,
                ModelProbability = candidate.ModelProbability,
                ProbabilityEdge = candidate.ProbabilityEdge,
                ExpectedValue = candidate.ExpectedValue,
                KellyFraction = candidate.KellyFraction,
                SelectionScore = candidate.SelectionScore,
                CornersPrediction = candidate.CornersPrediction,
                OverUnderPrediction = candidate.OverUnderPrediction,
                PredictionContext = candidate.PredictionContext,
                DecisionReason = candidate.DecisionReason
            },
            cancellationToken);
        var selection = ToPersistedSelection(
            runId,
            botProfile.AutomationVersion,
            candidate,
            profileStake) with
        {
            AutomatedCornerBetSelectionId = upsert.SelectionId
        };

        if (robustEvaluation is not null)
        {
            try
            {
                await _robustPickEvaluationService.PersistAsync(
                    robustEvaluation,
                    upsert.SelectionId,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Robust evaluation persistence failed after pick upsert. SelectionId={SelectionId}, BotKey={BotKey}, Mode={Mode}",
                    upsert.SelectionId,
                    botProfile.Key,
                    robustEvaluation.Decision.Mode);
                if (robustEvaluation.Decision.Mode == RobustEvaluationMode.Enforce)
                {
                    throw new InvalidOperationException(
                        $"Robust Enforce evaluation could not be persisted for selection {upsert.SelectionId}.",
                        exception);
                }
            }
        }

        return new PersistCandidateResult(
            new AutomatedSelectionResult(upsert.MergeAction, selection),
            Inserted: upsert.MergeAction.Equals("INSERT", StringComparison.OrdinalIgnoreCase),
            Updated: upsert.MergeAction.Equals("UPDATE", StringComparison.OrdinalIgnoreCase),
            RejectedByRobustLayer: false,
            RobustReason: robustEvaluation?.Decision.HumanReadableReason);
    }

    private RobustPickEvaluationInput BuildRobustEvaluationInput(
        Guid runId,
        BotVariantProfile botProfile,
        AutomatedSelectionCandidate candidate,
        decimal originalStake,
        long? botPickSelectionId)
    {
        var odds = candidate.Odds;
        var prediction = candidate.CornersPrediction;
        var fixtureStartUtc = ToUtcFromSantiago(odds.MatchDate);
        var quoteTimestampUtc = EnsureUtc(odds.OddsCapturedAtUtc ?? odds.UpdatedAtUtc);
        var decisionRoot = TryParseJson(candidate.DecisionReason);
        var predictionAsOfUtc = ReadJsonDateTime(
                decisionRoot,
                "featureSnapshot", "predictionTimestampUtc")
            ?? ReadJsonDateTime(decisionRoot, "predictionTimestampUtc")
            ?? quoteTimestampUtc;
        predictionAsOfUtc = EnsureUtc(predictionAsOfUtc);
        // The robust snapshot must not claim an AsOf earlier than any evidence it
        // consumes. Quotes and the feature snapshot can differ by milliseconds;
        // persisting the prediction time alone made the temporal SQL guard reject
        // otherwise valid live evaluations after the pick had already been saved.
        var evaluationAsOfUtc = predictionAsOfUtc >= quoteTimestampUtc
            ? predictionAsOfUtc
            : quoteTimestampUtc;

        var rawProbability = FirstFinite(
            ReadJsonDouble(decisionRoot, "BaseRawProbability"),
            ReadJsonDouble(decisionRoot, "featureSnapshot", "model", "baseRawProbability"),
            ReadJsonDouble(decisionRoot, "featureSnapshot", "footballIntelligence", "probabilityBeforeFootballIntelligence"),
            candidate.ModelProbability);
        var calibratedProbability = Math.Clamp(
            FirstFinite(
                ReadJsonDouble(decisionRoot, "FinalProbability"),
                ReadJsonDouble(decisionRoot, "featureSnapshot", "marketProbability", "finalProbability"),
                candidate.ModelProbability),
            0d,
            1d);
        rawProbability = Math.Clamp(rawProbability, 0d, 1d);

        var uncertainty = Math.Max(0d, FirstFinite(
            ReadJsonDouble(decisionRoot, "featureSnapshot", "empiricalCalibration", "result", "StandardError"),
            ReadFeatureDouble(candidate.Features, "uncertainty"),
            0d));
        var conservativeProbability = ReadJsonDouble(
            decisionRoot,
            "featureSnapshot", "empiricalCalibration", "result", "ConservativeEquivalentProbability");
        var probabilityLower = Math.Clamp(
            conservativeProbability
                ?? Math.Min(rawProbability, calibratedProbability) - uncertainty,
            0d,
            1d);
        var probabilityUpper = Math.Clamp(
            Math.Max(rawProbability, calibratedProbability) + uncertainty,
            0d,
            1d);

        var dataQuality = Math.Clamp(
            FirstFinite(
                ReadJsonDouble(decisionRoot, "DataQualityScore"),
                ReadJsonDouble(decisionRoot, "featureSnapshot", "quality", "dataQuality"),
                ReadFeatureDouble(candidate.Features, "dataQualityScore"),
                0.50d),
            0d,
            1d);
        var directPrediction = ResolveBasePredictedValue(prediction, odds.MarketType);
        var isTotalMarket = odds.MarketType.EndsWith("Total", StringComparison.OrdinalIgnoreCase);
        var modelMae = prediction.Mae > 0d && double.IsFinite(prediction.Mae)
            ? prediction.Mae
            : (double?)null;
        var calibrationTier = ReadJsonString(
            decisionRoot,
            "featureSnapshot", "empiricalCalibration", "result", "EvidenceTier");
        var calibrationFallback = ParseCalibrationFallback(calibrationTier);
        var intelligenceStatus = ResolveRobustIntelligenceStatus(
            botProfile.FootballIntelligence.Enabled,
            decisionRoot);
        var officialLineupAvailable = ReadJsonBoolean(
            decisionRoot,
            "featureSnapshot", "intelligenceEvidence", "officialLineupAvailable")
            ?? ReadJsonBoolean(decisionRoot, "intelligenceEvidence", "officialLineupAvailable");
        var actionableFactCount = ReadJsonInt32(
                decisionRoot,
                "featureSnapshot", "intelligenceEvidence", "actionableFactCount")
            ?? ReadJsonInt32(decisionRoot, "intelligenceEvidence", "actionableFactCount")
            ?? 0;
        var independentSourceCount = ReadJsonInt32(
                decisionRoot,
                "featureSnapshot", "intelligenceEvidence", "independentSourceCount")
            ?? ReadJsonInt32(decisionRoot, "intelligenceEvidence", "independentSourceCount")
            ?? 0;
        var intelligenceSnapshotAgeMinutes = ReadJsonInt32(
                decisionRoot,
                "featureSnapshot", "intelligenceEvidence", "snapshotAgeMinutes")
            ?? ReadJsonInt32(decisionRoot, "intelligenceEvidence", "snapshotAgeMinutes");

        var logicalFixtureId = ResolveLogicalFixtureId(odds);
        var baseModelVersion = BaseModelVersion(botProfile, prediction, odds.MarketType);
        var baseEvidenceIds = new List<string> { $"model:{baseModelVersion}" };
        if (odds.OddsSnapshotId is > 0)
        {
            baseEvidenceIds.Add($"odds-snapshot:{odds.OddsSnapshotId.Value}");
        }
        var scenarioEvidence = new Dictionary<RobustScenarioType, RobustScenarioEvidenceSnapshot>
        {
            [RobustScenarioType.Base] = new(
                RobustScenarioType.Base,
                "Base model snapshot",
                RobustEvidenceStatus.ReviewedNeutral,
                HasStructuredEvidence: true,
                IsAdjustmentValidated: true,
                ProbabilityWeight: 1m,
                PredictionAdjustment: 0m,
                ProbabilityAdjustment: 0m,
                Confidence: Math.Max(0.01m, ToRobustDecimal(dataQuality)),
                EvidenceIds: baseEvidenceIds,
                AsOfUtc: predictionAsOfUtc,
                ExpiresUtc: fixtureStartUtc,
                AdjustmentVersion: baseModelVersion,
                HistoricalEventObservationCount: 0,
                Reason: "BASE_MODEL_AND_QUOTE_CAPTURED_PREMATCH")
        };
        if (botProfile.FootballIntelligence.Enabled)
        {
            var intelligenceIds = new[]
                {
                    ReadJsonInt64(decisionRoot, "featureSnapshot", "footballIntelligence", "result", "HomeSnapshotId"),
                    ReadJsonInt64(decisionRoot, "featureSnapshot", "footballIntelligence", "result", "AwaySnapshotId")
                }
                .Where(value => value is > 0)
                .Select(value => $"intelligence-snapshot:{value!.Value}")
                .ToArray();
            var intelligenceAdjustment = ToRobustDecimal(ReadJsonDouble(
                decisionRoot,
                "featureSnapshot", "footballIntelligence", "result", "ProbabilityAdjustment") ?? 0d);
            var intelligenceConfidence = Math.Clamp(ToRobustDecimal(FirstFinite(
                ReadJsonDouble(decisionRoot, "featureSnapshot", "intelligenceEvidence", "home", "OverallNewsConfidence"),
                ReadJsonDouble(decisionRoot, "intelligenceEvidence", "home", "OverallNewsConfidence"),
                ReadJsonDouble(decisionRoot, "featureSnapshot", "intelligenceEvidence", "away", "OverallNewsConfidence"),
                ReadJsonDouble(decisionRoot, "intelligenceEvidence", "away", "OverallNewsConfidence"),
                dataQuality)), 0.01m, 1m);
            var evidenceAsOf = ReadJsonDateTime(
                    decisionRoot,
                    "featureSnapshot", "intelligenceEvidence", "asOfUtc")
                ?? ReadJsonDateTime(decisionRoot, "intelligenceEvidence", "asOfUtc")
                ?? predictionAsOfUtc;
            scenarioEvidence[RobustScenarioType.Intelligence] = new(
                RobustScenarioType.Intelligence,
                "Pre-match intelligence",
                intelligenceStatus,
                HasStructuredEvidence: intelligenceIds.Length > 0 && actionableFactCount > 0,
                IsAdjustmentValidated: intelligenceStatus is RobustEvidenceStatus.AppliedPositive
                    or RobustEvidenceStatus.AppliedNegative,
                ProbabilityWeight: intelligenceConfidence,
                PredictionAdjustment: 0m,
                ProbabilityAdjustment: intelligenceAdjustment,
                Confidence: intelligenceConfidence,
                EvidenceIds: intelligenceIds,
                AsOfUtc: EnsureUtc(evidenceAsOf),
                ExpiresUtc: fixtureStartUtc,
                AdjustmentVersion: botProfile.FootballIntelligence.Version,
                HistoricalEventObservationCount: 0,
                Reason: intelligenceStatus.ToString());
            if (officialLineupAvailable == true && intelligenceIds.Length > 0)
            {
                scenarioEvidence[RobustScenarioType.Lineup] = new(
                    RobustScenarioType.Lineup,
                    "Confirmed lineup reviewed",
                    RobustEvidenceStatus.ReviewedNeutral,
                    HasStructuredEvidence: true,
                    IsAdjustmentValidated: true,
                    ProbabilityWeight: intelligenceConfidence,
                    PredictionAdjustment: 0m,
                    ProbabilityAdjustment: 0m,
                    Confidence: intelligenceConfidence,
                    EvidenceIds: intelligenceIds,
                    AsOfUtc: EnsureUtc(evidenceAsOf),
                    ExpiresUtc: fixtureStartUtc,
                    AdjustmentVersion: botProfile.FootballIntelligence.Version,
                    HistoricalEventObservationCount: 0,
                    Reason: "CONFIRMED_LINEUP_REVIEWED_WITHOUT_SEPARATE_ADJUSTMENT");
            }
        }
        var subjectKey = string.Join(
            "|",
            botProfile.Key.Trim().ToUpperInvariant(),
            logicalFixtureId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            odds.MarketType.Trim().ToUpperInvariant(),
            odds.LineValue.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
            candidate.SelectedSide.Trim().ToUpperInvariant(),
            odds.Source.Trim().ToUpperInvariant());

        return new RobustPickEvaluationInput
        {
            BotPickSelectionId = botPickSelectionId,
            SourceOddsSnapshotId = odds.OddsSnapshotId is > 0 ? odds.OddsSnapshotId : null,
            EvaluationSubjectKey = subjectKey,
            BotKey = botProfile.Key,
            MarketFamily = MarketFamily(odds.MarketType),
            MarketType = MapSelectionMarketType(odds.MarketType),
            SelectedSide = candidate.SelectedSide,
            League = odds.EffectiveLeague,
            HomeTeam = odds.EffectiveHomeTeam,
            AwayTeam = odds.EffectiveAwayTeam,
            Bookmaker = odds.Source,
            AutomationVersion = botProfile.AutomationVersion,
            FixtureId = logicalFixtureId,
            ExternalFixtureId = odds.ApiFootballFixtureId is > 0 ? odds.ApiFootballFixtureId : null,
            FixtureStartUtc = fixtureStartUtc,
            PredictionAsOfUtc = predictionAsOfUtc,
            EvaluationAsOfUtc = evaluationAsOfUtc,
            QuoteTimestampUtc = quoteTimestampUtc,
            Line = odds.LineValue,
            SelectedOdds = candidate.SelectedOdds,
            OverOdds = odds.SnapshotOverOdds is > 1m ? odds.SnapshotOverOdds : odds.OverOdds,
            UnderOdds = odds.SnapshotUnderOdds is > 1m ? odds.SnapshotUnderOdds : odds.UnderOdds,
            OriginalStake = originalStake,
            CurrentMinimumPointEdge = Convert.ToDecimal(Math.Max(0d, botProfile.MinEdge)),
            CurrentMinimumPointExpectedValue = Convert.ToDecimal(Math.Max(0d, botProfile.MinExpectedValue)),
            CurrentDecision = RobustCurrentSystemDecision.Bet,
            PrimaryPrediction = ToRobustDecimal(directPrediction),
            DirectPrediction = ToRobustDecimal(
                isTotalMarket
                    ? prediction.PredTotalDirect ?? prediction.PredictedTotalCorners
                    : directPrediction),
            HomePrediction = isTotalMarket ? ToNullableRobustDecimal(prediction.PredHomeCorners) : null,
            AwayPrediction = isTotalMarket ? ToNullableRobustDecimal(prediction.PredAwayCorners) : null,
            ContextPrediction = ToNullableRobustDecimal(
                ResolveContextPrediction(odds.MarketType, candidate.PredictionContext)),
            ConfiguredModelMae = ToNullableRobustDecimal(modelMae),
            RawProbability = ToRobustDecimal(rawProbability),
            CalibratedProbability = ToRobustDecimal(calibratedProbability),
            ProbabilityBeforeIntelligence = ToNullableRobustDecimal(ReadJsonDouble(
                decisionRoot,
                "featureSnapshot", "footballIntelligence", "probabilityBeforeFootballIntelligence")),
            ProbabilityLowerBound = ToRobustDecimal(probabilityLower),
            ProbabilityUpperBound = ToRobustDecimal(Math.Max(probabilityLower, probabilityUpper)),
            DataQualityScore = ToRobustDecimal(dataQuality),
            BaseModelVersion = baseModelVersion,
            ModelTrainedThroughUtc = BaseModelTrainedThrough(botProfile, prediction),
            SelectorVersion = botProfile.SelectorConfiguration?.ConfigurationVersion,
            CalibrationVersion = botProfile.SelectorConfiguration?.EmpiricalCalibration.Version,
            IntelligenceVersion = botProfile.FootballIntelligence.Version,
            CalibrationEffectiveN = ToNullableRobustDecimal(ReadJsonDouble(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "EffectiveSampleSize")),
            CalibrationExactMarketN = ReadJsonInt32(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "ExactMarketRows") ?? 0,
            CalibrationFamilyN = ReadJsonInt32(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "FamilyRows") ?? 0,
            CalibrationGlobalN = ReadJsonInt32(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "GlobalRows") ?? 0,
            CalibrationFallbackLevel = calibrationFallback,
            CalibrationError = ToNullableRobustDecimal(ReadJsonDouble(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "SourceBrierScore")),
            CalibrationPriorWeight = ToNullableRobustDecimal(ReadJsonDouble(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "PriorWeight")),
            CalibrationIntervalMethod = ReadJsonString(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "IntervalMethod"),
            CalibrationConfidenceLevel = ToNullableRobustDecimal(ReadJsonDouble(
                decisionRoot,
                "featureSnapshot", "empiricalCalibration", "result", "ConfidenceLevel")),
            ScenarioEvidence = scenarioEvidence,
            IntelligenceEvidenceStatus = intelligenceStatus,
            LineupStatus = !botProfile.FootballIntelligence.Enabled
                ? nameof(RobustEvidenceStatus.NotApplicable)
                : officialLineupAvailable == true
                    ? nameof(RobustEvidenceStatus.ReviewedNeutral)
                    : nameof(RobustEvidenceStatus.InsufficientEvidence),
            // General news/intelligence evidence is not proof that fatigue was
            // actually measured. Keep this provider explicitly unavailable until
            // a versioned, validated fatigue snapshot is wired into ScenarioEvidence.
            FatigueDataStatus = nameof(RobustEvidenceStatus.NotApplicable),
            GameStateModelStatus = nameof(RobustEvidenceStatus.NotApplicable),
            ActionableFactCount = actionableFactCount,
            IndependentSourceCount = independentSourceCount,
            IntelligenceSnapshotAgeMinutes = intelligenceSnapshotAgeMinutes
        };
    }

    private static object BuildIntelligenceEvidenceSummary(MatchIntelligenceSnapshotPair? snapshots)
    {
        var home = snapshots?.Home;
        var away = snapshots?.Away;
        return new
        {
            actionableFactCount = (home?.ActionableFactCount ?? 0) + (away?.ActionableFactCount ?? 0),
            independentSourceCount = Math.Max(home?.IndependentSourceCount ?? 0, away?.IndependentSourceCount ?? 0),
            snapshotAgeMinutes = Math.Max(home?.SnapshotAgeMinutes ?? 0, away?.SnapshotAgeMinutes ?? 0),
            officialLineupAvailable = home?.OfficialLineupAvailable == true || away?.OfficialLineupAvailable == true,
            asOfUtc = new[] { home?.CutoffAtUtc, away?.CutoffAtUtc }
                .Where(value => value.HasValue)
                .Select(value => EnsureUtc(value!.Value))
                .DefaultIfEmpty(DateTime.MinValue)
                .Max(),
            home = home is null ? null : new
            {
                home.ActionableFactCount,
                home.IndependentSourceCount,
                home.SnapshotAgeMinutes,
                home.OfficialLineupAvailable,
                home.OverallNewsConfidence,
                home.ConflictCount
            },
            away = away is null ? null : new
            {
                away.ActionableFactCount,
                away.IndependentSourceCount,
                away.SnapshotAgeMinutes,
                away.OfficialLineupAvailable,
                away.OverallNewsConfidence,
                away.ConflictCount
            }
        };
    }

    private static RobustEvidenceStatus ResolveRobustIntelligenceStatus(
        bool enabled,
        JsonElement? root)
    {
        if (!enabled)
        {
            return RobustEvidenceStatus.NotApplicable;
        }

        var isApplied = ReadJsonBoolean(
            root,
            "featureSnapshot", "footballIntelligence", "result", "IsApplied") ?? false;
        var adjustment = ReadJsonDouble(
            root,
            "featureSnapshot", "footballIntelligence", "result", "ProbabilityAdjustment") ?? 0d;
        if (isApplied)
        {
            return adjustment < 0d
                ? RobustEvidenceStatus.AppliedNegative
                : RobustEvidenceStatus.AppliedPositive;
        }

        var homeStatus = ReadJsonString(
            root,
            "featureSnapshot", "footballIntelligence", "result", "HomeEvidenceStatus");
        var awayStatus = ReadJsonString(
            root,
            "featureSnapshot", "footballIntelligence", "result", "AwayEvidenceStatus");
        var statuses = new[] { homeStatus, awayStatus }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (statuses.Any(value => value!.Equals("Stale", StringComparison.OrdinalIgnoreCase)
            || value.Equals("FutureCutoff", StringComparison.OrdinalIgnoreCase)))
        {
            return RobustEvidenceStatus.SnapshotExpired;
        }
        if (statuses.Any(value => value!.Equals("Available", StringComparison.OrdinalIgnoreCase)))
        {
            return RobustEvidenceStatus.ReviewedNeutral;
        }
        return statuses.Length > 0
            ? RobustEvidenceStatus.InsufficientEvidence
            : RobustEvidenceStatus.SourceUnavailable;
    }

    private static RobustCalibrationFallbackLevel ParseCalibrationFallback(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RobustCalibrationFallbackLevel.Unavailable;
        }
        if (value.Contains("Exact", StringComparison.OrdinalIgnoreCase))
        {
            return RobustCalibrationFallbackLevel.ExactMarket;
        }
        if (value.Contains("Family", StringComparison.OrdinalIgnoreCase))
        {
            return RobustCalibrationFallbackLevel.MarketFamily;
        }
        if (value.Contains("Global", StringComparison.OrdinalIgnoreCase))
        {
            return RobustCalibrationFallbackLevel.Global;
        }
        return RobustCalibrationFallbackLevel.Unavailable;
    }

    private static long ResolveLogicalFixtureId(UpcomingOddsRecord odds)
    {
        if (odds.ApiFootballFixtureId is > 0)
        {
            return odds.ApiFootballFixtureId.Value;
        }
        if (odds.PartidoProximoCuotaId > 0)
        {
            return odds.PartidoProximoCuotaId;
        }

        var identity = string.Join(
            "|",
            odds.MatchDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            odds.EffectiveLeague,
            odds.EffectiveHomeTeam,
            odds.EffectiveAwayTeam);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        var value = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long)))
            & long.MaxValue;
        return value == 0 ? 1 : value;
    }

    private static JsonElement? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetJsonPath(
        JsonElement? root,
        out JsonElement value,
        params string[] path)
    {
        value = default;
        if (!root.HasValue)
        {
            return false;
        }

        var current = root.Value;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var found = false;
            foreach (var property in current.EnumerateObject())
            {
                if (!property.Name.Equals(segment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                current = property.Value;
                found = true;
                break;
            }
            if (!found)
            {
                return false;
            }
        }
        value = current;
        return true;
    }

    private static double? ReadJsonDouble(JsonElement? root, params string[] path)
    {
        if (!TryGetJsonPath(root, out var value, path))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && double.IsFinite(number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out number)
            && double.IsFinite(number)
                ? number
                : null;
    }

    private static int? ReadJsonInt32(JsonElement? root, params string[] path)
    {
        if (!TryGetJsonPath(root, out var value, path))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }

    private static long? ReadJsonInt64(JsonElement? root, params string[] path)
    {
        if (!TryGetJsonPath(root, out var value, path))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }

    private static bool? ReadJsonBoolean(JsonElement? root, params string[] path)
    {
        if (!TryGetJsonPath(root, out var value, path))
        {
            return null;
        }
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }
        return value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out var result)
                ? result
                : null;
    }

    private static string? ReadJsonString(JsonElement? root, params string[] path)
    {
        if (!TryGetJsonPath(root, out var value, path))
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : value.ToString();
    }

    private static DateTime? ReadJsonDateTime(JsonElement? root, params string[] path)
    {
        var value = ReadJsonString(root, path);
        return DateTime.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var timestamp)
                ? timestamp
                : null;
    }

    private static double? ReadFeatureDouble(
        IReadOnlyDictionary<string, object?> features,
        string key)
    {
        if (!features.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }
        try
        {
            var converted = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(converted) ? converted : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static double FirstFinite(params double?[] values) =>
        values.First(value => value.HasValue && double.IsFinite(value.Value))!.Value;

    private static decimal ToRobustDecimal(double value) =>
        Convert.ToDecimal(double.IsFinite(value) ? value : 0d);

    private static decimal? ToNullableRobustDecimal(double? value) =>
        value.HasValue && double.IsFinite(value.Value)
            ? Convert.ToDecimal(value.Value)
            : null;

    private async Task<IReadOnlyList<PredictionBundle>> BuildNewGenerationPredictionBundlesAsync(
        IReadOnlyList<UpcomingOddsRecord> oddsRows,
        PredictionContextDto predictionContext,
        PredictionContextDto? swappedPredictionContext,
        IReadOnlyList<TeamBi3InfoDto> teamInfo,
        bool isNeutralMatch,
        IDictionary<string, NewGenerationBatchPredictionResult> predictionCache,
        CancellationToken cancellationToken)
    {
        if (oddsRows.Count == 0)
        {
            return Array.Empty<PredictionBundle>();
        }

        var representative = oddsRows[0];
        var normalPredictions = await GetNewGenerationPredictionsAsync(
            representative,
            swapTeams: false,
            predictionCache,
            cancellationToken);
        var swappedPredictions = isNeutralMatch && swappedPredictionContext is not null
            ? await GetNewGenerationPredictionsAsync(
                representative,
                swapTeams: true,
                predictionCache,
                cancellationToken)
            : null;
        var bundles = new List<PredictionBundle>(oddsRows.Count);

        foreach (var odds in oddsRows)
        {
            var features = _featureBuilder.Build(odds, predictionContext, teamInfo);
            var effectiveContext = predictionContext;
            var effectiveFeatures = features;
            if (isNeutralMatch && swappedPredictionContext is not null)
            {
                var swappedOdds = SwapMatchSides(odds);
                var swappedFeatures = _featureBuilder.Build(swappedOdds, swappedPredictionContext, teamInfo);
                effectiveFeatures = BuildNeutralFeatures(features, swappedFeatures);
                effectiveContext = BuildNeutralContext(predictionContext, swappedPredictionContext, odds);
            }

            bundles.Add(new PredictionBundle(
                odds,
                effectiveContext,
                BuildNewGenerationPrediction(odds, normalPredictions, swappedPredictions),
                OverUnderPrediction: null,
                effectiveFeatures,
                IsNeutralAdjusted: isNeutralMatch && swappedPredictions is not null));
        }

        return bundles;
    }

    private async Task<NewGenerationBatchPredictionResult> GetNewGenerationPredictionsAsync(
        UpcomingOddsRecord odds,
        bool swapTeams,
        IDictionary<string, NewGenerationBatchPredictionResult> predictionCache,
        CancellationToken cancellationToken)
    {
        var homeTeam = swapTeams ? odds.EffectiveAwayTeam : odds.EffectiveHomeTeam;
        var awayTeam = swapTeams ? odds.EffectiveHomeTeam : odds.EffectiveAwayTeam;
        var key = string.Join(
            "|",
            DateOnly.FromDateTime(odds.MatchDate).ToString("yyyy-MM-dd"),
            odds.EffectiveLeague.Trim().ToUpperInvariant(),
            homeTeam.Trim().ToUpperInvariant(),
            awayTeam.Trim().ToUpperInvariant());
        if (predictionCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var request = new NewGenerationPredictionRequest(
            odds.EffectiveLeague,
            Season: null,
            DateOnly.FromDateTime(odds.MatchDate),
            homeTeam,
            awayTeam,
            HomeFormation: null,
            AwayFormation: null,
            IsKnockout: false);
        var predictions = await _newGenerationPredictionService.PredictAllAsync(request, cancellationToken);
        predictionCache[key] = predictions;
        return predictions;
    }

    private PredictionResultDto BuildNewGenerationPrediction(
        UpcomingOddsRecord odds,
        NewGenerationBatchPredictionResult normal,
        NewGenerationBatchPredictionResult? swapped)
    {
        var targets = ResolveNewGenerationTargets(odds.MarketType);
        var selectedPrediction = GetRoleAdjustedPrediction(normal, swapped, targets.Selected);
        var homePrediction = GetRoleAdjustedPrediction(normal, swapped, targets.Home);
        var awayPrediction = GetRoleAdjustedPrediction(normal, swapped, targets.Away);
        var totalPrediction = GetRoleAdjustedPrediction(normal, swapped, targets.Total);
        var combinedPrediction = homePrediction.Value + awayPrediction.Value;
        var selectedValue = selectedPrediction.Value;
        var lineValue = Convert.ToDouble(odds.LineValue);
        var distance = Math.Abs(selectedValue - lineValue);
        var modelInfo = _newGenerationPredictionService.GetModelInfo(targets.Selected);
        var mae = modelInfo.TestMae is > 0 ? modelInfo.TestMae.Value : DefaultNewGenerationMae(odds.MarketType);
        var sigma = Math.Max(MinimumSigma(odds.MarketType), mae * 1.2533141373155d);
        var modelDifference = Math.Abs(totalPrediction.Value - combinedPrediction);

        return new PredictionResultDto
        {
            PredictedTotalCorners = selectedValue,
            PredTotalDirect = totalPrediction.Value,
            PredHomeCorners = homePrediction.Value,
            PredAwayCorners = awayPrediction.Value,
            PredTotalCombined = combinedPrediction,
            BettingLine = lineValue,
            DistanceToLine = distance,
            RecommendedSide = selectedValue >= lineValue ? "OVER" : "UNDER",
            Confidence = NewGenerationConfidence(distance, mae),
            Message = $"Models 2026 native CatBoost prediction ({selectedPrediction.Prediction.ModelVersion}).",
            ModelDifference = modelDifference,
            ModelConsensus = NewGenerationConsensus(modelDifference, mae),
            Mae = mae,
            Rmse = 0,
            ProbabilitySigma = sigma,
            ModelGeneration = "2026",
            ModelVersion = selectedPrediction.Prediction.ModelVersion,
            TrainedThrough = selectedPrediction.Prediction.TrainedThrough,
            FeatureSet = selectedPrediction.Prediction.FeatureSet,
            ModelWarnings = selectedPrediction.Prediction.Warnings
        };
    }

    private static RoleAdjustedPrediction GetRoleAdjustedPrediction(
        NewGenerationBatchPredictionResult normal,
        NewGenerationBatchPredictionResult? swapped,
        string target)
    {
        var normalPrediction = FindPrediction(normal, target);
        if (swapped is null)
        {
            return new RoleAdjustedPrediction(normalPrediction.PredictionClipped, normalPrediction);
        }

        var swappedPrediction = FindPrediction(swapped, SwapNewGenerationTarget(target));
        return new RoleAdjustedPrediction(
            AverageFinite(normalPrediction.PredictionClipped, swappedPrediction.PredictionClipped),
            normalPrediction);
    }

    private static NewGenerationPredictionResult FindPrediction(
        NewGenerationBatchPredictionResult batch,
        string target) => batch.Predictions.FirstOrDefault(prediction =>
            prediction.Target.Equals(target, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Models 2026 response did not include {target}.");

    private static NewGenerationTargets ResolveNewGenerationTargets(string marketType) => marketType switch
    {
        "CornersHomeTeam" => new(
            NewGenerationModelDefinitions.HomeCorners,
            NewGenerationModelDefinitions.HomeCorners,
            NewGenerationModelDefinitions.AwayCorners,
            NewGenerationModelDefinitions.TotalCorners),
        "CornersAwayTeam" => new(
            NewGenerationModelDefinitions.AwayCorners,
            NewGenerationModelDefinitions.HomeCorners,
            NewGenerationModelDefinitions.AwayCorners,
            NewGenerationModelDefinitions.TotalCorners),
        "CornersTotal" => new(
            NewGenerationModelDefinitions.TotalCorners,
            NewGenerationModelDefinitions.HomeCorners,
            NewGenerationModelDefinitions.AwayCorners,
            NewGenerationModelDefinitions.TotalCorners),
        "ShotsTotal" => new(
            NewGenerationModelDefinitions.TotalShots,
            NewGenerationModelDefinitions.HomeShots,
            NewGenerationModelDefinitions.AwayShots,
            NewGenerationModelDefinitions.TotalShots),
        "ShotsHomeTeam" => new(
            NewGenerationModelDefinitions.HomeShots,
            NewGenerationModelDefinitions.HomeShots,
            NewGenerationModelDefinitions.AwayShots,
            NewGenerationModelDefinitions.TotalShots),
        "ShotsAwayTeam" => new(
            NewGenerationModelDefinitions.AwayShots,
            NewGenerationModelDefinitions.HomeShots,
            NewGenerationModelDefinitions.AwayShots,
            NewGenerationModelDefinitions.TotalShots),
        "ShotsOnTargetTotal" => new(
            NewGenerationModelDefinitions.TotalShotsOnGoal,
            NewGenerationModelDefinitions.HomeShotsOnGoal,
            NewGenerationModelDefinitions.AwayShotsOnGoal,
            NewGenerationModelDefinitions.TotalShotsOnGoal),
        "ShotsOnTargetHomeTeam" => new(
            NewGenerationModelDefinitions.HomeShotsOnGoal,
            NewGenerationModelDefinitions.HomeShotsOnGoal,
            NewGenerationModelDefinitions.AwayShotsOnGoal,
            NewGenerationModelDefinitions.TotalShotsOnGoal),
        "ShotsOnTargetAwayTeam" => new(
            NewGenerationModelDefinitions.AwayShotsOnGoal,
            NewGenerationModelDefinitions.HomeShotsOnGoal,
            NewGenerationModelDefinitions.AwayShotsOnGoal,
            NewGenerationModelDefinitions.TotalShotsOnGoal),
        "GoalsTotal" => new(
            NewGenerationModelDefinitions.TotalGoals,
            NewGenerationModelDefinitions.HomeGoals,
            NewGenerationModelDefinitions.AwayGoals,
            NewGenerationModelDefinitions.TotalGoals),
        "GoalsHomeTeam" => new(
            NewGenerationModelDefinitions.HomeGoals,
            NewGenerationModelDefinitions.HomeGoals,
            NewGenerationModelDefinitions.AwayGoals,
            NewGenerationModelDefinitions.TotalGoals),
        "GoalsAwayTeam" => new(
            NewGenerationModelDefinitions.AwayGoals,
            NewGenerationModelDefinitions.HomeGoals,
            NewGenerationModelDefinitions.AwayGoals,
            NewGenerationModelDefinitions.TotalGoals),
        _ => throw new ArgumentException($"Models 2026 does not support odds market {marketType}.")
    };

    private static string SwapNewGenerationTarget(string target) => target switch
    {
        NewGenerationModelDefinitions.HomeCorners => NewGenerationModelDefinitions.AwayCorners,
        NewGenerationModelDefinitions.AwayCorners => NewGenerationModelDefinitions.HomeCorners,
        NewGenerationModelDefinitions.HomeShots => NewGenerationModelDefinitions.AwayShots,
        NewGenerationModelDefinitions.AwayShots => NewGenerationModelDefinitions.HomeShots,
        NewGenerationModelDefinitions.HomeShotsOnGoal => NewGenerationModelDefinitions.AwayShotsOnGoal,
        NewGenerationModelDefinitions.AwayShotsOnGoal => NewGenerationModelDefinitions.HomeShotsOnGoal,
        NewGenerationModelDefinitions.HomeGoals => NewGenerationModelDefinitions.AwayGoals,
        NewGenerationModelDefinitions.AwayGoals => NewGenerationModelDefinitions.HomeGoals,
        _ => target
    };

    private static double DefaultNewGenerationMae(string marketType) => marketType switch
    {
        "GoalsTotal" => 1.2790d,
        "GoalsHomeTeam" => 0.9290d,
        "GoalsAwayTeam" => 0.8157d,
        "ShotsOnTargetTotal" => 2.4802d,
        "ShotsOnTargetHomeTeam" => 1.8761d,
        "ShotsOnTargetAwayTeam" => 1.7004d,
        "ShotsTotal" => 4.6174d,
        "ShotsHomeTeam" => 3.8192d,
        "ShotsAwayTeam" => 3.4867d,
        "CornersHomeTeam" => 2.1858d,
        "CornersAwayTeam" => 1.9562d,
        _ => 2.6514d
    };

    private static double MinimumSigma(string marketType) => marketType switch
    {
        "GoalsTotal" or "GoalsHomeTeam" or "GoalsAwayTeam" => 0.90d,
        "ShotsTotal" or "ShotsHomeTeam" or "ShotsAwayTeam" => 2.0d,
        "ShotsOnTargetTotal" or "ShotsOnTargetHomeTeam" or "ShotsOnTargetAwayTeam" => 1.35d,
        _ => 0.95d
    };

    private static string NewGenerationConfidence(double distance, double mae)
    {
        var ratio = mae > 0 ? distance / mae : 0;
        return ratio switch
        {
            >= 1.0d => "VERY_HIGH",
            >= 0.55d => "HIGH",
            >= 0.30d => "MEDIUM",
            _ => "LOW"
        };
    }

    private static string NewGenerationConsensus(double difference, double mae)
    {
        var ratio = mae > 0 ? difference / mae : double.PositiveInfinity;
        return ratio switch
        {
            <= 0.35d => "HIGH",
            <= 0.75d => "MEDIUM",
            _ => "LOW"
        };
    }

    private async Task<PredictionBundle> BuildPredictionBundleAsync(
        UpcomingOddsRecord odds,
        PredictionContextDto predictionContext,
        PredictionContextDto? swappedPredictionContext,
        IReadOnlyList<TeamBi3InfoDto> teamInfo,
        bool isNeutralMatch,
        IDictionary<string, PredictionResultDto> predictionCache,
        CancellationToken cancellationToken)
    {
        var features = _featureBuilder.Build(odds, predictionContext, teamInfo);
        var normalCacheKey = $"normal|{LegacyPredictionGroup(odds.MarketType)}";
        if (!predictionCache.TryGetValue(normalCacheKey, out var cachedNormalPrediction))
        {
            cachedNormalPrediction = await PredictMarketAsync(odds, features, cancellationToken);
            predictionCache[normalCacheKey] = cachedNormalPrediction;
        }
        var cornersPrediction = AdaptPredictionToOdds(cachedNormalPrediction, odds);
        var overUnderPrediction = _options.EnableOverUnderPrediction && IsCornersMarket(odds.MarketType)
            ? await _predictionApiClient.PredictOverUnderAsync(features, cancellationToken)
            : null;

        if (!isNeutralMatch || swappedPredictionContext is null)
        {
            return new PredictionBundle(
                odds,
                predictionContext,
                cornersPrediction,
                overUnderPrediction,
                features,
                false);
        }

        var swappedOdds = SwapMatchSides(odds);
        var swappedFeatures = _featureBuilder.Build(swappedOdds, swappedPredictionContext, teamInfo);
        var swappedCacheKey = $"swapped|{LegacyPredictionGroup(swappedOdds.MarketType)}";
        if (!predictionCache.TryGetValue(swappedCacheKey, out var cachedSwappedPrediction))
        {
            cachedSwappedPrediction = await PredictMarketAsync(swappedOdds, swappedFeatures, cancellationToken);
            predictionCache[swappedCacheKey] = cachedSwappedPrediction;
        }
        var swappedCornersPrediction = AdaptPredictionToOdds(cachedSwappedPrediction, swappedOdds);
        var swappedOverUnderPrediction = _options.EnableOverUnderPrediction && IsCornersMarket(swappedOdds.MarketType)
            ? await _predictionApiClient.PredictOverUnderAsync(swappedFeatures, cancellationToken)
            : null;
        // Neutral matches do not have real home advantage, so blend both role directions.
        var neutralFeatures = BuildNeutralFeatures(features, swappedFeatures);
        var neutralContext = BuildNeutralContext(predictionContext, swappedPredictionContext, odds);
        var neutralCornersPrediction = BuildNeutralCornersPrediction(cornersPrediction, swappedCornersPrediction, odds);
        var neutralOverUnderPrediction = BuildNeutralOverUnderPrediction(overUnderPrediction, swappedOverUnderPrediction, odds);

        return new PredictionBundle(
            odds,
            neutralContext,
            neutralCornersPrediction,
            neutralOverUnderPrediction,
            neutralFeatures,
            true);
    }

    private async Task<PredictionResultDto> PredictMarketAsync(
        UpcomingOddsRecord odds,
        IReadOnlyDictionary<string, object?> features,
        CancellationToken cancellationToken)
    {
        if (IsCornersMarket(odds.MarketType))
        {
            return await _predictionApiClient.PredictCornersAsync(features, cancellationToken);
        }

        var multiMarket = await _predictionApiClient.PredictMultiMarketAsync(features, cancellationToken);
        var market = odds.MarketType switch
        {
            "GoalsTotal" or "GoalsHomeTeam" or "GoalsAwayTeam" => multiMarket.Goals,
            "ShotsTotal" or "ShotsHomeTeam" or "ShotsAwayTeam" => multiMarket.Shots,
            "ShotsOnTargetTotal" or "ShotsOnTargetHomeTeam" or "ShotsOnTargetAwayTeam" => multiMarket.ShotsOnGoal,
            _ => null
        } ?? throw new InvalidOperationException($"Prediction response did not include market {odds.MarketType}.");

        var (mae, rmse, modelVersion) = LegacyModelMetrics(odds.MarketType);
        var totalPrediction = double.IsFinite(market.FinalPrediction)
            ? market.FinalPrediction
            : market.Prediction;
        var predictedValue = odds.MarketType switch
        {
            "GoalsHomeTeam" or "ShotsHomeTeam" or "ShotsOnTargetHomeTeam" => market.HomePrediction ?? totalPrediction,
            "GoalsAwayTeam" or "ShotsAwayTeam" or "ShotsOnTargetAwayTeam" => market.AwayPrediction ?? totalPrediction,
            _ => totalPrediction
        };
        var line = Convert.ToDouble(odds.LineValue);

        return new PredictionResultDto
        {
            PredictedTotalCorners = totalPrediction,
            PredTotalDirect = market.TotalDirectPrediction,
            PredHomeCorners = market.HomePrediction,
            PredAwayCorners = market.AwayPrediction,
            PredTotalCombined = market.CombinedHomeAwayPrediction,
            BettingLine = line,
            DistanceToLine = Math.Abs(predictedValue - line),
            RecommendedSide = predictedValue >= line ? "OVER" : "UNDER",
            Confidence = market.Confidence,
            Message = $"{MapSelectionMarketType(odds.MarketType)} model prediction.",
            Mae = mae,
            Rmse = rmse,
            ProbabilitySigma = rmse,
            ModelGeneration = "Legacy",
            ModelVersion = modelVersion,
            TrainedThrough = "2026-06-11T16:36:16Z",
            FeatureSet = "legacy-pre-2026-deployment"
        };
    }

    private static PredictionResultDto AdaptPredictionToOdds(PredictionResultDto value, UpcomingOddsRecord odds)
    {
        var predicted = ResolveBasePredictedValue(value, odds.MarketType);
        var line = Convert.ToDouble(odds.LineValue);
        var isCorners = IsCornersMarket(odds.MarketType);
        var metrics = isCorners
            ? (value.Mae, value.Rmse, value.ModelVersion ?? "legacy-corners-filtered-v1")
            : LegacyModelMetrics(odds.MarketType);
        return new PredictionResultDto
        {
            PredictedTotalCorners = predicted,
            PredTotalDirect = value.PredTotalDirect,
            PredHomeCorners = value.PredHomeCorners,
            PredAwayCorners = value.PredAwayCorners,
            PredTotalCombined = value.PredTotalCombined,
            BettingLine = line,
            DistanceToLine = Math.Abs(predicted - line),
            RecommendedSide = predicted >= line ? "OVER" : "UNDER",
            Confidence = value.Confidence,
            Message = value.Message,
            LegacyTotalCorners = value.LegacyTotalCorners,
            ModelDifference = value.ModelDifference,
            ModelConsensus = value.ModelConsensus,
            Mae = metrics.Item1,
            Rmse = metrics.Item2,
            ProbabilitySigma = metrics.Item2,
            ModelGeneration = value.ModelGeneration,
            ModelVersion = metrics.Item3,
            TrainedThrough = value.TrainedThrough ?? "2026-06-11T16:36:16Z",
            FeatureSet = value.FeatureSet ?? "legacy-pre-2026-deployment",
            ModelWarnings = value.ModelWarnings
        };
    }

    private static double ResolveBasePredictedValue(PredictionResultDto prediction, string marketType) => marketType switch
    {
        "CornersHomeTeam" or "GoalsHomeTeam" or "ShotsHomeTeam" or "ShotsOnTargetHomeTeam" =>
            prediction.PredHomeCorners ?? prediction.PredictedTotalCorners,
        "CornersAwayTeam" or "GoalsAwayTeam" or "ShotsAwayTeam" or "ShotsOnTargetAwayTeam" =>
            prediction.PredAwayCorners ?? prediction.PredictedTotalCorners,
        _ => prediction.PredictedTotalCorners
    };

    private static string LegacyPredictionGroup(string marketType) => marketType switch
    {
        "CornersTotal" or "CornersHomeTeam" or "CornersAwayTeam" => "CORNERS",
        "GoalsTotal" or "GoalsHomeTeam" or "GoalsAwayTeam" => "GOALS",
        "ShotsTotal" or "ShotsHomeTeam" or "ShotsAwayTeam" => "SHOTS",
        _ => "SOG"
    };

    private static (double Mae, double Rmse, string Version) LegacyModelMetrics(string marketType) => marketType switch
    {
        "GoalsHomeTeam" => (0.9506d, 1.2843d, "goals_v1"),
        "GoalsAwayTeam" => (0.8302d, 1.1319d, "goals_v1"),
        "GoalsTotal" => (1.3131d, 1.6761d, "goals_v1"),
        "ShotsHomeTeam" => (3.8300d, 4.9159d, "shots_v3_catboost"),
        "ShotsAwayTeam" => (3.4209d, 4.3622d, "shots_v3_catboost"),
        "ShotsTotal" => (4.5572d, 5.7584d, "shots_v3_catboost"),
        _ => (2.6060d, 3.2945d, "sog_v1")
    };

    private static bool IsCornersMarket(string marketType) =>
        marketType is "CornersTotal" or "CornersHomeTeam" or "CornersAwayTeam";

    private static bool IsNewGenerationOnlyMarket(string marketType) => marketType is
        "GoalsHomeTeam" or "GoalsAwayTeam" or
        "ShotsTotal" or "ShotsHomeTeam" or "ShotsAwayTeam" or
        "ShotsOnTargetHomeTeam" or "ShotsOnTargetAwayTeam";

    private static PredictionContextDto BuildNeutralContext(
        PredictionContextDto normalContext,
        PredictionContextDto swappedContext,
        UpcomingOddsRecord odds)
    {
        var enrichedPrediction = AverageFinite(
            normalContext.Comparison.EnrichedPrediction,
            swappedContext.Comparison.EnrichedPrediction);
        var enrichedShotsOnGoalPrediction = AverageFinite(
            normalContext.Comparison.EnrichedShotsOnGoalPrediction,
            swappedContext.Comparison.EnrichedShotsOnGoalPrediction);
        var enrichedGoalsPrediction = AverageFinite(
            normalContext.Comparison.EnrichedGoalsPrediction,
            swappedContext.Comparison.EnrichedGoalsPrediction);
        var recommendation = enrichedPrediction >= Convert.ToDouble(odds.LineValue) ? "Over" : "Under";

        return new PredictionContextDto(
            new PredictionComparisonDto(
                enrichedPrediction,
                null,
                recommendation,
                enrichedShotsOnGoalPrediction,
                enrichedGoalsPrediction,
                AverageFinite(normalContext.Comparison.HomeExpectedShotsOnGoal, swappedContext.Comparison.AwayExpectedShotsOnGoal),
                AverageFinite(normalContext.Comparison.AwayExpectedShotsOnGoal, swappedContext.Comparison.HomeExpectedShotsOnGoal),
                AverageFinite(normalContext.Comparison.HomeExpectedGoals, swappedContext.Comparison.AwayExpectedGoals),
                AverageFinite(normalContext.Comparison.AwayExpectedGoals, swappedContext.Comparison.HomeExpectedGoals),
                AverageFinite(
                    normalContext.Comparison.EnrichedShotsPrediction,
                    swappedContext.Comparison.EnrichedShotsPrediction),
                AverageFinite(normalContext.Comparison.HomeExpectedShots, swappedContext.Comparison.AwayExpectedShots),
                AverageFinite(normalContext.Comparison.AwayExpectedShots, swappedContext.Comparison.HomeExpectedShots)),
            normalContext.HomeGeneralMatches,
            normalContext.HomeAsHomeMatches,
            normalContext.AwayGeneralMatches,
            normalContext.AwayAsAwayMatches);
    }

    private static PredictionResultDto BuildNeutralCornersPrediction(
        PredictionResultDto normalPrediction,
        PredictionResultDto swappedPrediction,
        UpcomingOddsRecord odds)
    {
        var predictedTotal = AverageFinite(normalPrediction.PredictedTotalCorners, swappedPrediction.PredictedTotalCorners);
        var predTotalDirect = AverageNullable(normalPrediction.PredTotalDirect, swappedPrediction.PredTotalDirect);
        var predTotalCombined = AverageNullable(normalPrediction.PredTotalCombined, swappedPrediction.PredTotalCombined);
        var predHomeCorners = AverageNullable(normalPrediction.PredHomeCorners, swappedPrediction.PredAwayCorners);
        var predAwayCorners = AverageNullable(normalPrediction.PredAwayCorners, swappedPrediction.PredHomeCorners);
        var distanceToLine = Math.Abs(predictedTotal - Convert.ToDouble(odds.LineValue));

        return new PredictionResultDto
        {
            PredictedTotalCorners = predictedTotal,
            PredTotalDirect = predTotalDirect,
            PredHomeCorners = predHomeCorners,
            PredAwayCorners = predAwayCorners,
            PredTotalCombined = predTotalCombined,
            BettingLine = normalPrediction.BettingLine ?? swappedPrediction.BettingLine,
            DistanceToLine = distanceToLine,
            RecommendedSide = predictedTotal >= Convert.ToDouble(odds.LineValue) ? "Over" : "Under",
            Confidence = LowerConfidence(normalPrediction.Confidence, swappedPrediction.Confidence),
            Message = "Neutral-field blend: averaged normal and role-swapped predictions.",
            LegacyTotalCorners = AverageNullable(normalPrediction.LegacyTotalCorners, swappedPrediction.LegacyTotalCorners),
            ModelDifference = AverageNullable(normalPrediction.ModelDifference, swappedPrediction.ModelDifference),
            ModelConsensus = LowerConsensus(normalPrediction.ModelConsensus, swappedPrediction.ModelConsensus),
            Mae = AverageFinite(normalPrediction.Mae, swappedPrediction.Mae),
            Rmse = AverageFinite(normalPrediction.Rmse, swappedPrediction.Rmse)
        };
    }

    private static OverUnderPredictionResultDto? BuildNeutralOverUnderPrediction(
        OverUnderPredictionResultDto? normalPrediction,
        OverUnderPredictionResultDto? swappedPrediction,
        UpcomingOddsRecord odds)
    {
        if (normalPrediction is null && swappedPrediction is null)
        {
            return null;
        }

        if (normalPrediction is null)
        {
            return swappedPrediction;
        }

        if (swappedPrediction is null)
        {
            return normalPrediction;
        }

        var overProbability = AverageNullable(normalPrediction.OverProbability, swappedPrediction.OverProbability);
        var underProbability = AverageNullable(normalPrediction.UnderProbability, swappedPrediction.UnderProbability);
        var prediction = (overProbability ?? 0) >= (underProbability ?? 0) ? "Over" : "Under";

        return new OverUnderPredictionResultDto
        {
            BettingLine = Convert.ToDouble(odds.LineValue),
            Prediction = prediction,
            PredictedClass = prediction == "Over" ? 1 : 0,
            OverProbability = overProbability,
            UnderProbability = underProbability,
            Confidence = LowerConfidence(normalPrediction.Confidence, swappedPrediction.Confidence),
            DistanceToLine = AverageFinite(normalPrediction.DistanceToLine, swappedPrediction.DistanceToLine)
        };
    }

    private static Dictionary<string, object?> BuildNeutralFeatures(
        Dictionary<string, object?> normalFeatures,
        Dictionary<string, object?> swappedFeatures)
    {
        var result = new Dictionary<string, object?>(normalFeatures, StringComparer.Ordinal)
        {
            ["NeutralFieldAdjustment"] = 1
        };

        BlendFeaturePair(result, normalFeatures, swappedFeatures, "HomeCornersPowerLast5", "AwayCornersPowerLast5");
        BlendFeaturePair(result, normalFeatures, swappedFeatures, "HomeShotsPowerLast5", "AwayShotsPowerLast5");
        BlendFeaturePair(result, normalFeatures, swappedFeatures, "HomeShotsOnGoalPowerLast5", "AwayShotsOnGoalPowerLast5");
        BlendFeaturePair(result, normalFeatures, swappedFeatures, "HomeGoalsPowerLast5", "AwayGoalsPowerLast5");

        result["ExpectedTotalCornersPowerLast5"] = RoundNeutralFeature(
            ToFeatureDouble(result, "HomeCornersPowerLast5", 0) + ToFeatureDouble(result, "AwayCornersPowerLast5", 0));

        return result;
    }

    private static void BlendFeaturePair(
        IDictionary<string, object?> target,
        IReadOnlyDictionary<string, object?> normalFeatures,
        IReadOnlyDictionary<string, object?> swappedFeatures,
        string homeKey,
        string awayKey)
    {
        target[homeKey] = AverageFinite(
            ToFeatureDouble(normalFeatures, homeKey, 0),
            ToFeatureDouble(swappedFeatures, awayKey, 0));
        target[awayKey] = AverageFinite(
            ToFeatureDouble(normalFeatures, awayKey, 0),
            ToFeatureDouble(swappedFeatures, homeKey, 0));
    }

    private static UpcomingOddsRecord SwapMatchSides(UpcomingOddsRecord odds) =>
        odds with
        {
            HomeTeam = odds.AwayTeam,
            AwayTeam = odds.HomeTeam,
            StandardizedHomeTeam = odds.StandardizedAwayTeam,
            StandardizedAwayTeam = odds.StandardizedHomeTeam,
            HomeTeamGender = odds.AwayTeamGender,
            AwayTeamGender = odds.HomeTeamGender
        };

    private static (AutomatedSelectionCandidate? candidate, string? reason) EvaluateCandidate(
        UpcomingOddsRecord odds,
        PredictionContextDto context,
        PredictionResultDto cornersPrediction,
        OverUnderPredictionResultDto? overUnderPrediction,
        Dictionary<string, object?> features,
        BotVariantProfile botProfile,
        bool isNeutralAdjusted,
        MatchIntelligenceSnapshotPair? intelligenceSnapshot)
    {
        var marketSnapshot = ResolveMarketSnapshot(odds, cornersPrediction, features, context);
        if (marketSnapshot is null)
        {
            return (null, $"Line {odds.LineValue:0.0}: no projected value was available for market {odds.MarketType}.");
        }

        var lineValue = Convert.ToDouble(odds.LineValue);
        var distanceToLine = Math.Abs(marketSnapshot.PredictedValue - lineValue);
        if (distanceToLine < botProfile.MinDistanceToLine)
        {
            return (null, $"Line {lineValue:0.0}: distance to line {distanceToLine:0.00} was below the threshold.");
        }

        var contextPrediction = marketSnapshot.ContextValue;
        var contextDifference = Math.Abs(contextPrediction - marketSnapshot.PredictedValue);
        if (contextDifference > botProfile.MaxContextDifference)
        {
            return (null, $"Line {lineValue:0.0}: context difference {contextDifference:0.00} was too high.");
        }

        var cornersSide = marketSnapshot.CornersSide;
        var overUnderSide = marketSnapshot.AllowOverUnderModel
            ? NormalizeSide(overUnderPrediction?.Prediction)
            : null;
        if (cornersSide is not null && overUnderSide is not null && cornersSide != overUnderSide && !botProfile.AllowModelDisagreement)
        {
            return (null, $"Line {lineValue:0.0}: corners model and over/under model disagreed.");
        }

        var sigma = marketSnapshot.Sigma;
        var approximateOverProbability = 1d - StandardNormalDistribution.Cdf(lineValue, marketSnapshot.PredictedValue, sigma);
        var approximateUnderProbability = 1d - approximateOverProbability;

        string? selectedSide = overUnderSide ?? cornersSide;
        if (selectedSide is null)
        {
            var bestOverEv = odds.OverOdds is > 1 ? approximateOverProbability * (double)odds.OverOdds.Value - 1d : double.NegativeInfinity;
            var bestUnderEv = odds.UnderOdds is > 1 ? approximateUnderProbability * (double)odds.UnderOdds.Value - 1d : double.NegativeInfinity;
            selectedSide = bestOverEv >= bestUnderEv ? "Over" : "Under";
        }

        var selectedOdds = selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase) ? odds.OverOdds : odds.UnderOdds;
        if (selectedOdds is null || selectedOdds <= 1)
        {
            return (null, $"Line {lineValue:0.0}: there was no usable {selectedSide} odds.");
        }

        if (botProfile.MinOddsExclusive is double minOddsExclusive && (double)selectedOdds.Value <= minOddsExclusive)
        {
            return (null, $"Line {lineValue:0.0}: selected odds {selectedOdds:0.00} were not greater than {minOddsExclusive:0.00}.");
        }

        var probabilityBeforeFootballIntelligence = selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            ? overUnderPrediction?.OverProbability ?? approximateOverProbability
            : overUnderPrediction?.UnderProbability ?? approximateUnderProbability;
        var predictionTimestampUtc = DateTime.UtcNow;
        var footballIntelligence = FootballIntelligenceAdjustmentCalculator.Calculate(
            predictionTimestampUtc,
            marketSnapshot.SelectionMarketType,
            selectedSide,
            probabilityBeforeFootballIntelligence,
            intelligenceSnapshot,
            botProfile.FootballIntelligence);
        var modelProbability = footballIntelligence.ProbabilityAfter;

        var impliedProbability = 1d / (double)selectedOdds.Value;
        var probabilityEdge = modelProbability - impliedProbability;
        var expectedValueBeforeFootballIntelligence =
            probabilityBeforeFootballIntelligence * (double)selectedOdds.Value - 1d;
        var expectedValue = modelProbability * (double)selectedOdds.Value - 1d;
        var kellyFraction = CalculateKellyFraction(modelProbability, (double)selectedOdds.Value);
        var minimumLiftedProbability = impliedProbability * (1d + botProfile.MinProbabilityLiftOverImplied);

        if (modelProbability < minimumLiftedProbability)
        {
            return (null, $"Line {lineValue:0.0}: model probability {modelProbability:0.000} was below conservative probability floor {minimumLiftedProbability:0.000}.");
        }

        if (probabilityEdge < botProfile.MinEdge)
        {
            return (null, $"Line {lineValue:0.0}: edge {probabilityEdge:0.000} was below the threshold.");
        }

        if (expectedValue < botProfile.MinExpectedValue)
        {
            return (null, $"Line {lineValue:0.0}: EV {expectedValue:0.000} was below the threshold.");
        }

        var agreementBonus = cornersSide is not null && overUnderSide is not null && cornersSide == overUnderSide ? 0.12 : 0;
        var disagreementPenalty = cornersSide is not null && overUnderSide is not null && cornersSide != overUnderSide ? 0.07 : 0;
        var score = expectedValue
            + probabilityEdge
            + Math.Min(distanceToLine / 5d, 0.20)
            + ConfidenceWeight(cornersPrediction.Confidence)
            + ConfidenceWeight(overUnderPrediction?.Confidence) / 2d
            + ConsensusWeight(cornersPrediction.ModelConsensus)
            + agreementBonus
            - disagreementPenalty
            - Math.Min(contextDifference / 10d, 0.20);

        var decisionReason = JsonSerializer.Serialize(new
        {
            botProfile = botProfile.Key,
            botProfile.DisplayName,
            automationVersion = botProfile.AutomationVersion,
            isNeutralAdjusted,
            league = odds.EffectiveLeague,
            homeTeam = odds.EffectiveHomeTeam,
            awayTeam = odds.EffectiveAwayTeam,
            matchDate = odds.MatchDate,
            sourceMarketType = odds.MarketType,
            selectionMarketType = marketSnapshot.SelectionMarketType,
            lineValue = odds.LineValue,
            selectedSide,
            selectedOdds,
            modelProbability,
            impliedProbability,
            probabilityEdge,
            expectedValue,
            kellyFraction,
            thresholds = new
            {
                botProfile.MinEdge,
                botProfile.MinExpectedValue,
                botProfile.MinDistanceToLine,
                botProfile.MaxContextDifference,
                botProfile.AllowModelDisagreement,
                botProfile.MinOddsExclusive,
                botProfile.MinProbabilityLiftOverImplied,
                botProfile.StakeMultiplier
            },
            distanceToLine,
            contextPrediction,
            contextDifference,
            featureSnapshot = new
            {
                predictionTimestampUtc,
                footballIntelligence = new
                {
                    enabled = botProfile.FootballIntelligence.Enabled,
                    botProfile.FootballIntelligence.Version,
                    probabilityBeforeFootballIntelligence,
                    expectedValueBeforeFootballIntelligence,
                    result = footballIntelligence
                },
                intelligenceEvidence = BuildIntelligenceEvidenceSummary(intelligenceSnapshot),
                configuration = new
                {
                    footballIntelligence = botProfile.FootballIntelligence
                }
            },
            cornersModel = new
            {
                cornersPrediction.PredictedTotalCorners,
                cornersPrediction.PredTotalDirect,
                cornersPrediction.PredHomeCorners,
                cornersPrediction.PredAwayCorners,
                cornersPrediction.PredTotalCombined,
                cornersPrediction.RecommendedSide,
                cornersPrediction.Confidence,
                cornersPrediction.ModelConsensus,
                cornersPrediction.Message,
                cornersPrediction.ModelGeneration,
                cornersPrediction.ModelVersion,
                cornersPrediction.TrainedThrough,
                cornersPrediction.FeatureSet,
                cornersPrediction.ModelWarnings
            },
            overUnderModel = overUnderPrediction is null
                ? null
                : new
                {
                    overUnderPrediction.Prediction,
                    overUnderPrediction.OverProbability,
                    overUnderPrediction.UnderProbability,
                    overUnderPrediction.Confidence,
                    overUnderPrediction.DistanceToLine
                }
        });

        return (new AutomatedSelectionCandidate
        {
            Odds = odds,
            CornersPrediction = cornersPrediction,
            OverUnderPrediction = overUnderPrediction,
            PredictionContext = context,
            Features = features,
            SelectedSide = selectedSide,
            SelectedOdds = selectedOdds.Value,
            ModelProbability = modelProbability,
            ImpliedProbability = impliedProbability,
            ProbabilityEdge = probabilityEdge,
            ExpectedValue = expectedValue,
            KellyFraction = kellyFraction,
            DistanceToLine = distanceToLine,
            ContextDifference = contextDifference,
            SelectionScore = score,
            DecisionReason = decisionReason,
            SelectionStatus = "Pending"
        }, null);
    }

    private static MatchIntelligenceSnapshotPair? ResolveIntelligenceSnapshot(
        IEnumerable<UpcomingOddsRecord> rows,
        IReadOnlyDictionary<long, MatchIntelligenceSnapshotPair> snapshotsByFixture)
    {
        foreach (var fixtureId in rows
                     .Where(row => row.ApiFootballFixtureId is > 0)
                     .Select(row => row.ApiFootballFixtureId!.Value)
                     .Distinct())
        {
            if (snapshotsByFixture.TryGetValue(fixtureId, out var snapshot))
                return snapshot;
        }

        return null;
    }

    private BotCEvaluation EvaluateBotCCandidate(
        PredictionBundle bundle,
        BotVariantProfile botProfile,
        IReadOnlyDictionary<string, IReadOnlyList<BotECalibrationObservation>> calibrationHistoryBySourceBot,
        IReadOnlyDictionary<long, MatchIntelligenceSnapshotPair> intelligenceSnapshotsByFixture)
    {
        var configuration = botProfile.SelectorConfiguration
            ?? throw new InvalidOperationException($"Bot {botProfile.Key} has no Pick Selector configuration.");
        var prediction = bundle.CornersPrediction;
        var basePredictedValue = ResolveBasePredictedValue(prediction, bundle.Odds.MarketType);
        var baseModelName = BaseModelName(botProfile);
        var baseModelVersion = BaseModelVersion(botProfile, prediction, bundle.Odds.MarketType);
        var sigma = prediction.ProbabilitySigma
            ?? Math.Max(MinimumSigma(bundle.Odds.MarketType), prediction.Mae > 0 ? prediction.Mae : 1d);
        var input = new BotCPickEvaluationInput(
            MapSelectionMarketType(bundle.Odds.MarketType),
            bundle.Odds.LineValue,
            bundle.Odds.OverOdds,
            bundle.Odds.UnderOdds,
            EnsureUtc(bundle.Odds.OddsCapturedAtUtc ?? bundle.Odds.UpdatedAtUtc),
            ToUtcFromSantiago(bundle.Odds.MatchDate),
            basePredictedValue,
            sigma,
            baseModelName,
            baseModelVersion,
            ToBotCHistory(bundle.PredictionContext.HomeGeneralMatches, bundle.Odds.EffectiveHomeTeam, bundle.Odds.MarketType),
            ToBotCHistory(bundle.PredictionContext.HomeAsHomeMatches, bundle.Odds.EffectiveHomeTeam, bundle.Odds.MarketType),
            ToBotCHistory(bundle.PredictionContext.AwayGeneralMatches, bundle.Odds.EffectiveAwayTeam, bundle.Odds.MarketType),
            ToBotCHistory(bundle.PredictionContext.AwayAsAwayMatches, bundle.Odds.EffectiveAwayTeam, bundle.Odds.MarketType),
            CrossMarketPredictionAvailable: false,
            BaseModelTrainedThroughUtc: BaseModelTrainedThrough(botProfile, prediction),
            HomeTeam: bundle.Odds.EffectiveHomeTeam,
            AwayTeam: bundle.Odds.EffectiveAwayTeam,
            TeamStrengthHistory: ToBotDHistory(
                bundle.PredictionContext.HomeGeneralMatches
                    .Concat(bundle.PredictionContext.AwayGeneralMatches)),
            CalibrationHistory: configuration.EmpiricalCalibration.Enabled
                && calibrationHistoryBySourceBot.TryGetValue(
                    configuration.EmpiricalCalibration.SourceBotKey,
                    out var calibrationHistory)
                    ? calibrationHistory
                    : Array.Empty<BotECalibrationObservation>(),
            FootballIntelligenceSnapshot: configuration.FootballIntelligence.Enabled
                && bundle.Odds.ApiFootballFixtureId.HasValue
                && intelligenceSnapshotsByFixture.TryGetValue(
                    bundle.Odds.ApiFootballFixtureId.Value,
                    out var intelligenceSnapshot)
                    ? intelligenceSnapshot
                    : null,
            PredictionTimestampUtc: DateTime.UtcNow);
        var decision = _botCPickDecisionEngine.Evaluate(input, configuration);

        _logger.LogDebug(
            "Selector candidate evaluated. BotKey={BotKey}, BaseModel={BaseModel}, Match={HomeTeam} vs {AwayTeam}, Market={Market}, Line={Line}, Decision={Decision}, Engine={Engine}, Edge={Edge:0.0000}, EV={ExpectedValue:0.0000}, Quality={Quality:0.0000}, Agreement={Agreement:0.0000}, FeatureSchema={FeatureSchema}",
            botProfile.Key,
            baseModelName,
            bundle.Odds.EffectiveHomeTeam,
            bundle.Odds.EffectiveAwayTeam,
            bundle.Odds.MarketType,
            bundle.Odds.LineValue,
            decision.Decision,
            decision.DecisionEngineType,
            decision.FinalEdge,
            decision.FinalExpectedValue,
            decision.DataQualityScore,
            decision.ContextAgreementScore,
            decision.FeatureSchemaVersion);

        if (!decision.Decision.Equals("Approved", StringComparison.Ordinal))
        {
            return new BotCEvaluation(bundle, decision, null);
        }

        var decisionReason = JsonSerializer.Serialize(new
        {
            botProfile = botProfile.Key,
            botProfile.DisplayName,
            automationVersion = botProfile.AutomationVersion,
            strategy = configuration.EmpiricalCalibration.Enabled
                ? "Pick Selector 2026 · Empirical Market Calibration"
                : configuration.TeamStrength.Enabled
                    ? "Pick Selector 2026 · Team Strength Gap"
                    : "Pick Selector 2026",
            decision = decision.Decision,
            decision.DecisionEngineType,
            decision.FeatureSchemaVersion,
            decision.ConfigurationVersion,
            decision.BaseRawProbability,
            decision.BaseCalibratedProbability,
            decision.MarketNoVigProbability,
            decision.FinalProbability,
            probabilityEdge = decision.FinalEdge,
            expectedValue = decision.FinalExpectedValue,
            decision.RuleBasedConfidenceScore,
            contextPrediction = decision.ContextExpectedValue,
            decision.ContextAgreementScore,
            decision.DataQualityScore,
            decision.DecisionReasons,
            decision.RiskFlags,
            decision.Summary,
            featureSnapshot = JsonSerializer.Deserialize<JsonElement>(decision.FeatureSnapshotJson),
            intelligenceEvidence = BuildIntelligenceEvidenceSummary(input.FootballIntelligenceSnapshot),
            model = new
            {
                name = baseModelName,
                version = baseModelVersion,
                trainedThrough = BaseModelTrainedThrough(botProfile, prediction),
                prediction.FeatureSet,
                prediction.ModelWarnings
            }
        });
        var modelProbability = decision.FinalProbability;
        var selectedOdds = decision.SelectedOdds
            ?? throw new InvalidOperationException("An approved Bot C decision must have selected odds.");
        var candidate = new AutomatedSelectionCandidate
        {
            Odds = bundle.Odds,
            CornersPrediction = prediction,
            OverUnderPrediction = null,
            PredictionContext = bundle.PredictionContext,
            Features = bundle.Features,
            SelectedSide = decision.SelectedSide,
            SelectedOdds = selectedOdds,
            ModelProbability = modelProbability,
            ImpliedProbability = decision.RawImpliedProbability,
            ProbabilityEdge = decision.FinalEdge,
            ExpectedValue = decision.FinalExpectedValue,
            KellyFraction = CalculateKellyFraction(modelProbability, Convert.ToDouble(selectedOdds)),
            DistanceToLine = Math.Abs(decision.BaseLineMargin),
            ContextDifference = Math.Abs(basePredictedValue - decision.ContextExpectedValue),
            SelectionScore = decision.SelectionScore,
            DecisionReason = decisionReason,
            SelectionStatus = "Pending"
        };
        return new BotCEvaluation(bundle, decision, candidate);
    }

    private async Task PersistPendingBotCEvaluationsAsync(
        Guid runId,
        IEnumerable<BotVariantProfile> profiles,
        IEnumerable<UpcomingOddsRecord> oddsRows,
        string reason,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return;
        }

        foreach (var profile in profiles)
        {
            var configuration = profile.SelectorConfiguration
                ?? throw new InvalidOperationException($"Bot {profile.Key} has no Pick Selector configuration.");
            foreach (var odds in oddsRows)
            {
                var decision = PendingBotCDecision(configuration, odds, reason);
                await _repository.UpsertBotCEvaluationAsync(
                    new PersistBotCEvaluationCommand(
                        runId,
                        profile.Key,
                        profile.AutomationVersion,
                        odds,
                        MapSelectionMarketType(odds.MarketType),
                        "Models 2026",
                        "unavailable-missing-history",
                        decision),
                    cancellationToken);
            }
        }
    }

    private static BotCPickDecision PendingBotCDecision(
        BotCStrategyConfiguration configuration,
        UpcomingOddsRecord odds,
        string reason)
    {
        var reasons = new[] { BotCDecisionCodes.PendingHistory };
        var risks = new[] { BotCRiskFlags.InsufficientOverallHistory };
        var summary = $"PendingData: {reason}";
        var snapshot = JsonSerializer.Serialize(new
        {
            featureSchemaVersion = configuration.FeatureSchemaVersion,
            configurationVersion = configuration.ConfigurationVersion,
            asOfDateUtc = EnsureUtc(odds.MatchDate),
            oddsCapturedAtUtc = EnsureUtc(odds.OddsCapturedAtUtc ?? odds.UpdatedAtUtc),
            leakageGuard = new { strictBeforeAsOf = true },
            pendingReason = reason,
            reasons,
            risks
        });
        return new BotCPickDecision(
            Decision: "PendingData",
            DecisionEngineType: "RuleBasedFallback",
            SelectedSide: string.Empty,
            SelectedOdds: null,
            BaseRawProbability: 0,
            BaseCalibratedProbability: 0,
            RawImpliedProbability: 0,
            MarketNoVigProbability: null,
            MarketOverround: 0,
            FinalProbability: 0,
            FinalEdge: 0,
            FinalExpectedValue: 0,
            RuleBasedConfidenceScore: 0,
            ContextExpectedValue: 0,
            ContextAgreementScore: 0,
            DataQualityScore: 0,
            BaseLineMargin: 0,
            ContextLineMargin: 0,
            BaseLineDistanceSigma: 0,
            ContextLineDistanceSigma: 0,
            CombinedExactLineShrunkHitRate: 0,
            SelectionScore: 0,
            DecisionReasons: reasons,
            RiskFlags: risks,
            Summary: summary,
            FeatureSchemaVersion: configuration.FeatureSchemaVersion,
            ConfigurationVersion: configuration.ConfigurationVersion,
            FeatureSnapshotJson: snapshot);
    }

    private static IReadOnlyList<BotCHistoricalObservation> ToBotCHistory(
        IReadOnlyList<MatchHistoryItemDto> matches,
        string teamName,
        string sourceMarketType)
    {
        return matches
            .Select(match =>
            {
                var teamIsHome = TeamNameMatcher.AreEquivalent(match.HomeTeam, teamName);
                var teamIsAway = TeamNameMatcher.AreEquivalent(match.AwayTeam, teamName);
                if (!teamIsHome && !teamIsAway)
                {
                    return null;
                }

                var homeValue = MatchMetric(match, sourceMarketType, home: true);
                var awayValue = MatchMetric(match, sourceMarketType, home: false);
                return new BotCHistoricalObservation(
                    DateTime.SpecifyKind(match.MatchDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                    teamIsHome ? homeValue : awayValue,
                    teamIsHome ? awayValue : homeValue);
            })
            .Where(value => value is not null)
            .Cast<BotCHistoricalObservation>()
            .OrderByDescending(value => value.MatchDateUtc)
            .ToArray();
    }

    private static IReadOnlyList<BotDTeamResultObservation> ToBotDHistory(
        IEnumerable<MatchHistoryItemDto> matches) =>
        matches
            .Select(match => new BotDTeamResultObservation(
                match.Id,
                DateTime.SpecifyKind(match.MatchDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                match.HomeTeam,
                match.AwayTeam,
                match.HomeGoals,
                match.AwayGoals))
            .ToArray();

    private static double MatchMetric(MatchHistoryItemDto match, string sourceMarketType, bool home)
    {
        if (sourceMarketType.StartsWith("Goals", StringComparison.OrdinalIgnoreCase))
        {
            return home ? match.HomeGoals : match.AwayGoals;
        }
        if (sourceMarketType.StartsWith("ShotsOnTarget", StringComparison.OrdinalIgnoreCase))
        {
            return home ? match.HomeShotsOnGoal : match.AwayShotsOnGoal;
        }
        if (sourceMarketType.StartsWith("Shots", StringComparison.OrdinalIgnoreCase))
        {
            return home ? match.HomeShots : match.AwayShots;
        }
        return home ? match.HomeCorners : match.AwayCorners;
    }

    private async Task<IReadOnlyList<AutomatedBotPerformanceScorecard>> LoadProductionScorecardsAsync(
        bool required,
        CancellationToken cancellationToken)
    {
        if (!required)
            return [];

        try
        {
            return await _cache.GetOrCreateAsync<IReadOnlyList<AutomatedBotPerformanceScorecard>>(
                PerformanceScorecardsCacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                    return await _performanceService.GetScorecardsAsync(cancellationToken);
                }) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Production scorecards could not be loaded. Publication will fail closed for this run.");
            return [];
        }
    }

    private static AutomatedBotProductionEligibility EvaluateProductionEligibility(
        IReadOnlyCollection<AutomatedBotPerformanceScorecard> scorecards,
        BotVariantProfile profile,
        AutomatedSelectionCandidate candidate) =>
        AutomatedBotProductionEligibilityPolicy.Evaluate(
            scorecards,
            profile.Key,
            MarketFamily(candidate.Odds.MarketType),
            MapSelectionMarketType(candidate.Odds.MarketType),
            candidate.SelectedSide,
            candidate.Odds.Source,
            profile.AutomationVersion,
            candidate.Odds.LineValue,
            EnsureUtc(candidate.Odds.OddsCapturedAtUtc ?? candidate.Odds.UpdatedAtUtc),
            DateTime.UtcNow,
            immutableOddsSnapshotAvailable: candidate.Odds.OddsSnapshotId is > 0
                && candidate.Odds.OddsCapturedAtUtc.HasValue
                && candidate.Odds.SnapshotOverOdds is > 1m
                && candidate.Odds.SnapshotUnderOdds is > 1m);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime ToUtcFromSantiago(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(value, SantiagoTimeZone)
    };

    private static DateTime? ParseTrainedThroughUtc(string? value)
    {
        if (DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var date))
        {
            return DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }

        return DateTime.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : null;
    }

    private static PersistedAutomatedSelection ToPersistedSelection(
        Guid runId,
        string automationVersion,
        AutomatedSelectionCandidate candidate,
        decimal stake)
    {
        return new PersistedAutomatedSelection
        {
            RunId = runId,
            AutomationVersion = automationVersion,
            Source = candidate.Odds.Source,
            SourceMatchId = candidate.Odds.SourceMatchId,
            SourceUrl = candidate.Odds.SourceUrl,
            MatchDate = candidate.Odds.MatchDate,
            League = candidate.Odds.League,
            StandardizedLeague = candidate.Odds.StandardizedLeague,
            HomeTeam = candidate.Odds.HomeTeam,
            AwayTeam = candidate.Odds.AwayTeam,
            StandardizedHomeTeam = candidate.Odds.StandardizedHomeTeam,
            StandardizedAwayTeam = candidate.Odds.StandardizedAwayTeam,
            HomeTeamGender = candidate.Odds.HomeTeamGender,
            AwayTeamGender = candidate.Odds.AwayTeamGender,
            SourceMarketType = candidate.Odds.MarketType,
            MarketType = MapSelectionMarketType(candidate.Odds.MarketType),
            LineValue = candidate.Odds.LineValue,
            SelectedSide = candidate.SelectedSide,
            Odds = candidate.SelectedOdds,
            Stake = stake,
            FlatStake = stake,
            ImpliedProbability = candidate.ImpliedProbability.ToSqlDecimal(),
            ModelProbability = candidate.ModelProbability.ToSqlDecimal(),
            ProbabilityEdge = candidate.ProbabilityEdge.ToSqlDecimal(),
            ExpectedValue = candidate.ExpectedValue.ToSqlDecimal(),
            KellyFraction = candidate.KellyFraction.ToSqlDecimal(),
            SelectionScore = candidate.SelectionScore.ToSqlDecimal(),
            PredictedTotalCorners = candidate.CornersPrediction.PredictedTotalCorners.ToSqlDecimal(),
            PredTotalDirect = candidate.CornersPrediction.PredTotalDirect?.ToSqlDecimal(),
            PredHomeCorners = candidate.CornersPrediction.PredHomeCorners?.ToSqlDecimal(),
            PredAwayCorners = candidate.CornersPrediction.PredAwayCorners?.ToSqlDecimal(),
            PredTotalCombined = candidate.CornersPrediction.PredTotalCombined?.ToSqlDecimal(),
            DistanceToLine = candidate.DistanceToLine.ToSqlDecimal(),
            ConfidenceLevel = candidate.CornersPrediction.Confidence,
            OverUnderConfidenceLevel = candidate.OverUnderPrediction?.Confidence,
            ModelConsensus = candidate.CornersPrediction.ModelConsensus,
            ContextTotalCorners = ResolveContextPrediction(candidate.Odds.MarketType, candidate.PredictionContext).ToSqlDecimal(),
            ContextDifference = candidate.ContextDifference.ToSqlDecimal(),
            RecommendedSide = candidate.CornersPrediction.RecommendedSide,
            Status = "Pending",
            DecisionReason = candidate.DecisionReason,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static double CalculateKellyFraction(double winProbability, double decimalOdds)
    {
        if (winProbability <= 0 || decimalOdds <= 1)
        {
            return 0;
        }

        var b = decimalOdds - 1d;
        var q = 1d - winProbability;
        var fullKelly = ((b * winProbability) - q) / b;
        return Math.Clamp(fullKelly, 0d, 1d);
    }

    private static decimal CalculateProfileStake(decimal baseStake, BotVariantProfile botProfile) =>
        Math.Round(baseStake * botProfile.StakeMultiplier, 2, MidpointRounding.AwayFromZero);

    private async Task<PredictionContextDto> GetPredictionContextAsync(
        string? league,
        string homeTeam,
        string awayTeam,
        string teamGender,
        DateOnly beforeDate,
        CancellationToken cancellationToken)
    {
        var context = await _predictionContextUseCase.GetAsync(
            homeTeam,
            awayTeam,
            league,
            teamGender,
            baseLocalAwayPrediction: null,
            beforeDate,
            cancellationToken);
        var comparison = context.Comparison;

        return new PredictionContextDto(
            new PredictionComparisonDto(
                comparison.EnrichedPrediction,
                comparison.Difference,
                comparison.Recommendation,
                comparison.EnrichedShotsOnGoalPrediction,
                comparison.EnrichedGoalsPrediction,
                comparison.HomeExpectedShotsOnGoal,
                comparison.AwayExpectedShotsOnGoal,
                comparison.HomeExpectedGoals,
                comparison.AwayExpectedGoals,
                comparison.EnrichedShotsPrediction,
                comparison.HomeExpectedShots,
                comparison.AwayExpectedShots),
            MapHistory(context.HomeGeneralMatches),
            MapHistory(context.HomeAsHomeMatches),
            MapHistory(context.AwayGeneralMatches),
            MapHistory(context.AwayAsAwayMatches));
    }

    private static IReadOnlyList<MatchHistoryItemDto> MapHistory(
        IReadOnlyList<CornersPrediction.Application.MatchHistory.MatchHistoryItemDto> matches) =>
        matches.Select(match => new MatchHistoryItemDto(
            match.Id,
            match.League,
            match.Season,
            match.MatchDate,
            match.IsKnockout,
            match.HomeTeam,
            match.AwayTeam,
            match.HomeFormation,
            match.AwayFormation,
            match.HomeCorners,
            match.AwayCorners,
            match.HomeGoals,
            match.AwayGoals,
            match.HomeShots,
            match.AwayShots,
            match.HomeShotsOnGoal,
            match.AwayShotsOnGoal,
            match.HomePossession,
            match.AwayPossession,
            match.TotalCorners)).ToArray();

    private static bool HasEnoughPredictionHistory(PredictionContextDto? context, bool isNeutralMatch)
    {
        if (context is null)
        {
            return false;
        }

        if (isNeutralMatch)
        {
            return (context.HomeGeneralMatches?.Count ?? 0) > 0
                && (context.AwayGeneralMatches?.Count ?? 0) > 0;
        }

        return (context.HomeAsHomeMatches?.Count ?? 0) > 0
            && (context.AwayAsAwayMatches?.Count ?? 0) > 0;
    }

    private static string BuildHistoryAvailabilityReason(
        PredictionContextDto? context,
        PredictionContextDto? swappedContext,
        bool isNeutralMatch)
    {
        if (isNeutralMatch)
        {
            return $"No enough neutral-direction general history was available. {DescribeNeutralHistory(context, swappedContext)}";
        }

        return $"No usable team history was available. {DescribeDirectionalHistory(context)}";
    }

    private static string DescribeDirectionalHistory(PredictionContextDto? context)
    {
        if (context is null)
        {
            return "Prediction context was null.";
        }

        return $"Home general/home: {context.HomeGeneralMatches?.Count ?? 0}/{context.HomeAsHomeMatches?.Count ?? 0}, away general/away: {context.AwayGeneralMatches?.Count ?? 0}/{context.AwayAsAwayMatches?.Count ?? 0}.";
    }

    private static string DescribeNeutralHistory(
        PredictionContextDto? context,
        PredictionContextDto? swappedContext)
    {
        if (context is null || swappedContext is null)
        {
            return "One of the neutral-direction contexts was null.";
        }

        return $"Direct general counts: {context.HomeGeneralMatches?.Count ?? 0}/{context.AwayGeneralMatches?.Count ?? 0}; swapped general counts: {swappedContext.HomeGeneralMatches?.Count ?? 0}/{swappedContext.AwayGeneralMatches?.Count ?? 0}.";
    }

    private static bool IsNeutralOrInternationalMatch(UpcomingOddsRecord odds)
    {
        var league = NormalizeComparableText(odds.EffectiveLeague);
        return league.Contains("world cup", StringComparison.Ordinal)
            || league.Contains("fifa", StringComparison.Ordinal)
            || league.Contains("copa del mundo", StringComparison.Ordinal)
            || league.Contains("mundial", StringComparison.Ordinal)
            || league.Contains("international", StringComparison.Ordinal)
            || league.Contains("friendly", StringComparison.Ordinal)
            || league.Contains("amistoso", StringComparison.Ordinal)
            || league.Contains("qualifying", StringComparison.Ordinal)
            || league.Contains("eliminatorias", StringComparison.Ordinal)
            || league.Contains("gold cup", StringComparison.Ordinal)
            || league.Contains("africa cup", StringComparison.Ordinal)
            || league.Contains("asian cup", StringComparison.Ordinal)
            || league.Contains("nations league", StringComparison.Ordinal)
            || league.Contains("european championship", StringComparison.Ordinal)
            || league.Contains("copa america", StringComparison.Ordinal)
            || league.Contains("copa américa", StringComparison.Ordinal);
    }

    private static string NormalizeGender(string? value)
    {
        return string.Equals(value, "F", StringComparison.OrdinalIgnoreCase) ? "F" : "M";
    }

    private static string BuildMatchIdentity(UpcomingOddsRecord row)
    {
        static string NormalizeKey(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

        // Source-specific IDs differ between bookmakers. Group one canonical match per
        // model family so corners, goals and SOG each produce their own best pick.
        return string.Join(
            "|",
            row.MatchDate.ToString("yyyy-MM-ddTHH:mm:ss"),
            NormalizeKey(row.EffectiveLeague),
            NormalizeKey(row.EffectiveHomeTeam),
            NormalizeKey(row.EffectiveAwayTeam),
            NormalizeKey(row.HomeTeamGender),
            NormalizeKey(row.AwayTeamGender),
            MarketFamily(row.MarketType));
    }

    private static UpcomingOddsRecord[] SelectBatchOddsRows(
        IReadOnlyList<UpcomingOddsRecord> eligibleOddsRows,
        int batchOffset,
        int batchSize,
        bool completeFixtures)
    {
        if (!completeFixtures)
            return eligibleOddsRows.Skip(batchOffset).Take(batchSize).ToArray();

        return eligibleOddsRows
            .GroupBy(BuildMatchIdentity)
            .Skip(batchOffset)
            .Take(batchSize)
            .SelectMany(group => group)
            .ToArray();
    }

    private static string MarketFamily(string sourceMarketType) => sourceMarketType switch
    {
        "GoalsTotal" or "GoalsHomeTeam" or "GoalsAwayTeam" => "GOALS",
        "ShotsTotal" or "ShotsHomeTeam" or "ShotsAwayTeam" => "SHOTS",
        "ShotsOnTargetTotal" or "ShotsOnTargetHomeTeam" or "ShotsOnTargetAwayTeam" => "SOG",
        _ => "CORNERS"
    };

    private static HashSet<string> ParseMarketFamilies(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var families = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supported = new HashSet<string>(["CORNERS", "GOALS", "SHOTS", "SOG"], StringComparer.OrdinalIgnoreCase);
        var invalid = families.Where(item => !supported.Contains(item)).ToArray();
        if (invalid.Length > 0)
        {
            throw new ArgumentException($"Unsupported market families: {string.Join(", ", invalid)}.");
        }

        return families;
    }

    private static HashSet<string> ParseBotKeys(string? value, bool onlyBotC, bool runBotC)
    {
        if (onlyBotC)
        {
            return new HashSet<string>(["C2026"], StringComparer.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(
                runBotC ? ["A", "C2026"] : ["A"],
                StringComparer.OrdinalIgnoreCase);
        }

        var botKeys = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RecommendationBotDefinitionsUseCase.NormalizeBotKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (botKeys.Count == 0)
        {
            throw new ArgumentException("At least one bot key is required.");
        }

        return botKeys;
    }

    private static string? NormalizeSide(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "OVER" => "Over",
            "UNDER" => "Under",
            _ => null
        };
    }

    private static double ConfidenceWeight(string? confidence)
    {
        return confidence?.Trim().ToUpperInvariant() switch
        {
            "VERY_HIGH" => 0.18,
            "HIGH" => 0.12,
            "MEDIUM" => 0.06,
            _ => 0
        };
    }

    private static double ConsensusWeight(string? consensus)
    {
        return consensus?.Trim().ToUpperInvariant() switch
        {
            "HIGH" => 0.08,
            "MEDIUM" => 0.04,
            "LOW" => -0.03,
            _ => 0
        };
    }

    private static MarketSnapshot? ResolveMarketSnapshot(
        UpcomingOddsRecord odds,
        PredictionResultDto cornersPrediction,
        IReadOnlyDictionary<string, object?> features,
        PredictionContextDto context)
    {
        var lineValue = Convert.ToDouble(odds.LineValue);

        return odds.MarketType switch
        {
            "CornersHomeTeam" when cornersPrediction.PredHomeCorners is double predictedHomeCorners => new MarketSnapshot(
                SelectionMarketType: "HomeTeamCorners",
                PredictedValue: predictedHomeCorners,
                ContextValue: ToFeatureDouble(features, "HomeCornersPowerLast5", predictedHomeCorners),
                CornersSide: predictedHomeCorners >= lineValue ? "Over" : "Under",
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(0.95, (cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 2.633) / 1.8d)),
            "CornersAwayTeam" when cornersPrediction.PredAwayCorners is double predictedAwayCorners => new MarketSnapshot(
                SelectionMarketType: "AwayTeamCorners",
                PredictedValue: predictedAwayCorners,
                ContextValue: ToFeatureDouble(features, "AwayCornersPowerLast5", predictedAwayCorners),
                CornersSide: predictedAwayCorners >= lineValue ? "Over" : "Under",
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(0.95, (cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 2.633) / 1.8d)),
            "GoalsTotal" => new MarketSnapshot(
                SelectionMarketType: "TotalGoals",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.EnrichedGoalsPrediction,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(0.90, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 1.3131d)),
            "GoalsHomeTeam" => new MarketSnapshot(
                SelectionMarketType: "HomeTeamGoals",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.HomeExpectedGoals,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(0.90, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 0.9290d)),
            "GoalsAwayTeam" => new MarketSnapshot(
                SelectionMarketType: "AwayTeamGoals",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.AwayExpectedGoals,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(0.90, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 0.8157d)),
            "ShotsTotal" => new MarketSnapshot(
                SelectionMarketType: "TotalShots",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.EnrichedShotsPrediction,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(2.0, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 4.6174d)),
            "ShotsHomeTeam" => new MarketSnapshot(
                SelectionMarketType: "HomeTeamShots",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.HomeExpectedShots,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(2.0, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 3.8192d)),
            "ShotsAwayTeam" => new MarketSnapshot(
                SelectionMarketType: "AwayTeamShots",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.AwayExpectedShots,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(2.0, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 3.4867d)),
            "ShotsOnTargetTotal" => new MarketSnapshot(
                SelectionMarketType: "TotalShotsOnGoal",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.EnrichedShotsOnGoalPrediction,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(1.35, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 2.6060d)),
            "ShotsOnTargetHomeTeam" => new MarketSnapshot(
                SelectionMarketType: "HomeTeamShotsOnGoal",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.HomeExpectedShotsOnGoal,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(1.35, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 1.8761d)),
            "ShotsOnTargetAwayTeam" => new MarketSnapshot(
                SelectionMarketType: "AwayTeamShotsOnGoal",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.AwayExpectedShotsOnGoal,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: false,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(1.35, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 1.7004d)),
            _ => new MarketSnapshot(
                SelectionMarketType: "TotalCorners",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.EnrichedPrediction,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: true,
                Sigma: cornersPrediction.ProbabilitySigma
                    ?? Math.Max(1.35, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 2.633))
        };
    }

    private static string MapSelectionMarketType(string sourceMarketType)
    {
        return sourceMarketType switch
        {
            "CornersHomeTeam" => "HomeTeamCorners",
            "CornersAwayTeam" => "AwayTeamCorners",
            "GoalsTotal" => "TotalGoals",
            "GoalsHomeTeam" => "HomeTeamGoals",
            "GoalsAwayTeam" => "AwayTeamGoals",
            "ShotsTotal" => "TotalShots",
            "ShotsHomeTeam" => "HomeTeamShots",
            "ShotsAwayTeam" => "AwayTeamShots",
            "ShotsOnTargetTotal" => "TotalShotsOnGoal",
            "ShotsOnTargetHomeTeam" => "HomeTeamShotsOnGoal",
            "ShotsOnTargetAwayTeam" => "AwayTeamShotsOnGoal",
            _ => "TotalCorners"
        };
    }

    private static double ResolveContextPrediction(string sourceMarketType, PredictionContextDto context)
    {
        return sourceMarketType switch
        {
            "GoalsTotal" => context.Comparison.EnrichedGoalsPrediction,
            "GoalsHomeTeam" => context.Comparison.HomeExpectedGoals,
            "GoalsAwayTeam" => context.Comparison.AwayExpectedGoals,
            "ShotsTotal" => context.Comparison.EnrichedShotsPrediction,
            "ShotsHomeTeam" => context.Comparison.HomeExpectedShots,
            "ShotsAwayTeam" => context.Comparison.AwayExpectedShots,
            "ShotsOnTargetTotal" => context.Comparison.EnrichedShotsOnGoalPrediction,
            "ShotsOnTargetHomeTeam" => context.Comparison.HomeExpectedShotsOnGoal,
            "ShotsOnTargetAwayTeam" => context.Comparison.AwayExpectedShotsOnGoal,
            _ => context.Comparison.EnrichedPrediction
        };
    }

    private static double ToFeatureDouble(
        IReadOnlyDictionary<string, object?> features,
        string key,
        double fallback)
    {
        if (!features.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            double doubleValue => doubleValue,
            decimal decimalValue => Convert.ToDouble(decimalValue),
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            _ when double.TryParse(Convert.ToString(value), out var parsedValue) => parsedValue,
            _ => fallback
        };
    }

    private static double AverageFinite(params double[] values)
    {
        var finiteValues = values.Where(double.IsFinite).ToArray();
        if (finiteValues.Length == 0)
        {
            return 0;
        }

        return RoundNeutralFeature(finiteValues.Average());
    }

    private static double? AverageNullable(params double?[] values)
    {
        var finiteValues = values
            .Where(value => value is not null && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .ToArray();

        return finiteValues.Length == 0 ? null : RoundNeutralFeature(finiteValues.Average());
    }

    private static string? LowerConfidence(params string?[] values)
    {
        var normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToUpperInvariant())
            .ToArray();

        if (normalizedValues.Length == 0)
        {
            return null;
        }

        return normalizedValues
            .OrderBy(ConfidenceRank)
            .First() switch
            {
                "VERY_HIGH" => "VERY_HIGH",
                "HIGH" => "HIGH",
                "MEDIUM" => "MEDIUM",
                "LOW" => "LOW",
                var value => value
            };
    }

    private static string? LowerConsensus(params string?[] values)
    {
        var normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToUpperInvariant())
            .ToArray();

        if (normalizedValues.Length == 0)
        {
            return null;
        }

        return normalizedValues
            .OrderBy(ConsensusRank)
            .First() switch
            {
                "HIGH" => "HIGH",
                "MEDIUM" => "MEDIUM",
                "LOW" => "LOW",
                var value => value
            };
    }

    private static int ConfidenceRank(string value) =>
        value switch
        {
            "VERY_HIGH" => 4,
            "HIGH" => 3,
            "MEDIUM" => 2,
            "LOW" => 1,
            _ => 0
        };

    private static int ConsensusRank(string value) =>
        value switch
        {
            "HIGH" => 3,
            "MEDIUM" => 2,
            "LOW" => 1,
            _ => 0
        };

    private static string NormalizeComparableText(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static double RoundNeutralFeature(double value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed record PredictionBundle(
        UpcomingOddsRecord Odds,
        PredictionContextDto PredictionContext,
        PredictionResultDto CornersPrediction,
        OverUnderPredictionResultDto? OverUnderPrediction,
        Dictionary<string, object?> Features,
        bool IsNeutralAdjusted);

    private sealed record BotCEvaluation(
        PredictionBundle Bundle,
        BotCPickDecision Decision,
        AutomatedSelectionCandidate? Candidate);

    private sealed record PersistCandidateResult(
        AutomatedSelectionResult? Result,
        bool Inserted,
        bool Updated,
        bool RejectedByRobustLayer,
        string? RobustReason);

    private sealed record SelectorProfilesRunResult(
        bool HadSelection,
        int InsertedRows,
        int UpdatedRows,
        IReadOnlyList<AutomatedSelectionResult> Selections,
        IReadOnlyList<SkippedMatchResult> Skipped);

    private sealed record RoleAdjustedPrediction(
        double Value,
        NewGenerationPredictionResult Prediction);

    private sealed record NewGenerationTargets(
        string Selected,
        string Home,
        string Away,
        string Total);

    private sealed record MarketSnapshot(
        string SelectionMarketType,
        double PredictedValue,
        double ContextValue,
        string? CornersSide,
        bool AllowOverUnderModel,
        double Sigma);
}

internal static class StandardNormalDistribution
{
    public static double Cdf(double x, double mean, double sigma)
    {
        if (sigma <= 0)
        {
            return x < mean ? 0d : 1d;
        }

        var z = (x - mean) / (sigma * Math.Sqrt(2d));
        return 0.5d * (1d + Erf(z));
    }

    private static double Erf(double x)
    {
        var sign = Math.Sign(x);
        x = Math.Abs(x);

        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var t = 1d / (1d + p * x);
        var y = 1d - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
