using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AutomatedCornersBot.Api;

public sealed class AutomatedCornersSelectionService
{
    private readonly AutomatedBotOptions _options;
    private readonly SqlAutomationRepository _repository;
    private readonly PredictionApiClient _predictionApiClient;
    private readonly FeatureBuilder _featureBuilder;
    private readonly ILogger<AutomatedCornersSelectionService> _logger;

    public AutomatedCornersSelectionService(
        IOptions<AutomatedBotOptions> options,
        SqlAutomationRepository repository,
        PredictionApiClient predictionApiClient,
        FeatureBuilder featureBuilder,
        ILogger<AutomatedCornersSelectionService> logger)
    {
        _options = options.Value;
        _repository = repository;
        _predictionApiClient = predictionApiClient;
        _featureBuilder = featureBuilder;
        _logger = logger;
    }

    public async Task<AutomatedRunResponse> RunAsync(
        RunAutomatedCornersRequest? request,
        CancellationToken cancellationToken)
    {
        var effectiveRequest = request ?? new RunAutomatedCornersRequest(null, null, null, null, null, null, null, false, null, null, true);
        var dateFrom = effectiveRequest.DateFrom ?? DateOnly.FromDateTime(DateTime.Today);
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
        var botProfiles = BuildBotProfiles(
            minEdge,
            minExpectedValue,
            minDistanceToLine,
            maxContextDifference,
            allowDisagreement);
        var runId = Guid.NewGuid();

        var oddsRows = await _repository.GetUpcomingOddsAsync(
            dateFrom,
            dateTo,
            effectiveRequest.League,
            effectiveRequest.ExcludeExistingSelections,
            botProfiles.Count,
            cancellationToken);
        var groupedMatches = oddsRows
            .GroupBy(BuildMatchIdentity)
            .ToArray();

        var selections = new List<AutomatedSelectionResult>();
        var skipped = new List<SkippedMatchResult>();
        var errors = new List<ErrorMatchResult>();
        var insertedRows = 0;
        var updatedRows = 0;
        var teamInfoCache = new Dictionary<string, IReadOnlyList<TeamBi3InfoDto>>(StringComparer.OrdinalIgnoreCase);

        foreach (var matchGroup in groupedMatches)
        {
            var representative = matchGroup.First();
            var league = representative.EffectiveLeague;
            var homeTeam = representative.EffectiveHomeTeam;
            var awayTeam = representative.EffectiveAwayTeam;
            var teamGender = NormalizeGender(representative.HomeTeamGender);
            var isNeutralMatch = IsNeutralOrInternationalMatch(representative);

            try
            {
                var predictionContext = await _predictionApiClient.GetPredictionContextAsync(
                    null,
                    homeTeam,
                    awayTeam,
                    teamGender,
                    cancellationToken);

                if (predictionContext is null)
                {
                    skipped.Add(new SkippedMatchResult(league, homeTeam, awayTeam, representative.MatchDate, "Prediction context was empty."));
                    continue;
                }

                PredictionContextDto? swappedPredictionContext = null;
                if (isNeutralMatch)
                {
                    swappedPredictionContext = await _predictionApiClient.GetPredictionContextAsync(
                        null,
                        awayTeam,
                        homeTeam,
                        teamGender,
                        cancellationToken);
                }

                if (!HasEnoughPredictionHistory(predictionContext, isNeutralMatch)
                    || (isNeutralMatch && !HasEnoughPredictionHistory(swappedPredictionContext, true)))
                {
                    skipped.Add(new SkippedMatchResult(
                        league,
                        homeTeam,
                        awayTeam,
                        representative.MatchDate,
                        isNeutralMatch
                            ? "No enough neutral-direction general history was available."
                            : "No enough home/away condition history was available."));
                    continue;
                }

                if (!teamInfoCache.TryGetValue($"{league}|{teamGender}", out var teamInfo))
                {
                    teamInfo = await _predictionApiClient.GetTeamInfoAsync(league, teamGender, cancellationToken);
                    teamInfoCache[$"{league}|{teamGender}"] = teamInfo;
                }

                var predictionBundles = new List<PredictionBundle>();

                foreach (var odds in matchGroup.OrderBy(row => row.LineValue))
                {
                    predictionBundles.Add(await BuildPredictionBundleAsync(
                        odds,
                        predictionContext,
                        swappedPredictionContext,
                        teamInfo,
                        isNeutralMatch,
                        cancellationToken));
                }

                var matchHadSelection = false;
                foreach (var botProfile in botProfiles)
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
                            predictionBundle.IsNeutralAdjusted);

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
                    var profileStake = CalculateProfileStake(stake, botProfile);
                    if (effectiveRequest.DryRun)
                    {
                        selections.Add(new AutomatedSelectionResult(
                            "DRY_RUN",
                            ToPersistedSelection(runId, botProfile.AutomationVersion, bestCandidate, profileStake)));
                        continue;
                    }

                    var upsert = await _repository.UpsertSelectionAsync(
                        new PersistSelectionCommand
                        {
                            RunId = runId,
                            AutomationVersion = botProfile.AutomationVersion,
                            Odds = bestCandidate.Odds,
                            SelectedSide = bestCandidate.SelectedSide,
                            SelectedOdds = bestCandidate.SelectedOdds,
                            Stake = profileStake,
                            ImpliedProbability = bestCandidate.ImpliedProbability,
                            ModelProbability = bestCandidate.ModelProbability,
                            ProbabilityEdge = bestCandidate.ProbabilityEdge,
                            ExpectedValue = bestCandidate.ExpectedValue,
                            KellyFraction = bestCandidate.KellyFraction,
                            SelectionScore = bestCandidate.SelectionScore,
                            CornersPrediction = bestCandidate.CornersPrediction,
                            OverUnderPrediction = bestCandidate.OverUnderPrediction,
                            PredictionContext = bestCandidate.PredictionContext,
                            DecisionReason = bestCandidate.DecisionReason
                        },
                        cancellationToken);

                    if (upsert.MergeAction.Equals("INSERT", StringComparison.OrdinalIgnoreCase))
                    {
                        insertedRows++;
                    }
                    else if (upsert.MergeAction.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
                    {
                        updatedRows++;
                    }

                    selections.Add(new AutomatedSelectionResult(
                        upsert.MergeAction,
                        ToPersistedSelection(runId, botProfile.AutomationVersion, bestCandidate, profileStake) with
                        {
                            AutomatedCornerBetSelectionId = upsert.SelectionId
                        }));
                }

                if (!matchHadSelection)
                {
                    continue;
                }
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

        return new AutomatedRunResponse(
            RunId: runId,
            DateFrom: dateFrom,
            DateTo: dateTo,
            TotalOddsRows: oddsRows.Count,
            TotalMatches: groupedMatches.Length,
            SelectedMatches: selections.Count,
            InsertedRows: insertedRows,
            UpdatedRows: updatedRows,
            SkippedMatches: skipped.Count,
            ErrorMatches: errors.Count,
            Selections: selections,
            Skipped: skipped,
            Errors: errors);
    }

    public async Task<SettleAutomatedCornersResponse> SettleAsync(
        SettleAutomatedCornersRequest? request,
        CancellationToken cancellationToken)
    {
        var effectiveRequest = request ?? new SettleAutomatedCornersRequest(null, false);
        var previewRows = await _repository.PreviewSettleAsync(effectiveRequest.MatchDateTo, cancellationToken);
        if (effectiveRequest.DryRun)
        {
            return new SettleAutomatedCornersResponse(
                effectiveRequest.MatchDateTo,
                true,
                previewRows.Count,
                0,
                previewRows);
        }

        var settledRows = await _repository.SettleAsync(effectiveRequest.MatchDateTo, cancellationToken);
        var rows = await _repository.GetSelectionsAsync(null, effectiveRequest.MatchDateTo, null, cancellationToken);
        return new SettleAutomatedCornersResponse(
            effectiveRequest.MatchDateTo,
            false,
            previewRows.Count,
            settledRows,
            rows.Where(row => row.SettledAtUtc is not null)
                .OrderByDescending(row => row.UpdatedAtUtc)
                .Take(50)
                .ToArray());
    }

    private IReadOnlyList<BotVariantProfile> BuildBotProfiles(
        double minEdge,
        double minExpectedValue,
        double minDistanceToLine,
        double maxContextDifference,
        bool allowModelDisagreement)
    {
        var botA = new BotVariantProfile(
            Key: "A",
            AutomationVersion: $"{_options.AutomationVersion}-A",
            DisplayName: "Bot A Actual",
            MinEdge: minEdge,
            MinExpectedValue: minExpectedValue,
            MinDistanceToLine: minDistanceToLine,
            MaxContextDifference: maxContextDifference,
            AllowModelDisagreement: allowModelDisagreement,
            MinOddsExclusive: null,
            MinProbabilityLiftOverImplied: 0,
            StakeMultiplier: 1m);

        if (!_options.EnableBotVariants)
        {
            return new[] { botA };
        }

        var lift = Math.Max(0, _options.ConservativeProbabilityLift);
        var stakeMultiplier = Convert.ToDecimal(Math.Clamp(_options.ConservativeStakeMultiplier, 0.01d, 1d));
        var botB = new BotVariantProfile(
            Key: "B",
            AutomationVersion: $"{_options.AutomationVersion}-B",
            DisplayName: "Bot B Conservador Odds > 1.60",
            MinEdge: minEdge * (1d + lift),
            MinExpectedValue: minExpectedValue * (1d + lift),
            MinDistanceToLine: minDistanceToLine * (1d + lift),
            MaxContextDifference: maxContextDifference * Math.Max(0.10d, 1d - lift),
            AllowModelDisagreement: false,
            MinOddsExclusive: _options.ConservativeMinOdds,
            MinProbabilityLiftOverImplied: lift,
            StakeMultiplier: stakeMultiplier);

        return new[] { botA, botB };
    }

    private async Task<PredictionBundle> BuildPredictionBundleAsync(
        UpcomingOddsRecord odds,
        PredictionContextDto predictionContext,
        PredictionContextDto? swappedPredictionContext,
        IReadOnlyList<TeamBi3InfoDto> teamInfo,
        bool isNeutralMatch,
        CancellationToken cancellationToken)
    {
        var features = _featureBuilder.Build(odds, predictionContext, teamInfo);
        var cornersPrediction = await _predictionApiClient.PredictCornersAsync(features, cancellationToken);
        var overUnderPrediction = await _predictionApiClient.PredictOverUnderAsync(features, cancellationToken);

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
        var swappedCornersPrediction = await _predictionApiClient.PredictCornersAsync(swappedFeatures, cancellationToken);
        var swappedOverUnderPrediction = await _predictionApiClient.PredictOverUnderAsync(swappedFeatures, cancellationToken);
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

    private static PredictionContextDto BuildNeutralContext(
        PredictionContextDto normalContext,
        PredictionContextDto swappedContext,
        UpcomingOddsRecord odds)
    {
        var enrichedPrediction = AverageFinite(
            normalContext.Comparison.EnrichedPrediction,
            swappedContext.Comparison.EnrichedPrediction);
        var recommendation = enrichedPrediction >= Convert.ToDouble(odds.LineValue) ? "Over" : "Under";

        return new PredictionContextDto(
            new PredictionComparisonDto(enrichedPrediction, null, recommendation),
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
        bool isNeutralAdjusted)
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

        var modelProbability = selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            ? overUnderPrediction?.OverProbability ?? approximateOverProbability
            : overUnderPrediction?.UnderProbability ?? approximateUnderProbability;

        var impliedProbability = 1d / (double)selectedOdds.Value;
        var probabilityEdge = modelProbability - impliedProbability;
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
                cornersPrediction.Message
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
            ContextTotalCorners = candidate.PredictionContext.Comparison.EnrichedPrediction.ToSqlDecimal(),
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

        // Source-specific IDs differ between bookmakers. Group them as one canonical match
        // so every available line competes and the bot can select the best value.
        return string.Join(
            "|",
            row.MatchDate.ToString("yyyy-MM-ddTHH:mm:ss"),
            NormalizeKey(row.EffectiveLeague),
            NormalizeKey(row.EffectiveHomeTeam),
            NormalizeKey(row.EffectiveAwayTeam),
            NormalizeKey(row.HomeTeamGender),
            NormalizeKey(row.AwayTeamGender));
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
                Sigma: Math.Max(0.95, (cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 2.633) / 1.8d)),
            "CornersAwayTeam" when cornersPrediction.PredAwayCorners is double predictedAwayCorners => new MarketSnapshot(
                SelectionMarketType: "AwayTeamCorners",
                PredictedValue: predictedAwayCorners,
                ContextValue: ToFeatureDouble(features, "AwayCornersPowerLast5", predictedAwayCorners),
                CornersSide: predictedAwayCorners >= lineValue ? "Over" : "Under",
                AllowOverUnderModel: false,
                Sigma: Math.Max(0.95, (cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 2.633) / 1.8d)),
            _ => new MarketSnapshot(
                SelectionMarketType: "TotalCorners",
                PredictedValue: cornersPrediction.PredictedTotalCorners,
                ContextValue: context.Comparison.EnrichedPrediction,
                CornersSide: NormalizeSide(cornersPrediction.RecommendedSide),
                AllowOverUnderModel: true,
                Sigma: Math.Max(1.35, cornersPrediction.Mae > 0 ? cornersPrediction.Mae : 2.633))
        };
    }

    private static string MapSelectionMarketType(string sourceMarketType)
    {
        return sourceMarketType switch
        {
            "CornersHomeTeam" => "HomeTeamCorners",
            "CornersAwayTeam" => "AwayTeamCorners",
            _ => "TotalCorners"
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
