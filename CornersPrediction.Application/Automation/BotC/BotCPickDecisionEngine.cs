using System.Text.Json;
using CornersPrediction.Application.Automation.BotD;
using CornersPrediction.Application.Automation.BotE;
using CornersPrediction.Application.FootballIntelligence;

namespace CornersPrediction.Application.Automation.BotC;

public sealed record BotCHistoricalObservation(
    DateTime MatchDateUtc,
    double ValueFor,
    double ValueAgainst);

public sealed record BotCPickEvaluationInput(
    string MarketType,
    decimal Line,
    decimal? OverOdds,
    decimal? UnderOdds,
    DateTime OddsCapturedAtUtc,
    DateTime AsOfDateUtc,
    double BasePredictedValue,
    double BasePredictedStandardDeviation,
    string BaseModelName,
    string BaseModelVersion,
    IReadOnlyList<BotCHistoricalObservation> HomeOverall,
    IReadOnlyList<BotCHistoricalObservation> HomeVenue,
    IReadOnlyList<BotCHistoricalObservation> AwayOverall,
    IReadOnlyList<BotCHistoricalObservation> AwayVenue,
    double? LeagueForAverage = null,
    double? LeagueAgainstAverage = null,
    bool CrossMarketPredictionAvailable = false,
    DateTime? BaseModelTrainedThroughUtc = null,
    string HomeTeam = "",
    string AwayTeam = "",
    IReadOnlyList<BotDTeamResultObservation>? TeamStrengthHistory = null,
    IReadOnlyList<BotECalibrationObservation>? CalibrationHistory = null,
    MatchIntelligenceSnapshotPair? FootballIntelligenceSnapshot = null,
    DateTime? PredictionTimestampUtc = null);

public sealed record BotCDistributionStatistics(
    int SampleCount,
    double SimpleAverage,
    double WeightedAverage,
    double Median,
    double StandardDeviation,
    double Variance,
    double Minimum,
    double Maximum,
    double Percentile25,
    double Percentile75,
    double InterquartileRange,
    double MedianAbsoluteDeviation);

public sealed record BotCPickDecision(
    string Decision,
    string DecisionEngineType,
    string SelectedSide,
    decimal? SelectedOdds,
    double BaseRawProbability,
    double BaseCalibratedProbability,
    double RawImpliedProbability,
    double? MarketNoVigProbability,
    double MarketOverround,
    double FinalProbability,
    double FinalEdge,
    double FinalExpectedValue,
    double RuleBasedConfidenceScore,
    double ContextExpectedValue,
    double ContextAgreementScore,
    double DataQualityScore,
    double BaseLineMargin,
    double ContextLineMargin,
    double BaseLineDistanceSigma,
    double ContextLineDistanceSigma,
    double CombinedExactLineShrunkHitRate,
    double SelectionScore,
    IReadOnlyList<string> DecisionReasons,
    IReadOnlyList<string> RiskFlags,
    string Summary,
    string FeatureSchemaVersion,
    string ConfigurationVersion,
    string FeatureSnapshotJson);

public interface IBotCPickDecisionEngine
{
    BotCPickDecision Evaluate(BotCPickEvaluationInput input, BotCStrategyConfiguration configuration);
}

public sealed class BotCPickDecisionEngine : IBotCPickDecisionEngine
{
    private const string RuleBasedEngine = "RuleBasedFallback";
    private const string MetaModelEngine = "MetaModel";
    private const string EmpiricalCalibrationEngine = "EmpiricalMarketCalibration";
    private readonly IBotCMetaModelPredictor _metaModelPredictor;

    public BotCPickDecisionEngine(IBotCMetaModelPredictor? metaModelPredictor = null)
    {
        _metaModelPredictor = metaModelPredictor ?? new UnavailableBotCMetaModelPredictor();
    }

    public BotCPickDecision Evaluate(BotCPickEvaluationInput input, BotCStrategyConfiguration configuration)
    {
        var config = BotCStrategyConfiguration.Validate(configuration);
        var risks = new List<string>();
        var reasons = new List<string>();
        var market = BotCMarketDefinition.Parse(input.MarketType);
        var asOfUtc = EnsureUtc(input.AsOfDateUtc);
        var homeOverall = Before(input.HomeOverall, asOfUtc);
        var homeVenue = Before(input.HomeVenue, asOfUtc);
        var awayOverall = Before(input.AwayOverall, asOfUtc);
        var awayVenue = Before(input.AwayVenue, asOfUtc);
        var line = Convert.ToDouble(input.Line);
        var sigma = Math.Max(config.MinimumStandardDeviation, input.BasePredictedStandardDeviation);

        if (!double.IsFinite(line) || line < 0)
        {
            risks.Add(BotCRiskFlags.InvalidLine);
            reasons.Add(BotCDecisionCodes.InvalidInput);
            return EmptyDecision("Invalid", input, config, risks, reasons, "La línea no es válida.");
        }

        if (!double.IsFinite(input.BasePredictedValue) || !double.IsFinite(input.BasePredictedStandardDeviation))
        {
            risks.Add(BotCRiskFlags.MissingBaseProbability);
            reasons.Add(BotCDecisionCodes.InvalidInput);
            return EmptyDecision("Invalid", input, config, risks, reasons, "La predicción base no es válida.");
        }

        if (input.BaseModelTrainedThroughUtc.HasValue
            && asOfUtc.Date <= EnsureUtc(input.BaseModelTrainedThroughUtc.Value).Date)
        {
            risks.Add(BotCRiskFlags.BaseModelTemporalLeakage);
            reasons.Add(BotCDecisionCodes.RejectedBaseModelTemporalLeakage);
            return EmptyDecision(
                "Invalid",
                input,
                config,
                risks,
                reasons,
                $"El modelo base fue entrenado hasta {input.BaseModelTrainedThroughUtc:yyyy-MM-dd}; no puede evaluar sin leakage un partido del {asOfUtc:yyyy-MM-dd}.");
        }

        var selectedSide = input.BasePredictedValue >= line ? "Over" : "Under";
        var thresholds = config.ResolveThresholds(market.Key, selectedSide);
        var selectedOdds = selectedSide == "Over" ? input.OverOdds : input.UnderOdds;
        var oppositeOdds = selectedSide == "Over" ? input.UnderOdds : input.OverOdds;
        if (selectedOdds is null || selectedOdds <= 1m)
        {
            risks.Add(BotCRiskFlags.InvalidOdds);
            reasons.Add(BotCDecisionCodes.PendingOdds);
            return EmptyDecision("PendingData", input, config, risks, reasons, $"No existe una cuota {selectedSide} utilizable.", selectedSide);
        }
        if (oppositeOdds is null || oppositeOdds <= 1m)
        {
            risks.Add(BotCRiskFlags.MissingOppositeOdds);
        }
        if (!input.CrossMarketPredictionAvailable)
        {
            risks.Add(BotCRiskFlags.MissingCrossMarketPrediction);
        }
        if (input.LeagueForAverage is null || input.LeagueAgainstAverage is null)
        {
            risks.Add(BotCRiskFlags.LeagueBaselineUnavailable);
        }
        if (homeOverall.Count == 0 || awayOverall.Count == 0)
        {
            risks.Add(BotCRiskFlags.InsufficientOverallHistory);
            reasons.Add(BotCDecisionCodes.PendingHistory);
            return EmptyDecision("PendingData", input, config, risks, reasons, "Falta historial previo para uno o ambos equipos.", selectedSide, selectedOdds);
        }

        if (homeOverall.Count < thresholds.MinimumHistoricalMatches || awayOverall.Count < thresholds.MinimumHistoricalMatches)
        {
            risks.Add(BotCRiskFlags.InsufficientOverallHistory);
        }
        if (homeVenue.Count < config.RequiredVenueMatches || awayVenue.Count < config.RequiredVenueMatches)
        {
            risks.Add(BotCRiskFlags.InsufficientVenueHistory);
        }

        var homeFor = BuildWindows(homeOverall.Select(value => value.ValueFor).ToArray(), config.DecayFactor);
        var homeAgainst = BuildWindows(homeOverall.Select(value => value.ValueAgainst).ToArray(), config.DecayFactor);
        var awayFor = BuildWindows(awayOverall.Select(value => value.ValueFor).ToArray(), config.DecayFactor);
        var awayAgainst = BuildWindows(awayOverall.Select(value => value.ValueAgainst).ToArray(), config.DecayFactor);
        var homeVenueFor = BotCStatistics.Describe(homeVenue.Select(value => value.ValueFor), config.DecayFactor);
        var homeVenueAgainst = BotCStatistics.Describe(homeVenue.Select(value => value.ValueAgainst), config.DecayFactor);
        var awayVenueFor = BotCStatistics.Describe(awayVenue.Select(value => value.ValueFor), config.DecayFactor);
        var awayVenueAgainst = BotCStatistics.Describe(awayVenue.Select(value => value.ValueAgainst), config.DecayFactor);

        var peerForAverage = input.LeagueForAverage ?? AverageFinite(homeFor.Last20.WeightedAverage, awayFor.Last20.WeightedAverage);
        var peerAgainstAverage = input.LeagueAgainstAverage ?? AverageFinite(homeAgainst.Last20.WeightedAverage, awayAgainst.Last20.WeightedAverage);
        var homeAdjustedFor = Adjust(homeFor.Last10.WeightedAverage, homeVenueFor.WeightedAverage, homeVenueFor.SampleCount, peerForAverage, config);
        var homeAdjustedAgainst = Adjust(homeAgainst.Last10.WeightedAverage, homeVenueAgainst.WeightedAverage, homeVenueAgainst.SampleCount, peerAgainstAverage, config);
        var awayAdjustedFor = Adjust(awayFor.Last10.WeightedAverage, awayVenueFor.WeightedAverage, awayVenueFor.SampleCount, peerForAverage, config);
        var awayAdjustedAgainst = Adjust(awayAgainst.Last10.WeightedAverage, awayVenueAgainst.WeightedAverage, awayVenueAgainst.SampleCount, peerAgainstAverage, config);
        var expectedHome = (homeAdjustedFor + awayAdjustedAgainst) / 2d;
        var expectedAway = (awayAdjustedFor + homeAdjustedAgainst) / 2d;
        var baseContextExpected = market.Scope switch
        {
            BotCMarketScope.Home => expectedHome,
            BotCMarketScope.Away => expectedAway,
            _ => expectedHome + expectedAway
        };
        var teamStrength = BotDTeamStrengthCalculator.Calculate(
            input.HomeTeam,
            input.AwayTeam,
            asOfUtc,
            input.TeamStrengthHistory,
            config.TeamStrength);
        risks.AddRange(teamStrength.RiskFlags);
        if (config.TeamStrength.Enabled && !teamStrength.IsAvailable)
        {
            risks.Add(BotCRiskFlags.TeamStrengthUnavailable);
        }
        var teamStrengthMarketWeight = market.Scope switch
        {
            BotCMarketScope.Home => config.TeamStrength.HomeTeamMarketWeight,
            BotCMarketScope.Away => config.TeamStrength.AwayTeamMarketWeight,
            _ => config.TeamStrength.TotalMarketWeight
        };
        var teamStrengthMetricSignal = market.Scope switch
        {
            BotCMarketScope.Home => teamStrength.AdjustedStrengthGap,
            BotCMarketScope.Away => -teamStrength.AdjustedStrengthGap,
            _ => Math.Abs(teamStrength.AdjustedStrengthGap)
        };
        var teamStrengthContextAdjustment = config.TeamStrength.Enabled && teamStrength.IsAvailable
            ? teamStrengthMetricSignal * teamStrengthMarketWeight * sigma
                * config.TeamStrength.ContextExpectedValueSigmaWeight
            : 0d;
        var contextExpected = Math.Max(0d, baseContextExpected + teamStrengthContextAdjustment);

        var homeLineValues = SelectLineValues(market.Scope, homeOverall, isHomeTeamHistory: true);
        var awayLineValues = SelectLineValues(market.Scope, awayOverall, isHomeTeamHistory: false);
        var combinedLineValues = homeLineValues.Concat(awayLineValues).ToArray();
        var leagueHitRate = combinedLineValues.Length == 0
            ? 0.5d
            : BotCStatistics.HitRate(combinedLineValues, line, selectedSide);
        var homeHitRate = BotCStatistics.ShrunkHitRate(homeLineValues, line, selectedSide, leagueHitRate, config.ShrinkageStrength);
        var awayHitRate = BotCStatistics.ShrunkHitRate(awayLineValues, line, selectedSide, leagueHitRate, config.ShrinkageStrength);
        var combinedHitRate = BotCStatistics.ShrunkHitRate(combinedLineValues, line, selectedSide, leagueHitRate, config.ShrinkageStrength);
        var lineSensitivity = new Dictionary<string, double>
        {
            ["lineMinus10"] = BotCStatistics.ShrunkHitRate(combinedLineValues, line - 1d, selectedSide, leagueHitRate, config.ShrinkageStrength),
            ["lineMinus05"] = BotCStatistics.ShrunkHitRate(combinedLineValues, line - 0.5d, selectedSide, leagueHitRate, config.ShrinkageStrength),
            ["exactLine"] = combinedHitRate,
            ["linePlus05"] = BotCStatistics.ShrunkHitRate(combinedLineValues, line + 0.5d, selectedSide, leagueHitRate, config.ShrinkageStrength),
            ["linePlus10"] = BotCStatistics.ShrunkHitRate(combinedLineValues, line + 1d, selectedSide, leagueHitRate, config.ShrinkageStrength)
        };

        var combinedStats = BotCStatistics.Describe(combinedLineValues, config.DecayFactor);
        var baseMargin = SelectionMargin(selectedSide, input.BasePredictedValue, line);
        var contextMargin = SelectionMargin(selectedSide, contextExpected, line);
        var historicalStd = Math.Max(config.MinimumStandardDeviation, combinedStats.StandardDeviation);
        var baseDistanceSigma = baseMargin / historicalStd;
        var contextDistanceSigma = contextMargin / historicalStd;
        var baseProbabilityOver = 1d - NormalDistribution.Cdf(line, input.BasePredictedValue, sigma);
        var baseRawProbability = selectedSide == "Over" ? baseProbabilityOver : 1d - baseProbabilityOver;
        var calibration = config.ResolveCalibration(market.Key, selectedSide);
        var baseCalibratedProbability = Calibrate(baseRawProbability, calibration.Intercept, calibration.Slope);
        var rawImpliedProbability = 1d / Convert.ToDouble(selectedOdds.Value);
        var marketNoVigProbability = NoVig(selectedSide, input.OverOdds, input.UnderOdds);
        var marketOverround = input.OverOdds is > 1m && input.UnderOdds is > 1m
            ? 1d / Convert.ToDouble(input.OverOdds.Value) + 1d / Convert.ToDouble(input.UnderOdds.Value) - 1d
            : 0d;
        var marketReference = marketNoVigProbability ?? rawImpliedProbability;
        var baseEdge = baseCalibratedProbability - marketReference;
        var baseExpectedValue = baseCalibratedProbability * Convert.ToDouble(selectedOdds.Value) - 1d;

        var trend = CalculateTrend(combinedLineValues);
        var supportSignals = new[]
        {
            baseMargin > 0,
            contextMargin > 0,
            SelectionMargin(selectedSide, combinedStats.Median, line) > 0,
            selectedSide == "Over" ? trend > 0 : trend < 0,
            combinedHitRate > marketReference,
            baseCalibratedProbability > marketReference
        };
        var agreementCount = supportSignals.Count(value => value);
        var agreementScore = agreementCount / (double)supportSignals.Length;
        var dataQuality = CalculateDataQuality(input, homeOverall, homeVenue, awayOverall, awayVenue, config, risks);
        var metaFeatures = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["baseCalibratedProbability"] = baseCalibratedProbability,
            ["odds"] = Convert.ToDouble(selectedOdds.Value),
            ["rawImpliedProbability"] = rawImpliedProbability,
            ["marketNoVigProbability"] = marketNoVigProbability ?? rawImpliedProbability,
            ["baseEdge"] = baseEdge,
            ["baseExpectedValue"] = baseExpectedValue,
            ["basePredictedValue"] = input.BasePredictedValue,
            ["line"] = line,
            ["baseLineMargin"] = baseMargin,
            ["baseLineDistanceSigma"] = baseDistanceSigma,
            ["contextExpectedValue"] = contextExpected,
            ["contextLineMargin"] = contextMargin,
            ["contextLineDistanceSigma"] = contextDistanceSigma,
            ["combinedExactLineShrunkHitRate"] = combinedHitRate,
            ["combinedMedian"] = combinedStats.Median,
            ["combinedStandardDeviation"] = combinedStats.StandardDeviation,
            ["combinedIqr"] = combinedStats.InterquartileRange,
            ["combinedMad"] = combinedStats.MedianAbsoluteDeviation,
            ["trend"] = trend,
            ["contextAgreementScore"] = agreementScore,
            ["dataQualityScore"] = dataQuality,
            ["teamStrengthAvailable"] = teamStrength.IsAvailable ? 1d : 0d,
            ["teamStrengthGap"] = teamStrength.AdjustedStrengthGap,
            ["teamStrengthConfidence"] = teamStrength.ConfidenceScore,
            ["teamStrengthEloSignal"] = teamStrength.EloSignal,
            ["teamStrengthDirectSignal"] = teamStrength.DirectMatchSignal,
            ["teamStrengthCommonOpponentSignal"] = teamStrength.CommonOpponentSignal,
            ["teamStrengthMarketSignal"] = teamStrengthMetricSignal,
            ["teamStrengthContextAdjustment"] = teamStrengthContextAdjustment
        };
        var metaPrediction = _metaModelPredictor.Predict(new BotCMetaModelInput(
            config.FeatureSchemaVersion,
            market.Key,
            selectedSide,
            metaFeatures));
        var metaModelTrainedThroughUtc = metaPrediction.TrainedThroughUtc;
        if (metaPrediction.IsAvailable
            && metaPrediction.TrainedThroughUtc.HasValue
            && EnsureUtc(metaPrediction.TrainedThroughUtc.Value) >= asOfUtc)
        {
            risks.Add(BotCRiskFlags.MetaModelTemporalLeakage);
            metaPrediction = BotCMetaModelPrediction.Unavailable(
                $"El meta-modelo fue entrenado hasta {metaPrediction.TrainedThroughUtc:yyyy-MM-dd HH:mm} UTC y no puede evaluar {asOfUtc:yyyy-MM-dd HH:mm} UTC sin leakage.");
        }
        var useMetaModel = metaPrediction.IsAvailable;
        if (!useMetaModel)
        {
            risks.Add(metaPrediction.UnavailableReason?.Contains("schema", StringComparison.OrdinalIgnoreCase) == true
                ? BotCRiskFlags.MetaModelSchemaMismatch
                : BotCRiskFlags.MetaModelUnavailable);
            if (!config.AllowRuleBasedFallback)
            {
                reasons.Add(risks.Contains(BotCRiskFlags.MetaModelTemporalLeakage)
                    ? BotCDecisionCodes.RejectedMetaModelTemporalLeakage
                    : metaPrediction.UnavailableReason?.Contains("schema", StringComparison.OrdinalIgnoreCase) == true
                        ? BotCDecisionCodes.RejectedModelSchemaMismatch
                        : BotCDecisionCodes.RejectedModelUnavailable);
                return EmptyDecision(
                    "Rejected",
                    input,
                    config,
                    risks,
                    reasons,
                    $"El meta-modelo no está disponible ({metaPrediction.UnavailableReason}) y el fallback está deshabilitado.",
                    selectedSide,
                    selectedOdds);
            }

            risks.Add(BotCRiskFlags.RuleBasedFallback);
        }
        var decisionEngine = useMetaModel ? MetaModelEngine : RuleBasedEngine;
        var probabilityBeforeTeamStrength = useMetaModel
            ? Clamp01(metaPrediction.Probability)
            : baseCalibratedProbability;
        var teamStrengthProbabilityAdjustment = !useMetaModel
            && config.TeamStrength.Enabled
            && teamStrength.IsAvailable
            ? (selectedSide == "Over" ? 1d : -1d)
                * teamStrengthMetricSignal
                * teamStrengthMarketWeight
                * config.TeamStrength.MaximumProbabilityAdjustment
            : 0d;
        var probabilityBeforeEmpiricalCalibration = Clamp01(
            probabilityBeforeTeamStrength + teamStrengthProbabilityAdjustment);
        var empiricalCalibration = BotEEmpiricalCalibrationCalculator.Calculate(
            asOfUtc,
            market.Key,
            selectedSide,
            selectedOdds.Value,
            input.BaseModelVersion,
            probabilityBeforeEmpiricalCalibration,
            marketNoVigProbability,
            input.CalibrationHistory,
            config.EmpiricalCalibration);
        if (config.EmpiricalCalibration.Enabled)
        {
            risks.AddRange(empiricalCalibration.RiskFlags);
            if (!empiricalCalibration.IsAvailable)
            {
                risks.Add(BotCRiskFlags.EmpiricalCalibrationUnavailable);
            }
        }
        if (config.EmpiricalCalibration.Enabled && empiricalCalibration.IsAvailable)
        {
            decisionEngine = EmpiricalCalibrationEngine;
        }
        var probabilityBeforeFootballIntelligence = config.EmpiricalCalibration.Enabled && empiricalCalibration.IsAvailable
            ? empiricalCalibration.ConservativeEquivalentProbability
            : probabilityBeforeEmpiricalCalibration;
        var footballIntelligence = FootballIntelligenceAdjustmentCalculator.Calculate(
            asOfUtc,
            market.Key,
            selectedSide,
            probabilityBeforeFootballIntelligence,
            input.FootballIntelligenceSnapshot,
            config.FootballIntelligence);
        var finalProbability = footballIntelligence.ProbabilityAfter;
        if (config.FootballIntelligence.Enabled)
        {
            risks.Add(footballIntelligence.IsApplied
                ? BotCRiskFlags.FootballIntelligenceApplied
                : BotCRiskFlags.FootballIntelligenceUnavailable);
            reasons.Add(footballIntelligence.IsApplied
                ? BotCDecisionCodes.ApprovedFootballIntelligence
                : BotCDecisionCodes.NeutralFootballIntelligence);
        }
        var finalEdge = finalProbability - marketReference;
        var expectedValueBeforeFootballIntelligence = config.EmpiricalCalibration.Enabled && empiricalCalibration.IsAvailable
            ? empiricalCalibration.ConservativeExpectedValue
            : probabilityBeforeFootballIntelligence * Convert.ToDouble(selectedOdds.Value) - 1d;
        var finalExpectedValue = finalProbability * Convert.ToDouble(selectedOdds.Value) - 1d;
        var contextLineComponent = Clamp01(0.5d + contextDistanceSigma / 4d);
        var edgeComponent = Clamp01((finalEdge + 0.05d) / 0.20d);
        var evComponent = Clamp01((finalExpectedValue + 0.05d) / 0.30d);
        var ruleScore = Clamp01(
            config.WeightCalibratedProbability * finalProbability
            + config.WeightEdge * edgeComponent
            + config.WeightExpectedValue * evComponent
            + config.WeightExactLineHitRate * combinedHitRate
            + config.WeightContextLineDistance * contextLineComponent
            + config.WeightContextAgreement * agreementScore
            + config.WeightDataQuality * dataQuality);

        var thresholdFailures = new List<string>();
        if (finalProbability < thresholds.MinimumFinalProbability)
            thresholdFailures.Add(config.EmpiricalCalibration.Enabled
                ? BotCDecisionCodes.RejectedProbability
                : useMetaModel ? BotCDecisionCodes.RejectedMetaProbability : BotCDecisionCodes.RejectedProbability);
        else
            reasons.Add(config.EmpiricalCalibration.Enabled
                ? BotCDecisionCodes.ApprovedEmpiricalCalibration
                : useMetaModel ? BotCDecisionCodes.ApprovedMetaProbability : BotCDecisionCodes.ApprovedProbability);
        if (finalEdge < thresholds.MinimumFinalEdge) thresholdFailures.Add(BotCDecisionCodes.RejectedEdge); else reasons.Add(BotCDecisionCodes.ApprovedEdge);
        if (finalExpectedValue < thresholds.MinimumFinalExpectedValue) thresholdFailures.Add(BotCDecisionCodes.RejectedExpectedValue); else reasons.Add(BotCDecisionCodes.ApprovedExpectedValue);
        if (dataQuality < thresholds.MinimumDataQualityScore) thresholdFailures.Add(BotCDecisionCodes.RejectedDataQuality);
        if (homeOverall.Count < thresholds.MinimumHistoricalMatches || awayOverall.Count < thresholds.MinimumHistoricalMatches) thresholdFailures.Add(BotCDecisionCodes.RejectedHistory);
        if (agreementScore < thresholds.MinimumContextAgreementScore || Math.Abs(input.BasePredictedValue - contextExpected) / historicalStd > config.MaximumBaseContextDistanceSigma) thresholdFailures.Add(BotCDecisionCodes.RejectedContext); else reasons.Add(BotCDecisionCodes.ApprovedContext);
        if (Convert.ToDouble(selectedOdds.Value) < thresholds.MinimumOdds || Convert.ToDouble(selectedOdds.Value) > thresholds.MaximumOdds) thresholdFailures.Add(BotCDecisionCodes.RejectedOdds);
        if (!useMetaModel && ruleScore < config.MinimumRuleBasedConfidenceScore) thresholdFailures.Add(BotCDecisionCodes.RejectedRuleScore);
        if (config.TeamStrength.Enabled
            && (!teamStrength.IsAvailable
                || teamStrength.ConfidenceScore < config.TeamStrength.MinimumConfidenceScore))
        {
            thresholdFailures.Add(BotCDecisionCodes.RejectedTeamStrength);
        }
        if (config.EmpiricalCalibration.Enabled && !empiricalCalibration.IsAvailable)
        {
            thresholdFailures.Add(BotCDecisionCodes.RejectedCalibrationUnavailable);
        }
        else if (config.EmpiricalCalibration.Enabled
                 && empiricalCalibration.Reliability < config.EmpiricalCalibration.MinimumReliability)
        {
            thresholdFailures.Add(BotCDecisionCodes.RejectedCalibrationReliability);
        }
        else if (config.EmpiricalCalibration.Enabled)
        {
            reasons.Add(BotCDecisionCodes.ApprovedEmpiricalCalibration);
        }
        else if (config.TeamStrength.Enabled)
        {
            reasons.Add(BotCDecisionCodes.ApprovedTeamStrength);
        }
        if (combinedHitRate >= marketReference) reasons.Add(BotCDecisionCodes.ApprovedExactLine);
        reasons.AddRange(thresholdFailures);
        var decision = thresholds.Enabled && thresholdFailures.Count == 0 ? "Approved" : "Rejected";
        if (!thresholds.Enabled)
        {
            reasons.Add("REJECTED_SELECTOR_DISABLED");
        }

        var snapshot = new
        {
            featureSchemaVersion = config.FeatureSchemaVersion,
            configurationVersion = config.ConfigurationVersion,
            predictionTimestampUtc = EnsureUtc(input.PredictionTimestampUtc ?? input.OddsCapturedAtUtc),
            asOfDateUtc = asOfUtc,
            oddsCapturedAtUtc = EnsureUtc(input.OddsCapturedAtUtc),
            leakageGuard = new { strictBeforeAsOf = true, inputRows = input.HomeOverall.Count + input.AwayOverall.Count, acceptedRows = homeOverall.Count + awayOverall.Count },
            market = new { input.MarketType, market.Scope, input.Line, selectedSide, selectedOdds, oppositeOdds },
            model = new { input.BaseModelName, input.BaseModelVersion, input.BaseModelTrainedThroughUtc, input.BasePredictedValue, sigma, baseRawProbability, baseCalibratedProbability, calibration },
            metaModel = new { available = useMetaModel, metaPrediction.ModelName, metaPrediction.ModelVersion, trainedThroughUtc = metaModelTrainedThroughUtc, metaPrediction.UnavailableReason, numericFeatures = metaFeatures },
            marketProbability = new { rawImpliedProbability, marketNoVigProbability, marketOverround, baseEdge, baseExpectedValue, finalProbability, finalEdge, finalExpectedValue },
            history = new
            {
                home = new { overallCount = homeOverall.Count, venueCount = homeVenue.Count, forWindows = homeFor, againstWindows = homeAgainst, venueFor = homeVenueFor, venueAgainst = homeVenueAgainst },
                away = new { overallCount = awayOverall.Count, venueCount = awayVenue.Count, forWindows = awayFor, againstWindows = awayAgainst, venueFor = awayVenueFor, venueAgainst = awayVenueAgainst },
                combined = combinedStats
            },
            shrinkage = new { peerForAverage, peerAgainstAverage, config.ShrinkageStrength },
            context = new { homeAdjustedFor, homeAdjustedAgainst, awayAdjustedFor, awayAdjustedAgainst, expectedHome, expectedAway, baseContextExpected, teamStrengthContextAdjustment, contextExpected },
            teamStrength = new
            {
                enabled = config.TeamStrength.Enabled,
                config.TeamStrength.Version,
                result = teamStrength,
                marketWeight = teamStrengthMarketWeight,
                metricSignal = teamStrengthMetricSignal,
                probabilityBeforeTeamStrength,
                probabilityAdjustment = teamStrengthProbabilityAdjustment
            },
            empiricalCalibration = new
            {
                enabled = config.EmpiricalCalibration.Enabled,
                config.EmpiricalCalibration.Version,
                sourceBot = config.EmpiricalCalibration.SourceBotKey,
                probabilityBeforeEmpiricalCalibration,
                result = empiricalCalibration
            },
            footballIntelligence = new
            {
                enabled = config.FootballIntelligence.Enabled,
                config.FootballIntelligence.Version,
                probabilityBeforeFootballIntelligence,
                expectedValueBeforeFootballIntelligence,
                result = footballIntelligence
            },
            lineDistance = new { baseMargin, contextMargin, historicalStd, baseDistanceSigma, contextDistanceSigma },
            hitRates = new { homeHitRate, awayHitRate, combinedHitRate, leagueHitRate, lineSensitivity },
            trend = new { combined = trend, normalized = trend / historicalStd },
            agreement = new { agreementCount, disagreementCount = supportSignals.Length - agreementCount, agreementScore, supportSignals },
            quality = new { dataQuality, risks },
            selector = new { engine = decisionEngine, ruleScore, thresholds, thresholdFailures, reasons },
            configuration = config
        };
        var featureSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        var probabilityLabel = config.EmpiricalCalibration.Enabled
            ? "probabilidad equivalente empírica conservadora"
            : useMetaModel ? "meta-probabilidad" : "probabilidad base calibrada";
        var strengthLabel = config.TeamStrength.Enabled
            ? $", gap nivel {teamStrength.AdjustedStrengthGap:+0.000;-0.000;0.000} (confianza {teamStrength.ConfidenceScore:P0}, ajuste prob. {teamStrengthProbabilityAdjustment:+0.0%;-0.0%;0.0%})"
            : string.Empty;
        var calibrationLabel = config.EmpiricalCalibration.Enabled
            ? empiricalCalibration.IsAvailable
                ? $", calibración {empiricalCalibration.EvidenceTier} n_eff={empiricalCalibration.EffectiveSampleSize:0.0}, reliability {empiricalCalibration.Reliability:P0}"
                : ", calibración empírica sin evidencia suficiente"
            : string.Empty;
        var intelligenceLabel = config.FootballIntelligence.Enabled
            ? footballIntelligence.IsApplied
                ? $", inteligencia pre-partido {footballIntelligence.ProbabilityAdjustment:+0.0%;-0.0%;0.0%}"
                : ", inteligencia pre-partido neutral (sin evidencia utilizable)"
            : string.Empty;
        var summary = $"{decision} por {decisionEngine}: {probabilityLabel} {finalProbability:P1}, mercado {marketReference:P1}, edge {finalEdge:P1}, EV {finalExpectedValue:P1}, contexto {contextExpected:0.00} vs línea {line:0.##}, hit rate {combinedHitRate:P1}, calidad {dataQuality:0.00}{strengthLabel}{calibrationLabel}{intelligenceLabel}.";

        return new BotCPickDecision(
            decision, decisionEngine, selectedSide, selectedOdds, baseRawProbability,
            baseCalibratedProbability, rawImpliedProbability, marketNoVigProbability,
            marketOverround, finalProbability, finalEdge, finalExpectedValue, ruleScore,
            contextExpected, agreementScore, dataQuality, baseMargin, contextMargin,
            baseDistanceSigma, contextDistanceSigma, combinedHitRate, ruleScore,
            reasons.Distinct(StringComparer.Ordinal).ToArray(), risks.Distinct(StringComparer.Ordinal).ToArray(),
            summary, config.FeatureSchemaVersion, config.ConfigurationVersion, featureSnapshotJson);
    }

    private static BotCPickDecision EmptyDecision(
        string decision,
        BotCPickEvaluationInput input,
        BotCStrategyConfiguration config,
        IReadOnlyList<string> risks,
        IReadOnlyList<string> reasons,
        string summary,
        string selectedSide = "",
        decimal? selectedOdds = null)
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            featureSchemaVersion = config.FeatureSchemaVersion,
            configurationVersion = config.ConfigurationVersion,
            predictionTimestampUtc = EnsureUtc(input.PredictionTimestampUtc ?? input.OddsCapturedAtUtc),
            input.MarketType,
            input.Line,
            input.AsOfDateUtc,
            input.BaseModelTrainedThroughUtc,
            decision,
            reasons,
            risks
        }, JsonOptions);
        return new BotCPickDecision(
            Decision: decision,
            DecisionEngineType: RuleBasedEngine,
            SelectedSide: selectedSide,
            SelectedOdds: selectedOdds,
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
            FeatureSchemaVersion: config.FeatureSchemaVersion,
            ConfigurationVersion: config.ConfigurationVersion,
            FeatureSnapshotJson: snapshot);
    }

    private static IReadOnlyList<BotCHistoricalObservation> Before(IEnumerable<BotCHistoricalObservation> rows, DateTime asOfUtc) =>
        rows.Where(row => EnsureUtc(row.MatchDateUtc) < asOfUtc)
            .OrderByDescending(row => row.MatchDateUtc)
            .ToArray();

    private static BotCWindowStatistics BuildWindows(IReadOnlyList<double> values, double decay) =>
        new(
            BotCStatistics.Describe(values.Take(5), decay),
            BotCStatistics.Describe(values.Take(10), decay),
            BotCStatistics.Describe(values.Take(20), decay));

    private static double Adjust(double overall, double venue, int venueMatches, double baseline, BotCStrategyConfiguration config)
    {
        var venueWeight = Math.Min(venueMatches / (double)config.RequiredVenueMatches, config.MaximumVenueWeight);
        var blended = venueMatches == 0 ? overall : venueWeight * venue + (1d - venueWeight) * overall;
        return BotCStatistics.Shrink(blended, Math.Max(1, venueMatches), baseline, config.ShrinkageStrength);
    }

    private static double[] SelectLineValues(BotCMarketScope scope, IReadOnlyList<BotCHistoricalObservation> rows, bool isHomeTeamHistory) =>
        rows.Select(row => scope switch
        {
            BotCMarketScope.Total => row.ValueFor + row.ValueAgainst,
            BotCMarketScope.Home => isHomeTeamHistory ? row.ValueFor : row.ValueAgainst,
            BotCMarketScope.Away => isHomeTeamHistory ? row.ValueAgainst : row.ValueFor,
            _ => row.ValueFor + row.ValueAgainst
        }).Where(double.IsFinite).ToArray();

    private static double CalculateTrend(IReadOnlyList<double> values)
    {
        var recent = values.Take(5).ToArray();
        var previous = values.Skip(5).Take(5).ToArray();
        return recent.Length == 0 || previous.Length == 0 ? 0d : recent.Average() - previous.Average();
    }

    private static double CalculateDataQuality(
        BotCPickEvaluationInput input,
        IReadOnlyList<BotCHistoricalObservation> homeOverall,
        IReadOnlyList<BotCHistoricalObservation> homeVenue,
        IReadOnlyList<BotCHistoricalObservation> awayOverall,
        IReadOnlyList<BotCHistoricalObservation> awayVenue,
        BotCStrategyConfiguration config,
        ICollection<string> risks)
    {
        var overall = Clamp01(Math.Min(homeOverall.Count, awayOverall.Count) / 20d);
        var venue = Clamp01(Math.Min(homeVenue.Count, awayVenue.Count) / (double)config.RequiredVenueMatches);
        var latest = homeOverall.Concat(awayOverall).OrderByDescending(row => row.MatchDateUtc).FirstOrDefault();
        var days = latest is null ? 365d : Math.Max(0, (EnsureUtc(input.AsOfDateUtc) - EnsureUtc(latest.MatchDateUtc)).TotalDays);
        var freshness = days <= 14d ? 1d : Clamp01(1d - (days - 14d) / 76d);
        if (freshness < 0.5d) risks.Add(BotCRiskFlags.StaleHistoricalData);
        var completeness = new[] { homeOverall.Count > 0, awayOverall.Count > 0, homeVenue.Count > 0, awayVenue.Count > 0 }.Count(value => value) / 4d;
        var marketData = input.OverOdds is > 1m && input.UnderOdds is > 1m ? 1d : 0.5d;
        var consistency = input.Line >= 0 && input.OverOdds is > 1m || input.UnderOdds is > 1m ? 1d : 0d;
        return Clamp01(
            config.QualityOverallSampleWeight * overall
            + config.QualityVenueSampleWeight * venue
            + config.QualityFreshnessWeight * freshness
            + config.QualityFeatureCompletenessWeight * completeness
            + config.QualityMarketDataWeight * marketData
            + config.QualityConsistencyWeight * consistency);
    }

    private static double Calibrate(double rawProbability, double intercept, double slope)
    {
        var bounded = Math.Clamp(rawProbability, 0.000001d, 0.999999d);
        var logit = Math.Log(bounded / (1d - bounded));
        return Clamp01(1d / (1d + Math.Exp(-(intercept + slope * logit))));
    }

    private static double? NoVig(string selectedSide, decimal? overOdds, decimal? underOdds)
    {
        if (overOdds is not > 1m || underOdds is not > 1m) return null;
        var over = 1d / Convert.ToDouble(overOdds.Value);
        var under = 1d / Convert.ToDouble(underOdds.Value);
        return selectedSide == "Over" ? over / (over + under) : under / (over + under);
    }

    private static double SelectionMargin(string side, double value, double line) =>
        side == "Over" ? value - line : line - value;

    private static double AverageFinite(params double[] values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        return finite.Length == 0 ? 0d : finite.Average();
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static double Clamp01(double value) => Math.Clamp(double.IsFinite(value) ? value : 0d, 0d, 1d);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

public sealed record BotCWindowStatistics(
    BotCDistributionStatistics Last5,
    BotCDistributionStatistics Last10,
    BotCDistributionStatistics Last20);

public static class BotCStatistics
{
    public static BotCDistributionStatistics Describe(IEnumerable<double> values, double decayFactor)
    {
        var data = values.Where(double.IsFinite).ToArray();
        if (data.Length == 0) return new BotCDistributionStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var average = data.Average();
        var variance = data.Average(value => Math.Pow(value - average, 2));
        var p25 = Percentile(data, 0.25d);
        var p75 = Percentile(data, 0.75d);
        var median = Median(data);
        return new BotCDistributionStatistics(
            data.Length, average, WeightedAverage(data, decayFactor), median,
            Math.Sqrt(variance), variance, data.Min(), data.Max(), p25, p75,
            p75 - p25, Median(data.Select(value => Math.Abs(value - median))));
    }

    public static double WeightedAverage(IReadOnlyList<double> values, double decayFactor)
    {
        if (values.Count == 0) return 0d;
        var weighted = 0d;
        var weights = 0d;
        for (var index = 0; index < values.Count; index++)
        {
            var weight = Math.Pow(decayFactor, index);
            weighted += values[index] * weight;
            weights += weight;
        }
        return weights == 0 ? 0d : weighted / weights;
    }

    public static double Median(IEnumerable<double> values) => Percentile(values, 0.5d);

    public static double Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.Where(double.IsFinite).Order().ToArray();
        if (ordered.Length == 0) return 0d;
        var position = Math.Clamp(percentile, 0d, 1d) * (ordered.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper ? ordered[lower] : ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }

    public static double Shrink(double teamValue, int sampleCount, double leagueValue, double strength)
    {
        var denominator = sampleCount + strength;
        return denominator <= 0 ? teamValue : sampleCount / denominator * teamValue + strength / denominator * leagueValue;
    }

    public static double HitRate(IEnumerable<double> values, double line, string side)
    {
        var data = values.Where(double.IsFinite).ToArray();
        if (data.Length == 0) return 0d;
        var hits = data.Count(value => side == "Over" ? value > line : value < line);
        return hits / (double)data.Length;
    }

    public static double ShrunkHitRate(IEnumerable<double> values, double line, string side, double leagueHitRate, double strength)
    {
        var data = values.Where(double.IsFinite).ToArray();
        var hits = data.Count(value => side == "Over" ? value > line : value < line);
        return (hits + strength * leagueHitRate) / (data.Length + strength);
    }
}

public enum BotCMarketScope { Total, Home, Away }

public sealed record BotCMarketDefinition(string Key, BotCMarketScope Scope)
{
    public static BotCMarketDefinition Parse(string marketType) => marketType switch
    {
        "TotalCorners" or "CornersTotal" => new("TotalCorners", BotCMarketScope.Total),
        "HomeTeamCorners" or "CornersHomeTeam" => new("HomeTeamCorners", BotCMarketScope.Home),
        "AwayTeamCorners" or "CornersAwayTeam" => new("AwayTeamCorners", BotCMarketScope.Away),
        "TotalGoals" or "GoalsTotal" => new("TotalGoals", BotCMarketScope.Total),
        "HomeTeamGoals" or "GoalsHomeTeam" => new("HomeTeamGoals", BotCMarketScope.Home),
        "AwayTeamGoals" or "GoalsAwayTeam" => new("AwayTeamGoals", BotCMarketScope.Away),
        "TotalShots" or "ShotsTotal" => new("TotalShots", BotCMarketScope.Total),
        "HomeTeamShots" or "ShotsHomeTeam" => new("HomeTeamShots", BotCMarketScope.Home),
        "AwayTeamShots" or "ShotsAwayTeam" => new("AwayTeamShots", BotCMarketScope.Away),
        "TotalShotsOnGoal" or "ShotsOnTargetTotal" => new("TotalShotsOnGoal", BotCMarketScope.Total),
        "HomeTeamShotsOnGoal" or "ShotsOnTargetHomeTeam" => new("HomeTeamShotsOnGoal", BotCMarketScope.Home),
        "AwayTeamShotsOnGoal" or "ShotsOnTargetAwayTeam" => new("AwayTeamShotsOnGoal", BotCMarketScope.Away),
        _ => throw new ArgumentException($"Bot C does not support market '{marketType}'.")
    };
}

internal static class NormalDistribution
{
    public static double Cdf(double x, double mean, double sigma)
    {
        if (sigma <= 0) return x < mean ? 0d : 1d;
        var z = (x - mean) / (sigma * Math.Sqrt(2d));
        var sign = z < 0 ? -1d : 1d;
        var absolute = Math.Abs(z);
        var t = 1d / (1d + 0.3275911d * absolute);
        var y = 1d - (((((1.061405429d * t - 1.453152027d) * t) + 1.421413741d) * t - 0.284496736d) * t + 0.254829592d) * t * Math.Exp(-absolute * absolute);
        return 0.5d * (1d + sign * y);
    }
}
