using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

public sealed class BotGExpectedValueService : IBotGExpectedValueService
{
    public BotGOutcomeDistribution Reanchor(
        BotGOutcomeDistribution baselineDistribution,
        double positiveReturnProbability)
    {
        var baseline = BotGOutcomeDistribution.Validate(baselineDistribution);
        if (!double.IsFinite(positiveReturnProbability)
            || positiveReturnProbability < 0d || positiveReturnProbability > 1d)
            throw new ArgumentOutOfRangeException(nameof(positiveReturnProbability));

        var positiveMass = baseline.Win + baseline.HalfWin;
        var nonPositiveMass = baseline.Push + baseline.HalfLoss + baseline.Loss;
        var winRatio = positiveMass > 0d ? baseline.Win / positiveMass : 1d;
        var halfWinRatio = positiveMass > 0d ? baseline.HalfWin / positiveMass : 0d;
        var pushRatio = nonPositiveMass > 0d ? baseline.Push / nonPositiveMass : 0d;
        var halfLossRatio = nonPositiveMass > 0d ? baseline.HalfLoss / nonPositiveMass : 0d;
        var lossRatio = nonPositiveMass > 0d ? baseline.Loss / nonPositiveMass : 1d;
        var nonPositiveProbability = 1d - positiveReturnProbability;
        return BotGOutcomeDistribution.Validate(new BotGOutcomeDistribution(
            positiveReturnProbability * winRatio,
            positiveReturnProbability * halfWinRatio,
            nonPositiveProbability * pushRatio,
            nonPositiveProbability * halfLossRatio,
            nonPositiveProbability * lossRatio));
    }

    public BotGExpectedValueResult Calculate(decimal selectedOdds, BotGOutcomeDistribution distribution)
    {
        if (selectedOdds <= 1m)
            throw new ArgumentOutOfRangeException(nameof(selectedOdds), "Decimal odds must be greater than 1.0.");
        var validated = BotGOutcomeDistribution.Validate(distribution);
        var odds = Convert.ToDouble(selectedOdds);
        var profit = validated.Win * (odds - 1d)
            + validated.HalfWin * 0.5d * (odds - 1d)
            - validated.HalfLoss * 0.5d
            - validated.Loss;
        return new BotGExpectedValueResult(
            validated,
            profit,
            validated.PositiveReturnProbability,
            validated.NegativeReturnProbability);
    }

    public BotGExpectedValueResult CalculateConservative(
        decimal selectedOdds,
        BotGOutcomeDistribution distribution,
        double probabilityUncertainty,
        BotGConfiguration configuration)
    {
        var config = BotGConfiguration.Validate(configuration).Uncertainty;
        var validated = BotGOutcomeDistribution.Validate(distribution);
        if (!double.IsFinite(probabilityUncertainty) || probabilityUncertainty < 0d)
            throw new ArgumentOutOfRangeException(nameof(probabilityUncertainty));

        // Preserve the relative five-state economics while moving the modeled positive-return
        // probability toward its lower, conservative bound.
        var conservativePositiveProbability = Math.Clamp(
            validated.PositiveReturnProbability - config.ConservativeLambda * probabilityUncertainty,
            0d,
            validated.PositiveReturnProbability);
        return Calculate(selectedOdds, Reanchor(validated, conservativePositiveProbability));
    }
}

public sealed record BotGAsianSettlementResult(
    BotGMarketType MarketType,
    BotGSelection Selection,
    decimal Line,
    int ActualValue,
    BotGSettlementState State,
    decimal Factor,
    decimal ProfitLoss,
    decimal? Yield);

public static class BotGAsianSettlementCalculator
{
    public static bool RequiresFiveStateDistribution(decimal line)
    {
        if (line < 0m || line * 4m != decimal.Truncate(line * 4m))
            throw new ArgumentOutOfRangeException(nameof(line), "Line must be a non-negative Asian quarter increment.");
        var fraction = line - decimal.Floor(line);
        return fraction is 0m or 0.25m or 0.75m;
    }

    public static BotGAsianSettlementResult Calculate(
        BotGMarketType marketType,
        BotGSelection selection,
        decimal line,
        int homeGoals,
        int awayGoals,
        decimal odds,
        decimal stake = 1m)
    {
        if (!Enum.IsDefined(marketType) || !Enum.IsDefined(selection))
            throw new ArgumentException("Bot G only settles supported GOALS markets and Over/Under selections.");
        if (homeGoals < 0 || awayGoals < 0)
            throw new ArgumentOutOfRangeException(nameof(homeGoals), "Goal results must be non-negative.");
        var actual = marketType switch
        {
            BotGMarketType.HomeTeamGoals => homeGoals,
            BotGMarketType.AwayTeamGoals => awayGoals,
            _ => homeGoals + awayGoals
        };
        var outcome = AutomatedBotPickSettlementCalculator.Calculate(
            selection.ToString(),
            line,
            actual,
            odds,
            stake);
        return new BotGAsianSettlementResult(
            marketType,
            selection,
            line,
            actual,
            ToState(outcome.Factor),
            outcome.Factor,
            outcome.ProfitLoss,
            outcome.YieldPct);
    }

    private static BotGSettlementState ToState(decimal factor) => factor switch
    {
        1m => BotGSettlementState.Win,
        0.5m => BotGSettlementState.HalfWin,
        0m => BotGSettlementState.Push,
        -0.5m => BotGSettlementState.HalfLoss,
        -1m => BotGSettlementState.Loss,
        _ => throw new InvalidOperationException($"Unsupported Asian settlement factor {factor}.")
    };
}
