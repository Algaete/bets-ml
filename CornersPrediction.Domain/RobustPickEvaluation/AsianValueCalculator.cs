namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface IAsianValueCalculator
{
    AsianValueResult Calculate(decimal decimalOdds, AsianSettlementProbabilities probabilities);
}

public sealed class AsianValueCalculator : IAsianValueCalculator
{
    private const decimal ProbabilityTolerance = 0.000001m;

    public AsianValueResult Calculate(decimal decimalOdds, AsianSettlementProbabilities probabilities)
    {
        if (decimalOdds <= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(decimalOdds), "Decimal odds must be greater than one.");
        }

        var values = new[]
        {
            probabilities.PWin,
            probabilities.PHalfWin,
            probabilities.PPush,
            probabilities.PHalfLoss,
            probabilities.PLoss
        };
        if (values.Any(value => value is < 0m or > 1m)
            || Math.Abs(values.Sum() - 1m) > ProbabilityTolerance)
        {
            throw new ArgumentException("Settlement probabilities must be in [0,1] and sum to one.", nameof(probabilities));
        }

        var positive = probabilities.PWin + 0.5m * probabilities.PHalfWin;
        var negative = probabilities.PLoss + 0.5m * probabilities.PHalfLoss;
        var expectedValue = positive * (decimalOdds - 1m) - negative;
        decimal? fairOdds = positive > 0m ? 1m + negative / positive : null;
        decimal? fairProbability = fairOdds > 0m ? 1m / fairOdds : null;

        return new AsianValueResult(
            positive,
            negative,
            expectedValue,
            fairOdds,
            fairProbability);
    }

    public static AsianSettlementProbabilities FromDistribution(PredictiveDistribution distribution) => new(
        distribution.PWin,
        distribution.PHalfWin,
        distribution.PPush,
        distribution.PHalfLoss,
        distribution.PLoss);
}
