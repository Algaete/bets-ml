namespace CornersPrediction.Domain.RobustPickEvaluation;

public sealed record RobustMarketQuote(
    SelectionSide SelectedSide,
    decimal SelectedOdds,
    decimal? OverOdds,
    decimal? UnderOdds,
    decimal Line);

public sealed record RobustMarketProbabilityResult(
    NoVigStatus Status,
    decimal SelectedRawImpliedProbability,
    decimal? ProportionalSelectedProbability,
    decimal? PowerSelectedProbability,
    decimal? ConservativeSelectedProbability,
    decimal? Overround,
    string Method);

public interface IRobustMarketProbabilityService
{
    RobustMarketProbabilityResult Calculate(RobustMarketQuote quote);
}

/// <summary>
/// Strict two-way market adapter. Both sides belong to the exact quote/line
/// supplied by the caller; a one-sided quote never masquerades as no-vig.
/// The selected probability is conservative across proportional and power
/// margin removal.
/// </summary>
public sealed class RobustMarketProbabilityService : IRobustMarketProbabilityService
{
    public RobustMarketProbabilityResult Calculate(RobustMarketQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        if (quote.SelectedOdds <= 1m || quote.Line < 0m)
            throw new ArgumentOutOfRangeException(nameof(quote), "Odds and line are invalid.");

        var rawSelected = 1m / quote.SelectedOdds;
        if (quote.OverOdds is not > 1m || quote.UnderOdds is not > 1m)
        {
            return new RobustMarketProbabilityResult(
                NoVigStatus.Unavailable,
                rawSelected,
                null,
                null,
                null,
                null,
                "UnavailableSingleSided");
        }

        var rawOver = 1m / quote.OverOdds.Value;
        var rawUnder = 1m / quote.UnderOdds.Value;
        var overround = rawOver + rawUnder;
        if (overround <= 0m)
        {
            return new RobustMarketProbabilityResult(
                NoVigStatus.Unavailable,
                rawSelected,
                null,
                null,
                null,
                null,
                "UnavailableInvalidMarket");
        }

        var proportionalOver = rawOver / overround;
        var proportionalSelected = quote.SelectedSide == SelectionSide.Over
            ? proportionalOver
            : 1m - proportionalOver;
        var exponent = SolvePowerExponent(rawOver, rawUnder);
        var powerOver = Pow(rawOver, exponent);
        var powerUnder = Pow(rawUnder, exponent);
        var powerTotal = powerOver + powerUnder;
        var powerSelected = quote.SelectedSide == SelectionSide.Over
            ? powerOver / powerTotal
            : powerUnder / powerTotal;
        var conservative = Math.Max(proportionalSelected, powerSelected);

        return new RobustMarketProbabilityResult(
            NoVigStatus.Available,
            rawSelected,
            proportionalSelected,
            powerSelected,
            conservative,
            overround,
            "ConservativeMax(Proportional,Power)");
    }

    private static decimal SolvePowerExponent(decimal rawOver, decimal rawUnder)
    {
        var lower = 0.01m;
        var upper = 10m;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var midpoint = (lower + upper) / 2m;
            var total = Pow(rawOver, midpoint) + Pow(rawUnder, midpoint);
            if (total > 1m) lower = midpoint;
            else upper = midpoint;
        }
        return (lower + upper) / 2m;
    }

    private static decimal Pow(decimal value, decimal exponent) =>
        (decimal)Math.Pow((double)value, (double)exponent);
}
