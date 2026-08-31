using System.Text.Json;

namespace CornersPrediction.Application.AutomatedCorners;

public sealed record AutomatedBotPickSettlementRequest(
    DateOnly? MatchDateTo = null,
    bool DryRun = false,
    int MaxRows = 5000,
    string? BotKey = null,
    string? MarketFamily = null);

public sealed record AutomatedBotPickSettlementFilter(
    DateOnly? MatchDateTo,
    int MaxRows,
    string? BotKey,
    string? MarketFamily);

public sealed record AutomatedBotPickSettlementResponse(
    DateOnly? MatchDateTo,
    bool DryRun,
    int ReviewedRows,
    int SettledRows,
    int StillPendingRows,
    int WonRows,
    int LostRows,
    int PushRows,
    int AppliedRows,
    int ConcurrentlySkippedRows,
    string? BotKey,
    string? MarketFamily,
    IReadOnlyList<AutomatedBotPickSettlementItem> Items);

public sealed record AutomatedBotPickSettlementItem(
    long SelectionId,
    long? MatchHistoryId,
    long? ApiFootballFixtureId,
    string MarketType,
    string Status,
    int? ActualValue,
    decimal? SettlementFactor,
    decimal? ProfitLoss,
    string Reason,
    string? FixtureStatus,
    string? LinkMethod);

public sealed record AutomatedBotPickSettlementCandidate
{
    public long SelectionId { get; init; }
    public DateTime MatchDate { get; init; } = DateTime.MaxValue;
    public bool ReconcileExistingSettlement { get; init; }
    public DateTime? ExpectedSettledAtUtc { get; init; }
    public DateTime? SourceUpdatedAtUtc { get; init; }
    public string MarketType { get; init; } = string.Empty;
    public string SelectedSide { get; init; } = string.Empty;
    public decimal LineValue { get; init; }
    public decimal Odds { get; init; }
    public decimal Stake { get; init; }
    public long? MatchHistoryId { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public int MatchCandidateCount { get; init; }
    public string? LinkMethod { get; init; }
    public string? FixtureStatus { get; init; }
    public int? HomeGoals { get; init; }
    public int? AwayGoals { get; init; }
    public int? HomeCorners { get; init; }
    public int? AwayCorners { get; init; }
    public int? HomeShots { get; init; }
    public int? AwayShots { get; init; }
    public int? HomeShotsOnGoal { get; init; }
    public int? AwayShotsOnGoal { get; init; }
}

public sealed record AutomatedBotPickSettlementUpdate(
    long SelectionId,
    bool ReconcileExistingSettlement,
    DateTime? ExpectedSettledAtUtc,
    long? MatchHistoryId,
    long? ApiFootballFixtureId,
    string Status,
    int? ActualHomeValue,
    int? ActualAwayValue,
    int? ActualTotalValue,
    int? ActualValue,
    decimal? SettlementFactor,
    decimal? ProfitLoss,
    decimal? YieldPct,
    string Reason,
    string SettlementSource,
    string? FixtureStatus,
    string SnapshotJson);

public sealed record AutomatedBotPickSettlementApplyResult(int AppliedRows, int SettledRows);

public interface IAutomatedBotPickSettlementRepository
{
    Task<IReadOnlyList<AutomatedBotPickSettlementCandidate>> GetPendingCandidatesAsync(
        AutomatedBotPickSettlementFilter filter,
        CancellationToken cancellationToken);

    Task<AutomatedBotPickSettlementApplyResult> ApplyAsync(
        IReadOnlyCollection<AutomatedBotPickSettlementUpdate> updates,
        CancellationToken cancellationToken);
}

public interface IAutomatedBotPickSettlementUseCase
{
    Task<AutomatedBotPickSettlementResponse> SettleAsync(
        AutomatedBotPickSettlementRequest request,
        CancellationToken cancellationToken);
}

public sealed class AutomatedBotPickSettlementUseCase : IAutomatedBotPickSettlementUseCase
{
    private const string LocalSettlementSource = "LocalMatchHistory";
    private readonly IAutomatedBotPickSettlementRepository _repository;

    public AutomatedBotPickSettlementUseCase(IAutomatedBotPickSettlementRepository repository)
    {
        _repository = repository;
    }

    public async Task<AutomatedBotPickSettlementResponse> SettleAsync(
        AutomatedBotPickSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxRows is < 1 or > 20000)
        {
            throw new ArgumentException("MaxRows must be between 1 and 20000.");
        }

        var filter = NormalizeFilter(request);
        var candidates = await _repository.GetPendingCandidatesAsync(filter, cancellationToken);
        var updates = candidates.Select(BuildUpdate).ToArray();
        var items = updates.Zip(candidates, ToItem).ToArray();
        var settled = updates.Count(update => update.Status != "Pending");
        var applyResult = request.DryRun || updates.Length == 0
            ? new AutomatedBotPickSettlementApplyResult(0, 0)
            : await _repository.ApplyAsync(updates, cancellationToken);

        return new AutomatedBotPickSettlementResponse(
            request.MatchDateTo,
            request.DryRun,
            updates.Length,
            request.DryRun ? settled : applyResult.SettledRows,
            updates.Count(update => update.Status == "Pending"),
            updates.Count(update => update.Status == "Won"),
            updates.Count(update => update.Status == "Lost"),
            updates.Count(update => update.Status == "Push"),
            applyResult.AppliedRows,
            request.DryRun ? 0 : Math.Max(0, updates.Length - applyResult.AppliedRows),
            filter.BotKey,
            filter.MarketFamily,
            items);
    }

    private static AutomatedBotPickSettlementFilter NormalizeFilter(
        AutomatedBotPickSettlementRequest request)
    {
        var botKey = NormalizeOptional(request.BotKey)?.ToUpperInvariant();
        botKey = botKey switch
        {
            "C" => "C2026",
            "D" => "D2026",
            "E" => "E2026",
            "F" => "F2026",
            "G" => "G2026",
            _ => botKey
        };

        if (botKey is { Length: > 50 } ||
            botKey?.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_') == true)
        {
            throw new ArgumentException("BotKey must contain at most 50 letters, numbers, hyphens or underscores.");
        }

        var marketFamily = NormalizeOptional(request.MarketFamily)?.ToUpperInvariant();
        marketFamily = marketFamily switch
        {
            null => null,
            "CORNER" or "CORNERS" => "CORNERS",
            "GOAL" or "GOALS" => "GOALS",
            "SHOT" or "SHOTS" => "SHOTS",
            "SOG" or "SHOTS_ON_GOAL" or "SHOTSONGOAL" => "SOG",
            _ => throw new ArgumentException("MarketFamily must be corners, goals, shots or sog.")
        };

        return new AutomatedBotPickSettlementFilter(
            request.MatchDateTo,
            request.MaxRows,
            botKey,
            marketFamily);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AutomatedBotPickSettlementItem ToItem(
        AutomatedBotPickSettlementUpdate update,
        AutomatedBotPickSettlementCandidate candidate) =>
        new(
            update.SelectionId,
            update.MatchHistoryId,
            update.ApiFootballFixtureId,
            candidate.MarketType,
            update.Status,
            update.ActualValue,
            update.SettlementFactor,
            update.ProfitLoss,
            update.Reason,
            update.FixtureStatus,
            candidate.LinkMethod);

    private static AutomatedBotPickSettlementUpdate BuildUpdate(
        AutomatedBotPickSettlementCandidate candidate)
    {
        var reason = ResolvePendingReason(candidate);
        if (reason is not null)
        {
            return Pending(candidate, reason);
        }

        if (!AutomatedBotPickSettlementCalculator.TryResolveActual(
                candidate,
                out var actual,
                out var actualHome,
                out var actualAway,
                out var actualTotal,
                out reason))
        {
            return Pending(candidate, reason!);
        }

        var outcome = AutomatedBotPickSettlementCalculator.Calculate(
            candidate.SelectedSide,
            candidate.LineValue,
            actual,
            candidate.Odds,
            candidate.Stake);
        var settlementReason = $"Liquidado desde MatchHistory local: {candidate.SelectedSide} {candidate.LineValue:0.##}, resultado {actual}, factor {outcome.Factor:0.##}.";
        if (candidate.ReconcileExistingSettlement)
        {
            settlementReason = $"Reconciliado por actualización de MatchHistory: {candidate.SelectedSide} {candidate.LineValue:0.##}, resultado {actual}, factor {outcome.Factor:0.##}.";
        }

        return new AutomatedBotPickSettlementUpdate(
            candidate.SelectionId,
            candidate.ReconcileExistingSettlement,
            candidate.ExpectedSettledAtUtc,
            candidate.MatchHistoryId,
            candidate.ApiFootballFixtureId,
            outcome.Status,
            actualHome,
            actualAway,
            actualTotal,
            actual,
            outcome.Factor,
            outcome.ProfitLoss,
            outcome.YieldPct,
            settlementReason,
            LocalSettlementSource,
            candidate.FixtureStatus,
            CreateSnapshot(candidate));
    }

    private static string? ResolvePendingReason(AutomatedBotPickSettlementCandidate candidate)
    {
        if (candidate.MatchCandidateCount == 0 || candidate.MatchHistoryId is null)
        {
            return "Pending: no existe todavía un resultado local enlazable en MatchHistory.";
        }

        if (candidate.MatchCandidateCount > 1)
        {
            return "Pending: el enlace histórico es ambiguo; se requiere ApiFootballFixtureId o revisión manual.";
        }

        if (!AutomatedBotPickFixtureStatusPolicy.IsFinished(candidate.FixtureStatus))
        {
            if (string.IsNullOrWhiteSpace(candidate.FixtureStatus))
            {
                return "Pending: el histórico local no tiene un estado final verificable; no se liquida automáticamente.";
            }

            return $"Pending: el partido no está finalizado (FixtureStatus={candidate.FixtureStatus ?? "NULL"}).";
        }

        return null;
    }

    private static AutomatedBotPickSettlementUpdate Pending(
        AutomatedBotPickSettlementCandidate candidate,
        string reason) =>
        new(
            candidate.SelectionId,
            candidate.ReconcileExistingSettlement,
            candidate.ExpectedSettledAtUtc,
            candidate.MatchCandidateCount == 1 ? candidate.MatchHistoryId : null,
            candidate.MatchCandidateCount == 1 ? candidate.ApiFootballFixtureId : null,
            "Pending",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            reason,
            LocalSettlementSource,
            candidate.FixtureStatus,
            CreateSnapshot(candidate));

    private static string CreateSnapshot(AutomatedBotPickSettlementCandidate candidate) =>
        JsonSerializer.Serialize(new
        {
            candidate.MatchDate,
            candidate.ReconcileExistingSettlement,
            candidate.ExpectedSettledAtUtc,
            candidate.SourceUpdatedAtUtc,
            candidate.MatchHistoryId,
            candidate.ApiFootballFixtureId,
            candidate.FixtureStatus,
            candidate.HomeGoals,
            candidate.AwayGoals,
            candidate.HomeCorners,
            candidate.AwayCorners,
            candidate.HomeShots,
            candidate.AwayShots,
            candidate.HomeShotsOnGoal,
            candidate.AwayShotsOnGoal,
            candidate.LinkMethod,
            candidate.MatchCandidateCount
        });
}

public static class AutomatedBotPickFixtureStatusPolicy
{
    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "AET", "PEN"
    };

    public static bool IsFinished(string? status) =>
        !string.IsNullOrWhiteSpace(status) && FinishedStatuses.Contains(status.Trim());
}

public sealed record AutomatedBotPickSettlementOutcome(
    string Status,
    decimal Factor,
    decimal ProfitLoss,
    decimal? YieldPct);

public static class AutomatedBotPickSettlementCalculator
{
    public static AutomatedBotPickSettlementOutcome Calculate(
        string selectedSide,
        decimal line,
        int actual,
        decimal odds,
        decimal stake)
    {
        if (!selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase) &&
            !selectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("SelectedSide must be Over or Under.");
        }

        if (line < 0 || actual < 0 || odds <= 1 || stake < 0)
        {
            throw new ArgumentException("Line, actual result, odds or stake are outside their valid range.");
        }

        var floor = decimal.Floor(line);
        var fraction = line - floor;
        decimal[] components = fraction switch
        {
            0.25m => [floor, floor + 0.5m],
            0.75m => [floor + 0.5m, floor + 1m],
            _ => [line]
        };
        var factor = components.Average(component => SettleComponent(selectedSide, component, actual));
        var status = factor > 0 ? "Won" : factor < 0 ? "Lost" : "Push";
        var profitLoss = factor > 0
            ? decimal.Round(stake * (odds - 1) * factor, 2, MidpointRounding.AwayFromZero)
            : decimal.Round(stake * factor, 2, MidpointRounding.AwayFromZero);
        decimal? yield = stake == 0
            ? null
            : factor > 0 ? (odds - 1) * factor : factor;

        return new AutomatedBotPickSettlementOutcome(status, factor, profitLoss, yield);
    }

    public static bool TryResolveActual(
        AutomatedBotPickSettlementCandidate candidate,
        out int actual,
        out int? actualHome,
        out int? actualAway,
        out int? actualTotal,
        out string? reason)
    {
        (actualHome, actualAway) = candidate.MarketType switch
        {
            "TotalGoals" or "HomeTeamGoals" or "AwayTeamGoals" =>
                (candidate.HomeGoals, candidate.AwayGoals),
            "TotalCorners" or "HomeTeamCorners" or "AwayTeamCorners" =>
                (candidate.HomeCorners, candidate.AwayCorners),
            "TotalShots" or "HomeTeamShots" or "AwayTeamShots" =>
                (candidate.HomeShots, candidate.AwayShots),
            "TotalShotsOnGoal" or "HomeTeamShotsOnGoal" or "AwayTeamShotsOnGoal" =>
                (candidate.HomeShotsOnGoal, candidate.AwayShotsOnGoal),
            _ => (null, null)
        };
        actualTotal = actualHome.HasValue && actualAway.HasValue
            ? actualHome.Value + actualAway.Value
            : null;

        int? selectedActual = candidate.MarketType switch
        {
            "HomeTeamGoals" or "HomeTeamCorners" or "HomeTeamShots" or "HomeTeamShotsOnGoal" => actualHome,
            "AwayTeamGoals" or "AwayTeamCorners" or "AwayTeamShots" or "AwayTeamShotsOnGoal" => actualAway,
            "TotalGoals" or "TotalCorners" or "TotalShots" or "TotalShotsOnGoal" => actualTotal,
            _ => null
        };

        if (!IsSupportedMarket(candidate.MarketType))
        {
            actual = 0;
            reason = $"Pending: MarketType '{candidate.MarketType}' no tiene regla de liquidación.";
            return false;
        }

        if (!selectedActual.HasValue)
        {
            actual = 0;
            reason = $"Pending: falta la estadística requerida para {candidate.MarketType}; NULL no equivale a cero.";
            return false;
        }

        actual = selectedActual.Value;
        reason = null;
        return true;
    }

    private static bool IsSupportedMarket(string marketType) => marketType is
        "TotalGoals" or "HomeTeamGoals" or "AwayTeamGoals" or
        "TotalCorners" or "HomeTeamCorners" or "AwayTeamCorners" or
        "TotalShots" or "HomeTeamShots" or "AwayTeamShots" or
        "TotalShotsOnGoal" or "HomeTeamShotsOnGoal" or "AwayTeamShotsOnGoal";

    private static decimal SettleComponent(string selectedSide, decimal line, int actual)
    {
        if (actual == line)
        {
            return 0m;
        }

        var overWon = actual > line;
        return selectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase) == overWon ? 1m : -1m;
    }
}
