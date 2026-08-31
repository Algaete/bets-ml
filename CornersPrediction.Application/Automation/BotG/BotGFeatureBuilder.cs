using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

public sealed class BotGFeatureBuilder : IBotGFeatureBuilder
{
    public BotGFeatures Build(BotGFeatureBuildInput input, BotGConfiguration configuration)
    {
        var config = BotGConfiguration.Validate(configuration);
        ValidateQuote(input.Quote, config);
        ValidatePredictions(input.Predictions, input.Quote.PredictionTimestampUtc);
        if (!input.MarketProbability.IsAvailable)
            throw new ArgumentException("A strict two-sided no-vig probability is required to build Bot G features.");

        var asOfUtc = input.Quote.PredictionTimestampUtc;
        var homeOverall = ValidateHistory(input.HomeOverall, asOfUtc, nameof(input.HomeOverall));
        var homeVenue = ValidateHistory(input.HomeVenue, asOfUtc, nameof(input.HomeVenue));
        var awayOverall = ValidateHistory(input.AwayOverall, asOfUtc, nameof(input.AwayOverall));
        var awayVenue = ValidateHistory(input.AwayVenue, asOfUtc, nameof(input.AwayVenue));

        var overallValues = SelectMarketValues(input.Quote.MarketType, homeOverall, awayOverall);
        var venueValues = SelectMarketValues(input.Quote.MarketType, homeVenue, awayVenue);
        var overall = DescribeWindows(overallValues, config.Features.DecayFactor);
        var venue = DescribeWindows(venueValues, config.Features.DecayFactor);

        var expectedHome = ContextComponent(homeOverall, homeVenue, awayOverall, awayVenue, homeMarket: true, config.Features);
        var expectedAway = ContextComponent(homeOverall, homeVenue, awayOverall, awayVenue, homeMarket: false, config.Features);
        var contextPrediction = input.Quote.MarketType switch
        {
            BotGMarketType.HomeTeamGoals => expectedHome,
            BotGMarketType.AwayTeamGoals => expectedAway,
            _ => expectedHome + expectedAway
        };
        var legacy = input.Predictions.LegacyFor(input.Quote.MarketType);
        var model2026 = input.Predictions.Model2026For(input.Quote.MarketType);
        var averagePrediction = (legacy + model2026) / 2d;
        var historicalSigma = Math.Max(config.Features.MinimumStandardDeviation, overall.Last20.StandardDeviation);
        var modelVsContextDistance = Math.Abs(averagePrediction - contextPrediction);
        var modelVsContextSigma = modelVsContextDistance / historicalSigma;
        var contextAgreement = Math.Exp(-0.5d * modelVsContextSigma * modelVsContextSigma);
        var historyCount = Math.Min(homeOverall.Count, awayOverall.Count);
        var venueHistoryCount = Math.Min(homeVenue.Count, awayVenue.Count);
        var missing = CountMissing(input);
        var dataQuality = CalculateDataQuality(
            historyCount,
            venueHistoryCount,
            missing,
            input.MarketProbability,
            input.Predictions,
            config.Features);
        var selectedOdds = Convert.ToDouble(input.Quote.SelectedOdds!.Value);
        var oppositeOdds = Convert.ToDouble(input.Quote.OppositeOdds!.Value);
        var line = Convert.ToDouble(input.Quote.Line);

        return new BotGFeatures
        {
            FeatureSchemaVersion = config.FeatureSchemaVersion,
            AsOfDateUtc = asOfUtc,
            MarketType = input.Quote.MarketType,
            Selection = input.Quote.Selection,
            Bookmaker = input.Quote.Bookmaker.Trim(),
            Line = line,
            SelectedOdds = selectedOdds,
            OppositeOdds = oppositeOdds,
            RawImpliedProbability = input.MarketProbability.SelectedRawImpliedProbability,
            MarketNoVigProbability = input.MarketProbability.SelectedNoVigProbability,
            OddsMargin = input.MarketProbability.Overround,
            OddsAgeMinutes = (asOfUtc - input.Quote.OddsTimestampUtc).TotalMinutes,
            LegacyPrediction = legacy,
            Prediction2026 = model2026,
            LegacyMinus2026 = legacy - model2026,
            AveragePrediction = averagePrediction,
            PredictionMinusLine = averagePrediction - line,
            AbsPredictionMinusLine = Math.Abs(averagePrediction - line),
            TotalPrediction2026 = input.Predictions.Model2026Total,
            HomePrediction2026 = input.Predictions.Model2026Home,
            AwayPrediction2026 = input.Predictions.Model2026Away,
            HomePlusAway2026 = input.Predictions.Model2026Home + input.Predictions.Model2026Away,
            DirectTotalMinusHomeAway = input.Predictions.Model2026Total
                - input.Predictions.Model2026Home
                - input.Predictions.Model2026Away,
            ContextPrediction = contextPrediction,
            ModelVsContextDistance = modelVsContextDistance,
            ModelVsContextSigma = modelVsContextSigma,
            ContextAgreementScore = contextAgreement,
            HistoryCount = historyCount,
            VenueHistoryCount = venueHistoryCount,
            MissingFeaturesCount = missing,
            DataQualityScore = dataQuality,
            ModelDisagreement = Math.Abs(legacy - model2026),
            ExactLineHitRate = SmoothRate(
                input.ExactLineHitRate,
                input.ExactLineHistoricalSampleSize,
                config.Features.LineHitRatePriorMean,
                config.Features.LineHistoryPriorStrength),
            NeighborLowerHitRate = SmoothRate(
                input.NeighborLowerHitRate,
                input.ExactLineHistoricalSampleSize,
                config.Features.LineHitRatePriorMean,
                config.Features.LineHistoryPriorStrength),
            NeighborUpperHitRate = SmoothRate(
                input.NeighborUpperHitRate,
                input.ExactLineHistoricalSampleSize,
                config.Features.LineHitRatePriorMean,
                config.Features.LineHistoryPriorStrength),
            HistoricalPushRate = SmoothRate(
                input.HistoricalPushRate,
                input.ExactLineHistoricalSampleSize,
                config.Features.PushRatePriorMean,
                config.Features.LineHistoryPriorStrength),
            ExactLineHistoricalSampleSize = Math.Max(0, input.ExactLineHistoricalSampleSize),
            Overall = overall,
            Venue = venue
        };
    }

    private static void ValidateQuote(BotGMarketQuote quote, BotGConfiguration config)
    {
        RequireUtc(quote.FixtureDateUtc, nameof(quote.FixtureDateUtc));
        RequireUtc(quote.PredictionTimestampUtc, nameof(quote.PredictionTimestampUtc));
        RequireUtc(quote.OddsTimestampUtc, nameof(quote.OddsTimestampUtc));
        if (quote.FixtureId <= 0 || string.IsNullOrWhiteSpace(quote.Bookmaker)
            || string.IsNullOrWhiteSpace(quote.HomeTeam) || string.IsNullOrWhiteSpace(quote.AwayTeam))
            throw new ArgumentException("Bot G quote identity, teams and bookmaker are required.");
        if (!config.SupportedMarkets.Contains(quote.MarketType))
            throw new ArgumentException($"Market {quote.MarketType} is not enabled for Bot G.");
        if (quote.FixtureDateUtc <= quote.PredictionTimestampUtc)
            throw new BotGTemporalLeakageException("FixtureDateUtc must be strictly after PredictionTimestampUtc.");
        if (quote.OddsTimestampUtc > quote.PredictionTimestampUtc)
            throw new BotGTemporalLeakageException("Future odds cannot enter Bot G features.");
        var line = Convert.ToDouble(quote.Line);
        if (!double.IsFinite(line) || line < 0d || Math.Abs(line * 4d - Math.Round(line * 4d)) > 0.000001d)
            throw new ArgumentException("Bot G supports non-negative Asian lines in quarter-goal increments.");
        if (quote.SelectedOdds is not > 1m || quote.OppositeOdds is not > 1m)
            throw new ArgumentException("Both selected and opposite odds are required for Bot G.");
    }

    private static void ValidatePredictions(BotGBasePredictions value, DateTime asOfUtc)
    {
        var predictions = new[]
        {
            value.LegacyTotal, value.LegacyHome, value.LegacyAway,
            value.Model2026Total, value.Model2026Home, value.Model2026Away
        };
        if (predictions.Any(prediction => !double.IsFinite(prediction) || prediction < 0d))
            throw new ArgumentException("All Bot G base predictions must be finite and non-negative.");
        if (string.IsNullOrWhiteSpace(value.LegacyModelVersion) || string.IsNullOrWhiteSpace(value.Model2026Version))
            throw new ArgumentException("Both Bot G base-model versions are required.");
        ValidateModelTimestamp(value.LegacyTrainedThroughUtc, asOfUtc, "legacy");
        ValidateModelTimestamp(value.Model2026TrainedThroughUtc, asOfUtc, "2026");
    }

    private static void ValidateModelTimestamp(DateTime? trainedThroughUtc, DateTime asOfUtc, string label)
    {
        if (!trainedThroughUtc.HasValue)
            throw new BotGTemporalLeakageException(
                $"The {label} base model trained-through timestamp is required for as-of validation.");
        RequireUtc(trainedThroughUtc.Value, $"{label}TrainedThroughUtc");
        if (trainedThroughUtc.Value >= asOfUtc)
            throw new BotGTemporalLeakageException(
                $"The {label} base model was trained through {trainedThroughUtc:O}, not strictly before the prediction timestamp.");
    }

    private static IReadOnlyList<BotGHistoryObservation> ValidateHistory(
        IEnumerable<BotGHistoryObservation> values,
        DateTime asOfUtc,
        string label)
    {
        var rows = values.ToArray();
        foreach (var row in rows)
        {
            RequireUtc(row.MatchDateUtc, $"{label}.MatchDateUtc");
            if (row.MatchDateUtc >= asOfUtc)
                throw new BotGTemporalLeakageException($"{label} contains a fixture at or after PredictionTimestampUtc.");
            if (row.FixtureId <= 0 || !double.IsFinite(row.ValueFor) || !double.IsFinite(row.ValueAgainst)
                || row.ValueFor < 0d || row.ValueAgainst < 0d)
                throw new ArgumentException($"{label} contains an invalid historical observation.");
        }

        return rows
            .GroupBy(row => row.FixtureId)
            .Select(group => group.OrderByDescending(row => row.MatchDateUtc).First())
            .OrderByDescending(row => row.MatchDateUtc)
            .ThenByDescending(row => row.FixtureId)
            .ToArray();
    }

    private static IReadOnlyList<DatedValue> SelectMarketValues(
        BotGMarketType market,
        IReadOnlyList<BotGHistoryObservation> home,
        IReadOnlyList<BotGHistoryObservation> away)
    {
        var homeValues = home.Select(row => new DatedValue(
            row.FixtureId,
            row.MatchDateUtc,
            market switch
            {
                BotGMarketType.HomeTeamGoals => row.ValueFor,
                BotGMarketType.AwayTeamGoals => row.ValueAgainst,
                _ => row.ValueFor + row.ValueAgainst
            }));
        var awayValues = away.Select(row => new DatedValue(
            row.FixtureId,
            row.MatchDateUtc,
            market switch
            {
                BotGMarketType.HomeTeamGoals => row.ValueAgainst,
                BotGMarketType.AwayTeamGoals => row.ValueFor,
                _ => row.ValueFor + row.ValueAgainst
            }));
        return homeValues.Concat(awayValues)
            .GroupBy(value => value.FixtureId)
            .Select(group => new DatedValue(
                group.Key,
                group.Max(value => value.MatchDateUtc),
                group.Average(value => value.Value)))
            .OrderByDescending(value => value.MatchDateUtc)
            .ThenByDescending(value => value.FixtureId)
            .ToArray();
    }

    private static double ContextComponent(
        IReadOnlyList<BotGHistoryObservation> homeOverall,
        IReadOnlyList<BotGHistoryObservation> homeVenue,
        IReadOnlyList<BotGHistoryObservation> awayOverall,
        IReadOnlyList<BotGHistoryObservation> awayVenue,
        bool homeMarket,
        BotGFeatureConfiguration config)
    {
        var firstOverall = homeMarket
            ? homeOverall.Select(row => row.ValueFor)
            : awayOverall.Select(row => row.ValueFor);
        var secondOverall = homeMarket
            ? awayOverall.Select(row => row.ValueAgainst)
            : homeOverall.Select(row => row.ValueAgainst);
        var firstVenue = homeMarket
            ? homeVenue.Select(row => row.ValueFor)
            : awayVenue.Select(row => row.ValueFor);
        var secondVenue = homeMarket
            ? awayVenue.Select(row => row.ValueAgainst)
            : homeVenue.Select(row => row.ValueAgainst);
        var overall = AverageFinite(
            Describe(firstOverall.Take(10), config.DecayFactor).WeightedMean,
            Describe(secondOverall.Take(10), config.DecayFactor).WeightedMean);
        var venueRows = firstVenue.Concat(secondVenue).ToArray();
        if (venueRows.Length == 0) return overall;
        var venue = Describe(venueRows.Take(20), config.DecayFactor).WeightedMean;
        var venueWeight = Math.Min(0.65d, venueRows.Length / (double)(2 * config.RequiredVenueMatches));
        return (1d - venueWeight) * overall + venueWeight * venue;
    }

    private static BotGWindowStatistics DescribeWindows(IReadOnlyList<DatedValue> values, double decay) => new(
        Describe(values.Take(5).Select(value => value.Value), decay),
        Describe(values.Take(10).Select(value => value.Value), decay),
        Describe(values.Take(20).Select(value => value.Value), decay));

    public static BotGDistributionStatistics Describe(IEnumerable<double> values, double decayFactor)
    {
        var data = values.Where(double.IsFinite).ToArray();
        if (data.Length == 0) return BotGDistributionStatistics.Empty;
        var mean = data.Average();
        var variance = data.Average(value => Math.Pow(value - mean, 2d));
        var median = Percentile(data, 0.5d);
        var p25 = Percentile(data, 0.25d);
        var p75 = Percentile(data, 0.75d);
        var weights = Enumerable.Range(0, data.Length).Select(index => Math.Pow(decayFactor, index)).ToArray();
        var weightedMean = data.Zip(weights, (value, weight) => value * weight).Sum() / weights.Sum();
        return new BotGDistributionStatistics(
            data.Length,
            mean,
            weightedMean,
            median,
            Math.Sqrt(variance),
            variance,
            p25,
            p75,
            p75 - p25,
            Percentile(data.Select(value => Math.Abs(value - median)), 0.5d),
            data.Min(),
            data.Max());
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0d;
        var position = Math.Clamp(percentile, 0d, 1d) * (ordered.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? ordered[lower]
            : ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }

    private static int CountMissing(BotGFeatureBuildInput input)
    {
        var missing = 0;
        if (!input.Predictions.LegacyTrainedThroughUtc.HasValue) missing++;
        if (!input.Predictions.Model2026TrainedThroughUtc.HasValue) missing++;
        if (!input.ExactLineHitRate.HasValue) missing++;
        if (!input.NeighborLowerHitRate.HasValue) missing++;
        if (!input.NeighborUpperHitRate.HasValue) missing++;
        if (!input.HistoricalPushRate.HasValue) missing++;
        return missing;
    }

    private static double CalculateDataQuality(
        int historyCount,
        int venueHistoryCount,
        int missing,
        BotGMarketProbabilityResult market,
        BotGBasePredictions predictions,
        BotGFeatureConfiguration config)
    {
        var history = Math.Clamp(historyCount / 20d, 0d, 1d);
        var venue = Math.Clamp(venueHistoryCount / (double)config.RequiredVenueMatches, 0d, 1d);
        var completeness = Math.Clamp(1d - missing / 10d, 0d, 1d);
        var marketQuality = market.IsAvailable ? 1d : 0d;
        var modelMetadata = predictions.LegacyTrainedThroughUtc.HasValue && predictions.Model2026TrainedThroughUtc.HasValue
            ? 1d
            : 0.5d;
        return Math.Clamp(
            0.35d * history + 0.20d * venue + 0.20d * completeness
            + 0.15d * marketQuality + 0.10d * modelMetadata,
            0d,
            1d);
    }

    private static double AverageFinite(params double[] values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        return finite.Length == 0 ? 0d : finite.Average();
    }

    private static double SmoothRate(double? rate, int sampleSize, double priorMean, double priorStrength)
    {
        var boundedSample = Math.Max(0, sampleSize);
        var observed = rate.HasValue && double.IsFinite(rate.Value)
            ? Math.Clamp(rate.Value, 0d, 1d)
            : priorMean;
        return (boundedSample * observed + priorStrength * priorMean) / (boundedSample + priorStrength);
    }

    private static void RequireUtc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException($"{name} must be an explicit UTC timestamp.");
    }

    private sealed record DatedValue(long FixtureId, DateTime MatchDateUtc, double Value);
}
