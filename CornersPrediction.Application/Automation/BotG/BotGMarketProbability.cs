using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

/// <summary>
/// Computes a proportional two-way no-vig probability.  Bot G intentionally has no one-sided
/// odds fallback: both sides of the same bookmaker market must be available at the same snapshot.
/// </summary>
public sealed class StrictMarketProbabilityService : IMarketProbabilityService
{
    public BotGMarketProbabilityResult Calculate(BotGMarketQuote quote)
    {
        if (!Enum.IsDefined(quote.Selection))
            return BotGMarketProbabilityResult.Unavailable("The selected side is not supported.");
        if (quote.OverOdds is not > 1m || quote.UnderOdds is not > 1m)
            return BotGMarketProbabilityResult.Unavailable(
                "Both over and under decimal odds greater than 1.0 are required for strict no-vig.");

        var rawOver = 1d / Convert.ToDouble(quote.OverOdds.Value);
        var rawUnder = 1d / Convert.ToDouble(quote.UnderOdds.Value);
        var denominator = rawOver + rawUnder;
        if (!double.IsFinite(rawOver) || !double.IsFinite(rawUnder)
            || !double.IsFinite(denominator) || denominator <= 0d)
            return BotGMarketProbabilityResult.Unavailable("The two-sided implied market is invalid.");

        var noVigOver = rawOver / denominator;
        var noVigUnder = rawUnder / denominator;
        var selectedRaw = quote.Selection == BotGSelection.Over ? rawOver : rawUnder;
        var selectedNoVig = quote.Selection == BotGSelection.Over ? noVigOver : noVigUnder;
        return new BotGMarketProbabilityResult(
            true,
            rawOver,
            rawUnder,
            noVigOver,
            noVigUnder,
            selectedRaw,
            selectedNoVig,
            denominator - 1d);
    }
}
