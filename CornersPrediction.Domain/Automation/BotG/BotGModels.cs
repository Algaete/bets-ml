using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPrediction.Domain.Automation.BotG;

public sealed record BotGMarketQuote(
    long FixtureId,
    DateTime FixtureDateUtc,
    DateTime PredictionTimestampUtc,
    DateTime OddsTimestampUtc,
    string League,
    string Season,
    string HomeTeam,
    string AwayTeam,
    string Bookmaker,
    BotGMarketType MarketType,
    BotGSelection Selection,
    decimal Line,
    decimal? OverOdds,
    decimal? UnderOdds,
    long? SourceOddsId = null)
{
    public const string MarketFamily = "GOALS";
    public decimal? SelectedOdds => Selection == BotGSelection.Over ? OverOdds : UnderOdds;
    public decimal? OppositeOdds => Selection == BotGSelection.Over ? UnderOdds : OverOdds;
}

public sealed record BotGHistoryObservation(
    long FixtureId,
    DateTime MatchDateUtc,
    double ValueFor,
    double ValueAgainst);

public sealed record BotGBasePredictions
{
    public double LegacyTotal { get; init; }
    public double LegacyHome { get; init; }
    public double LegacyAway { get; init; }
    public double Model2026Total { get; init; }
    public double Model2026Home { get; init; }
    public double Model2026Away { get; init; }
    public string LegacyModelVersion { get; init; } = "goals_v1";
    public string Model2026Version { get; init; } = "goals_deep_tuned_v2";
    public DateTime? LegacyTrainedThroughUtc { get; init; }
    public DateTime? Model2026TrainedThroughUtc { get; init; }

    public double LegacyFor(BotGMarketType market) => market switch
    {
        BotGMarketType.HomeTeamGoals => LegacyHome,
        BotGMarketType.AwayTeamGoals => LegacyAway,
        _ => LegacyTotal
    };

    public double Model2026For(BotGMarketType market) => market switch
    {
        BotGMarketType.HomeTeamGoals => Model2026Home,
        BotGMarketType.AwayTeamGoals => Model2026Away,
        _ => Model2026Total
    };
}

public sealed record BotGFeatureBuildInput(
    BotGMarketQuote Quote,
    BotGBasePredictions Predictions,
    IReadOnlyList<BotGHistoryObservation> HomeOverall,
    IReadOnlyList<BotGHistoryObservation> HomeVenue,
    IReadOnlyList<BotGHistoryObservation> AwayOverall,
    IReadOnlyList<BotGHistoryObservation> AwayVenue,
    BotGMarketProbabilityResult MarketProbability,
    double? ExactLineHitRate = null,
    double? NeighborLowerHitRate = null,
    double? NeighborUpperHitRate = null,
    double? HistoricalPushRate = null,
    int ExactLineHistoricalSampleSize = 0);

public sealed record BotGDistributionStatistics(
    int SampleCount,
    double Mean,
    double WeightedMean,
    double Median,
    double StandardDeviation,
    double Variance,
    double Percentile25,
    double Percentile75,
    double InterquartileRange,
    double MedianAbsoluteDeviation,
    double Minimum,
    double Maximum)
{
    public static BotGDistributionStatistics Empty { get; } =
        new(0, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d);
}

public sealed record BotGWindowStatistics(
    BotGDistributionStatistics Last5,
    BotGDistributionStatistics Last10,
    BotGDistributionStatistics Last20);

public sealed record BotGFeatures
{
    public string FeatureSchemaVersion { get; init; } = BotGConfiguration.DefaultFeatureSchemaVersion;
    public DateTime AsOfDateUtc { get; init; }
    public BotGMarketType MarketType { get; init; }
    public BotGSelection Selection { get; init; }
    public string Bookmaker { get; init; } = string.Empty;
    public double Line { get; init; }
    public double SelectedOdds { get; init; }
    public double OppositeOdds { get; init; }
    public double RawImpliedProbability { get; init; }
    public double MarketNoVigProbability { get; init; }
    public double OddsMargin { get; init; }
    public double OddsAgeMinutes { get; init; }
    public double LegacyPrediction { get; init; }
    public double Prediction2026 { get; init; }
    public double LegacyMinus2026 { get; init; }
    public double AveragePrediction { get; init; }
    public double PredictionMinusLine { get; init; }
    public double AbsPredictionMinusLine { get; init; }
    public double TotalPrediction2026 { get; init; }
    public double HomePrediction2026 { get; init; }
    public double AwayPrediction2026 { get; init; }
    public double HomePlusAway2026 { get; init; }
    public double DirectTotalMinusHomeAway { get; init; }
    public double ContextPrediction { get; init; }
    public double ModelVsContextDistance { get; init; }
    public double ModelVsContextSigma { get; init; }
    public double ContextAgreementScore { get; init; }
    public int HistoryCount { get; init; }
    public int VenueHistoryCount { get; init; }
    public int MissingFeaturesCount { get; init; }
    public double DataQualityScore { get; init; }
    public double ModelDisagreement { get; init; }
    public double ExactLineHitRate { get; init; }
    public double NeighborLowerHitRate { get; init; }
    public double NeighborUpperHitRate { get; init; }
    public double HistoricalPushRate { get; init; }
    public int ExactLineHistoricalSampleSize { get; init; }
    public BotGWindowStatistics Overall { get; init; } = new(
        BotGDistributionStatistics.Empty,
        BotGDistributionStatistics.Empty,
        BotGDistributionStatistics.Empty);
    public BotGWindowStatistics Venue { get; init; } = new(
        BotGDistributionStatistics.Empty,
        BotGDistributionStatistics.Empty,
        BotGDistributionStatistics.Empty);

    public IReadOnlyDictionary<string, double> ToNumericVector()
    {
        var vector = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketNoVigProbability"] = MarketNoVigProbability,
            ["rawImpliedProbability"] = RawImpliedProbability,
            ["selectedOdds"] = SelectedOdds,
            ["oppositeOdds"] = OppositeOdds,
            ["oddsMargin"] = OddsMargin,
            ["oddsAgeMinutes"] = OddsAgeMinutes,
            ["line"] = Line,
            ["legacyPrediction"] = LegacyPrediction,
            ["prediction2026"] = Prediction2026,
            ["legacyMinus2026"] = LegacyMinus2026,
            ["averagePrediction"] = AveragePrediction,
            ["predictionMinusLine"] = PredictionMinusLine,
            ["absPredictionMinusLine"] = AbsPredictionMinusLine,
            ["totalPrediction2026"] = TotalPrediction2026,
            ["homePrediction2026"] = HomePrediction2026,
            ["awayPrediction2026"] = AwayPrediction2026,
            ["homePlusAway2026"] = HomePlusAway2026,
            ["directTotalMinusHomeAway"] = DirectTotalMinusHomeAway,
            ["contextPrediction"] = ContextPrediction,
            ["modelVsContextDistance"] = ModelVsContextDistance,
            ["modelVsContextSigma"] = ModelVsContextSigma,
            ["contextAgreementScore"] = ContextAgreementScore,
            ["historyCount"] = HistoryCount,
            ["venueHistoryCount"] = VenueHistoryCount,
            ["missingFeaturesCount"] = MissingFeaturesCount,
            ["dataQualityScore"] = DataQualityScore,
            ["modelDisagreement"] = ModelDisagreement,
            ["exactLineHitRate"] = ExactLineHitRate,
            ["neighborLowerHitRate"] = NeighborLowerHitRate,
            ["neighborUpperHitRate"] = NeighborUpperHitRate,
            ["historicalPushRate"] = HistoricalPushRate,
            ["exactLineHistoricalSampleSize"] = ExactLineHistoricalSampleSize
        };
        AddStatistics(vector, "overallLast5", Overall.Last5);
        AddStatistics(vector, "overallLast10", Overall.Last10);
        AddStatistics(vector, "overallLast20", Overall.Last20);
        AddStatistics(vector, "venueLast5", Venue.Last5);
        AddStatistics(vector, "venueLast10", Venue.Last10);
        AddStatistics(vector, "venueLast20", Venue.Last20);
        return vector;
    }

    private static void AddStatistics(
        IDictionary<string, double> vector,
        string prefix,
        BotGDistributionStatistics statistics)
    {
        vector[$"{prefix}SampleCount"] = statistics.SampleCount;
        vector[$"{prefix}Mean"] = statistics.Mean;
        vector[$"{prefix}WeightedMean"] = statistics.WeightedMean;
        vector[$"{prefix}Median"] = statistics.Median;
        vector[$"{prefix}StandardDeviation"] = statistics.StandardDeviation;
        vector[$"{prefix}Variance"] = statistics.Variance;
        vector[$"{prefix}P25"] = statistics.Percentile25;
        vector[$"{prefix}P75"] = statistics.Percentile75;
        vector[$"{prefix}Iqr"] = statistics.InterquartileRange;
        vector[$"{prefix}Mad"] = statistics.MedianAbsoluteDeviation;
        vector[$"{prefix}Minimum"] = statistics.Minimum;
        vector[$"{prefix}Maximum"] = statistics.Maximum;
    }
}

public sealed record BotGMarketProbabilityResult(
    bool IsAvailable,
    double RawImpliedOver,
    double RawImpliedUnder,
    double NoVigOver,
    double NoVigUnder,
    double SelectedRawImpliedProbability,
    double SelectedNoVigProbability,
    double Overround,
    string? UnavailableReason = null)
{
    public static BotGMarketProbabilityResult Unavailable(string reason) =>
        new(false, 0d, 0d, 0d, 0d, 0d, 0d, 0d, reason);
}

public sealed record BotGOutcomeDistribution(
    double Win,
    double HalfWin,
    double Push,
    double HalfLoss,
    double Loss)
{
    public double Total => Win + HalfWin + Push + HalfLoss + Loss;
    public double PositiveReturnProbability => Win + HalfWin;
    public double NegativeReturnProbability => Loss + HalfLoss;

    public static BotGOutcomeDistribution Validate(BotGOutcomeDistribution value)
    {
        var values = new[] { value.Win, value.HalfWin, value.Push, value.HalfLoss, value.Loss };
        if (values.Any(item => !double.IsFinite(item) || item < 0d || item > 1d)
            || Math.Abs(values.Sum() - 1d) > 0.000001d)
            throw new ArgumentException("Bot G settlement probabilities must be finite, non-negative and add up to 1.0.");
        return value;
    }

    public static BotGOutcomeDistribution Binary(double probability) =>
        Validate(new BotGOutcomeDistribution(Math.Clamp(probability, 0d, 1d), 0d, 0d, 0d, 1d - Math.Clamp(probability, 0d, 1d)));
}

public sealed record BotGMetaModelInput(
    string FeatureSchemaVersion,
    DateTime PredictionTimestampUtc,
    BotGMarketType MarketType,
    BotGSelection Selection,
    string Bookmaker,
    double MarketNoVigProbability,
    IReadOnlyDictionary<string, double> NumericFeatures,
    BotGConfiguration RuntimeConfiguration,
    string LegacyModelVersion,
    string Model2026Version,
    string League = "",
    decimal Line = 0m);

public sealed record BotGMetaModelPrediction(
    bool IsAvailable,
    double Probability,
    double ResidualLogit,
    string ModelVersion,
    string FeatureSchemaVersion,
    DateTime? TrainedThroughUtc,
    double EnsembleDispersion,
    BotGOutcomeDistribution? SettlementDistribution = null,
    string? UnavailableReason = null)
{
    public static BotGMetaModelPrediction Unavailable(string reason) =>
        new(false, 0d, 0d, string.Empty, string.Empty, null, 0d, null, reason);
}

public sealed record BotGCalibrationKey(
    string Family,
    BotGMarketType? MarketType,
    BotGSelection? Selection,
    string? Bookmaker)
{
    public static BotGCalibrationKey GlobalGoals { get; } = new("GOALS", null, null, null);
}

public sealed record BotGCalibrationProfile
{
    public required BotGCalibrationKey Key { get; init; }
    public required string Version { get; init; }
    public string Method { get; init; } = "BetaCalibration";
    public double Intercept { get; init; }
    public double LogProbabilityCoefficient { get; init; } = 1d;
    public double LogOneMinusProbabilityCoefficient { get; init; } = -1d;
    public int SampleSize { get; init; }
    public double EffectiveSampleSize { get; init; }
    public DateTime EvidenceAvailableThroughUtc { get; init; }
}

public sealed record BotGCalibrationResult(
    bool IsAvailable,
    double InputProbability,
    double CalibratedProbability,
    double Reliability,
    double EffectiveSampleSize,
    string Version,
    BotGCalibrationLevel? MostSpecificLevel,
    IReadOnlyList<BotGCalibrationLevel> AppliedLevels,
    string? UnavailableReason = null)
{
    public static BotGCalibrationResult Unavailable(double probability, string reason) =>
        new(false, probability, probability, 0d, 0d, string.Empty, null, [], reason);
}

public sealed record BotGUncertaintyResult(
    double FinalProbability,
    double ProbabilityLowerBound,
    double ProbabilityUpperBound,
    double ProbabilityUncertainty,
    double ConservativeProbability,
    string Version);

public sealed record BotGOodFeatureReference(
    string Name,
    double Median,
    double MedianAbsoluteDeviation,
    double Percentile01,
    double Percentile99,
    int SampleSize);

public sealed record BotGOodResult(
    bool IsAvailable,
    double Score,
    IReadOnlyDictionary<string, double> RobustZScores,
    IReadOnlyList<string> OutlyingFeatures,
    string Version,
    string? UnavailableReason = null);

public sealed record BotGExpectedValueResult(
    BotGOutcomeDistribution Distribution,
    double ExpectedProfitPerUnit,
    double PositiveReturnProbability,
    double NegativeReturnProbability);

public sealed record BotGDecision(
    BotGDecisionStatus Status,
    BotGDecisionReason PrimaryReason,
    IReadOnlyList<BotGDecisionReason> Reasons,
    string Explanation)
{
    public bool IsApproved => Status == BotGDecisionStatus.Approved;
}

public sealed record BotGDecisionInput
{
    public required BotGMarketQuote Quote { get; init; }
    public required BotGMarketProbabilityResult MarketProbability { get; init; }
    public required BotGMetaModelPrediction MetaPrediction { get; init; }
    public required BotGCalibrationResult Calibration { get; init; }
    public required BotGUncertaintyResult Uncertainty { get; init; }
    public required BotGOodResult OutOfDistribution { get; init; }
    public bool SettlementDistributionAvailable { get; init; }
    public double FinalProbability { get; init; }
    public double ConservativeEdge { get; init; }
    public double ConservativeExpectedValue { get; init; }
    public double DataQualityScore { get; init; }
    public double ContextAgreementScore { get; init; }
    public double ModelDisagreement { get; init; }
    public int HistoricalMatches { get; init; }
}

public sealed record BotGCandidate
{
    public long CandidateId { get; init; }
    public Guid CandidateUuid { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public long FixtureId { get; init; }
    public long? OfficialFixtureId { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public DateTime OddsTimestampUtc { get; init; }
    public long? SourceOddsId { get; init; }
    public string BotKey { get; init; } = BotGConfiguration.DefaultBotKey;
    public string AutomationVersion { get; init; } = $"{BotGConfiguration.DefaultConfigurationVersion}-G2026";
    public string ConfigurationVersion { get; init; } = BotGConfiguration.DefaultConfigurationVersion;
    public string FeatureSchemaVersion { get; init; } = BotGConfiguration.DefaultFeatureSchemaVersion;
    public string League { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Bookmaker { get; init; } = string.Empty;
    public string MarketFamily { get; init; } = "GOALS";
    public BotGMarketType MarketType { get; init; }
    public BotGSelection Selection { get; init; }
    public decimal Line { get; init; }
    public decimal? OverOdds { get; init; }
    public decimal? UnderOdds { get; init; }
    public decimal SelectedOdds { get; init; }
    public double RawImpliedProbability { get; init; }
    public double NoVigMarketProbability { get; init; }
    public double LegacyPrediction { get; init; }
    public double Prediction2026 { get; init; }
    public double ContextPrediction { get; init; }
    public double HistoricalMean { get; init; }
    public double HistoricalMedian { get; init; }
    public double HistoricalStandardDeviation { get; init; }
    public double PredictionMinusLine { get; init; }
    public double LegacyMinusMarketEquivalent { get; init; }
    public double Model2026MinusMarketEquivalent { get; init; }
    public double CandidateProbability { get; init; }
    public double CalibratedProbability { get; init; }
    public double FinalProbability { get; init; }
    public double ProbabilityLowerBound { get; init; }
    public double ProbabilityUpperBound { get; init; }
    public double ProbabilityUncertainty { get; init; }
    public double UncertaintyScore => ProbabilityUncertainty;
    public double ConservativeProbability { get; init; }
    public double Edge { get; init; }
    public double ConservativeEdge { get; init; }
    public double ExpectedValue { get; init; }
    public double ConservativeExpectedValue { get; init; }
    public double DataQualityScore { get; init; }
    public double ContextAgreementScore { get; init; }
    public double CalibrationReliability { get; init; }
    public double OutOfDistributionScore { get; init; }
    public double ModelDisagreement { get; init; }
    public double GSelectionScore { get; init; }
    public BotGDecisionStatus Decision { get; init; } = BotGDecisionStatus.Abstain;
    public BotGDecisionReason DecisionReason { get; init; } = BotGDecisionReason.InvalidInput;
    public IReadOnlyList<BotGDecisionReason> DecisionReasons { get; init; } = [];
    public bool Approved => Decision == BotGDecisionStatus.Approved;
    public bool Published { get; init; }
    public bool Shadow { get; init; } = true;
    public long? PublishedSelectionId { get; init; }
    public string BaseModelVersion { get; init; } = string.Empty;
    public DateTime? BaseModelTrainedThroughUtc { get; init; }
    public string MetaModelVersion { get; init; } = string.Empty;
    public string CalibrationVersion { get; init; } = string.Empty;
    public string UncertaintyVersion { get; init; } = string.Empty;
    public string OodVersion { get; init; } = string.Empty;
    public decimal StakeUnits { get; init; } = 1m;
    public BotGSettlementState SettlementState { get; init; } = BotGSettlementState.Pending;
    public string? Result { get; init; }
    public decimal? ProfitLoss { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public string FeatureSnapshotJson { get; init; } = "{}";

    public static string SerializeFeatureSnapshot(BotGFeatures features) =>
        JsonSerializer.Serialize(features, SnapshotJsonOptions);

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
