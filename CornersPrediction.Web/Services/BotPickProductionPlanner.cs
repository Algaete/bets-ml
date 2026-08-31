using System.Text.Json;
using System.Text.RegularExpressions;
using CornersPrediction.Web.Models.BotAutomation;
using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Services;

public static class BotPickProductionPlanner
{
    private const decimal MinimumOdds = 1.60m;
    private const decimal MaximumOdds = 2.30m;
    private const decimal DefaultMinimumEdge = 0.025m;
    private const decimal DefaultMinimumExpectedValue = 0.020m;
    private const int MinimumPredictiveFixtures = 100;
    private const int ControlledTrialMinimumPredictiveFixtures = 30;
    private const string CurrentPolicyVersion = "PRODUCTIVE-GATE-2026-08-27-V2";
    private const string LegacyGoalsPolicyVersion = "GOALS-HISTORICAL-RECONSTRUCTION-V1";
    private const string LegacyCornersPolicyVersion = "CORNERS-HISTORICAL-RECONSTRUCTION-V1";
    private static readonly DateTime LegacyGoalsPolicyCutover = new(2026, 8, 27, 0, 0, 0);
    private static readonly DateTime LegacyCornersPolicyCutover = new(2026, 8, 27, 0, 0, 0);
    private static readonly TimeZoneInfo SantiagoTimeZone = ResolveSantiagoTimeZone();

    public static void Apply(
        IReadOnlyCollection<BotPickSelectionViewModel> selections,
        IReadOnlyCollection<RecommendationBotDefinitionViewModel> definitions,
        string marketFamily,
        IReadOnlyCollection<BotPerformanceScorecardViewModel>? performanceScorecards = null,
        DateTime? asOfLocalTime = null)
    {
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(definitions);

        var family = NormalizeFamily(marketFamily);
        var scorecards = performanceScorecards ?? [];
        var definitionsByKey = definitions
            .GroupBy(definition => NormalizeBotKey(definition.BotKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var eligible = new List<(BotPickSelectionViewModel Selection, string BotKey)>();
        var historicalGoalsEligible = new List<(BotPickSelectionViewModel Selection, string BotKey)>();
        var historicalCornersEligible = new List<(BotPickSelectionViewModel Selection, string BotKey)>();
        var evaluationTime = asOfLocalTime ?? GetSantiagoNow();

        foreach (var selection in selections)
        {
            var botKey = ResolveBotKey(selection);

            if (IsLegacyGoalsHistoricalScope(selection, family))
            {
                var historicalReason = LegacyGoalsIneligibilityReason(selection, botKey);
                selection.ProductionPlan = Monitor(
                    historicalReason ?? "Otra señal fue priorizada en la reconstrucción histórica");
                if (historicalReason is null)
                    historicalGoalsEligible.Add((selection, botKey));
                continue;
            }

            if (IsLegacyCornersHistoricalScope(selection, family))
            {
                var historicalReason = LegacyCornersIneligibilityReason(selection, botKey);
                selection.ProductionPlan = Monitor(
                    historicalReason ?? "Otra señal fue priorizada en la reconstrucción histórica de córners");
                if (historicalReason is null)
                    historicalCornersEligible.Add((selection, botKey));
                continue;
            }

            if (!IsUpcomingPending(selection, evaluationTime))
            {
                selection.ProductionPlan = Monitor(
                    family is "GOALS" or "CORNERS"
                        ? "Registro histórico fuera de la cohorte V1: el gate vigente no reevalúa resultados pasados"
                        : "Registro histórico: este mercado permanece en monitoreo y no tiene una cartera productiva retrospectiva");
                continue;
            }

            var reason = IneligibilityReason(selection, botKey, family, definitionsByKey, scorecards);
            selection.ProductionPlan = Monitor(reason ?? "Otra señal fue priorizada para este partido");
            if (reason is null)
            {
                eligible.Add((selection, botKey));
            }
        }

        ApplyLegacyGoalsHistoricalPlan(historicalGoalsEligible);
        ApplyLegacyCornersHistoricalPlan(historicalCornersEligible);

        foreach (var fixture in eligible.GroupBy(candidate => FixtureKey(candidate.Selection)))
        {
            var ranked = fixture
                .OrderByDescending(candidate => MarketPriority(candidate.Selection, family))
                .ThenBy(candidate => Math.Abs(
                    ResolvePerformance(scorecards, candidate.BotKey, candidate.Selection.MarketType, candidate.Selection.SelectedSide, candidate.Selection.Source, candidate.Selection.AutomationVersion)?.CalibrationGap
                    ?? double.MaxValue))
                .ThenBy(candidate =>
                    ResolvePerformance(scorecards, candidate.BotKey, candidate.Selection.MarketType, candidate.Selection.SelectedSide, candidate.Selection.Source, candidate.Selection.AutomationVersion)?.DeltaBrier
                    ?? double.MaxValue)
                .ThenByDescending(candidate =>
                    ResolvePerformance(scorecards, candidate.BotKey, candidate.Selection.MarketType, candidate.Selection.SelectedSide, candidate.Selection.Source, candidate.Selection.AutomationVersion)?.PredictiveFixtures
                    ?? 0)
                .ThenByDescending(candidate => candidate.Selection.ExpectedValue ?? decimal.MinValue)
                .ThenByDescending(candidate => BotPriority(candidate.BotKey))
                .ThenBy(candidate => candidate.Selection.AutomatedCornerBetSelectionId)
                .ToArray();
            var winner = ranked[0];
            var performance = ResolvePerformance(
                scorecards,
                winner.BotKey,
                winner.Selection.MarketType,
                winner.Selection.SelectedSide,
                winner.Selection.Source,
                winner.Selection.AutomationVersion);
            var stakeUnits = ResolveStakeUnits(winner.Selection, family, performance);
            var controlledTrial = IsControlledGoalsTrial(performance, family, winner.Selection.MarketType);
            var signalKey = SignalKey(winner.Selection);
            var sameSignal = ranked
                .Where(candidate => SignalKey(candidate.Selection) == signalKey)
                .ToArray();
            var independentLineages = sameSignal
                .Select(candidate => BotLineage(candidate.BotKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var consensusBots = sameSignal
                .Select(candidate => DisplayBotKey(candidate.BotKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var consensus = independentLineages.Length > 1
                ? $" Consenso entre linajes {string.Join('+', consensusBots)}."
                : string.Empty;
            var source = string.IsNullOrWhiteSpace(winner.Selection.Source)
                ? "Casa habilitada"
                : winner.Selection.Source.Trim();

            winner.Selection.ProductionPlan = new BotPickProductionPlanViewModel(
                stakeUnits == 1m ? "stake-1" : "stake-half",
                stakeUnits,
                controlledTrial ? "Prueba controlada 0.5u" : stakeUnits == 1m ? "Apostar 1u" : "Apostar 0.5u",
                controlledTrial
                    ? $"{source}; prueba controlada de goles: cohorte exacta con ROI positivo, calibración <= 5 pp y Brier mejor que mercado ({performance!.PredictiveFixtures} partidos).{consensus}"
                    : $"{source}; bot activo y publicable; liga, cuota, edge y EV aprobados. Semáforo {PerformanceLabel(performance)}.{consensus}",
                stakeUnits == 1m ? "bot-production-primary" : "bot-production-secondary",
                true,
                CurrentPolicyVersion,
                false);

            foreach (var candidate in ranked.Skip(1))
            {
                var duplicate = SignalKey(candidate.Selection) == signalKey;
                candidate.Selection.ProductionPlan = Monitor(duplicate
                    ? $"Misma señal ya cubierta por {DisplayBotKey(winner.BotKey)}"
                    : $"Otra señal obtuvo mayor prioridad productiva ({MarketLabel(winner.Selection.MarketType)})");
            }
        }
    }

    private static void ApplyLegacyGoalsHistoricalPlan(
        IReadOnlyCollection<(BotPickSelectionViewModel Selection, string BotKey)> eligible)
    {
        foreach (var fixture in eligible.GroupBy(candidate => FixtureKey(candidate.Selection)))
        {
            var ranked = fixture
                .OrderByDescending(candidate => MarketPriority(candidate.Selection, "GOALS"))
                .ThenByDescending(candidate => BotPriority(candidate.BotKey))
                .ThenByDescending(candidate => candidate.Selection.ExpectedValue ?? decimal.MinValue)
                .ThenBy(candidate => candidate.Selection.AutomatedCornerBetSelectionId)
                .ToArray();
            var winner = ranked[0];
            winner.Selection.ProductionPlan = new BotPickProductionPlanViewModel(
                "stake-1",
                1m,
                "Histórico reconstruido 1u",
                "Simulación GOALS V1 con regla congelada (goles de equipo, cuota 1.60–2.30, edge >= 2.5%, EV >= 2% y una señal por partido). No es una autorización del gate vigente ni prueba de que la apuesta se ejecutó.",
                "bot-production-primary",
                true,
                LegacyGoalsPolicyVersion,
                true);

            foreach (var candidate in ranked.Skip(1))
            {
                candidate.Selection.ProductionPlan = Monitor(
                    $"Reconstrucción histórica: el partido ya fue cubierto por {DisplayBotKey(winner.BotKey)}");
            }
        }
    }

    private static void ApplyLegacyCornersHistoricalPlan(
        IReadOnlyCollection<(BotPickSelectionViewModel Selection, string BotKey)> eligible)
    {
        foreach (var fixture in eligible.GroupBy(candidate => FixtureKey(candidate.Selection)))
        {
            var ranked = fixture
                .OrderByDescending(candidate => LegacyCornersMarketPriority(candidate.Selection.MarketType))
                .ThenByDescending(candidate => candidate.Selection.SelectionScore ?? decimal.MinValue)
                .ThenByDescending(candidate => candidate.Selection.Odds)
                .ThenByDescending(candidate => LegacyBotPriority(candidate.BotKey))
                .ThenBy(candidate => candidate.Selection.AutomatedCornerBetSelectionId)
                .ToArray();
            var winner = ranked[0];
            var stakeUnits = winner.Selection.MarketType == "HomeTeamCorners" ? 1m : 0.5m;
            winner.Selection.ProductionPlan = new BotPickProductionPlanViewModel(
                stakeUnits == 1m ? "stake-1" : "stake-half",
                stakeUnits,
                stakeUnits == 1m ? "Histórico reconstruido 1u" : "Histórico reconstruido 0.5u",
                "Simulación CORNERS V1 con la regla congelada que estaba visible antes del gate vigente: córners de equipo, cuota mínima 1.60 y una señal por partido. No es una autorización actual ni prueba de que la apuesta se ejecutó.",
                stakeUnits == 1m ? "bot-production-primary" : "bot-production-secondary",
                true,
                LegacyCornersPolicyVersion,
                true);

            foreach (var candidate in ranked.Skip(1))
            {
                candidate.Selection.ProductionPlan = Monitor(
                    $"Reconstrucción histórica: el partido ya fue cubierto por {DisplayBotKey(winner.BotKey)}");
            }
        }
    }

    private static string? LegacyGoalsIneligibilityReason(
        BotPickSelectionViewModel selection,
        string botKey)
    {
        if (NormalizeBotKey(botKey) is not ("A" or "C2026" or "D2026" or "E2026" or "F2026"))
            return "Reconstrucción histórica: bot fuera de la cohorte GOALS V1";
        if (selection.MarketType is not ("HomeTeamGoals" or "AwayTeamGoals"))
            return "Reconstrucción histórica: goles totales no pertenecía a la cohorte";

        var source = selection.Source.Trim();
        if (!source.Equals("Pinnacle", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("Betano", StringComparison.OrdinalIgnoreCase))
            return "Reconstrucción histórica: casa fuera de Pinnacle/Betano";
        if (selection.Odds < MinimumOdds || selection.Odds > MaximumOdds)
            return $"Reconstrucción histórica: cuota fuera de {MinimumOdds:0.00}–{MaximumOdds:0.00}";
        if (selection.ModelProbability is null or <= 0m or >= 1m)
            return "Reconstrucción histórica: probabilidad de modelo inválida o ausente";
        if (selection.SelectionScore is null)
            return "Reconstrucción histórica: score de selección ausente";
        if (!selection.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            && !selection.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
            return "Reconstrucción histórica: lado inválido";
        if (!IsHalfLine(selection.LineValue))
            return "Reconstrucción histórica: sólo líneas .5";
        if (HasExplicitNonApproval(selection.DecisionReason))
            return "Reconstrucción histórica: candidato no aprobado";
        if (selection.ProbabilityEdge is null || selection.ProbabilityEdge < DefaultMinimumEdge)
            return $"Reconstrucción histórica: edge menor a {DefaultMinimumEdge:P1}";
        if (selection.ExpectedValue is null || selection.ExpectedValue < DefaultMinimumExpectedValue)
            return $"Reconstrucción histórica: EV menor a {DefaultMinimumExpectedValue:P1}";

        return null;
    }

    private static string? LegacyCornersIneligibilityReason(
        BotPickSelectionViewModel selection,
        string botKey)
    {
        if (NormalizeBotKey(botKey) is not ("A" or "C2026" or "D2026" or "E2026" or "F2026"))
            return "Reconstrucción histórica: bot fuera de la cohorte CORNERS V1";
        if (selection.MarketType is not ("HomeTeamCorners" or "AwayTeamCorners"))
            return "Reconstrucción histórica: córners totales no pertenecía a la cartera";

        var source = selection.Source.Trim();
        if (!source.Equals("Pinnacle", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("Betano", StringComparison.OrdinalIgnoreCase))
            return "Reconstrucción histórica: casa fuera de Pinnacle/Betano";
        if (selection.Odds < MinimumOdds)
            return $"Reconstrucción histórica: cuota menor a {MinimumOdds:0.00}";
        if (IsBrazilSerieB(EffectiveLeague(selection)))
            return "Reconstrucción histórica: Brasil Serie B estaba excluida";
        if (!selection.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            && !selection.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
            return "Reconstrucción histórica: lado inválido";
        if (HasExplicitNonApproval(selection.DecisionReason))
            return "Reconstrucción histórica: candidato no aprobado";
        if (IsQuarterLine(selection.LineValue)
            && (selection.ProfitLoss is null || selection.Stake <= 0m))
        {
            return "Reconstrucción histórica: línea asiática dividida sin liquidación económica auditable";
        }

        return null;
    }

    private static bool IsLegacyGoalsHistoricalScope(
        BotPickSelectionViewModel selection,
        string family) =>
        family == "GOALS"
        && PolicyTimestamp(selection) < LegacyGoalsPolicyCutover
        && IsResolvedForHistoricalSimulation(selection.Status);

    private static bool IsLegacyCornersHistoricalScope(
        BotPickSelectionViewModel selection,
        string family) =>
        family == "CORNERS"
        && PolicyTimestamp(selection) < LegacyCornersPolicyCutover
        && IsResolvedForHistoricalSimulation(selection.Status);

    // The applicable policy is the one active when the recommendation was
    // created, not when its match was eventually played or settled.
    private static DateTime PolicyTimestamp(BotPickSelectionViewModel selection) =>
        selection.CreatedAtUtc == default ? selection.MatchDate : selection.CreatedAtUtc;

    private static bool IsUpcomingPending(BotPickSelectionViewModel selection, DateTime asOfUtc) =>
        selection.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)
        && selection.MatchDate > DateTime.SpecifyKind(asOfUtc, DateTimeKind.Unspecified);

    private static bool IsSettled(string status) =>
        status.Equals("Won", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Lost", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Push", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Void", StringComparison.OrdinalIgnoreCase);

    private static bool IsResolvedForHistoricalSimulation(string status) =>
        status.Equals("Won", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Lost", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Push", StringComparison.OrdinalIgnoreCase);

    private static string? IneligibilityReason(
        BotPickSelectionViewModel selection,
        string botKey,
        string family,
        IReadOnlyDictionary<string, RecommendationBotDefinitionViewModel> definitions,
        IReadOnlyCollection<BotPerformanceScorecardViewModel> scorecards)
    {
        if (!definitions.TryGetValue(NormalizeBotKey(botKey), out var definition))
            return "Monitoreo: no existe una definición activa verificable para este bot";
        if (!definition.IsEnabled)
            return "Monitoreo: el bot está deshabilitado";
        if (!definition.PublishEnabled)
            return "Monitoreo: la publicación productiva del bot está deshabilitada";
        if (!definition.MarketFamilies.Any(value => NormalizeFamily(value) == family))
            return $"Monitoreo: el bot no tiene habilitado el mercado {family}";
        if (!IsLeagueAllowed(definition.LeagueFilters, family, EffectiveLeague(selection)))
            return "Monitoreo: la liga está excluida en el mantenedor del bot";

        var source = selection.Source.Trim();
        if (!source.Equals("Pinnacle", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("Betano", StringComparison.OrdinalIgnoreCase))
            return $"Monitoreo: {source.DefaultIfEmpty("casa desconocida")} no está habilitada para apuestas productivas";
        if (selection.Odds < MinimumOdds || selection.Odds > MaximumOdds)
            return $"Monitoreo: cuota fuera del rango productivo {MinimumOdds:0.00}–{MaximumOdds:0.00}";
        if (selection.ModelProbability is null or <= 0m or >= 1m)
            return "Monitoreo: probabilidad de modelo inválida o ausente";
        if (selection.SelectionScore is null)
            return "Monitoreo: score de selección ausente";
        if (!selection.SelectedSide.Equals("Over", StringComparison.OrdinalIgnoreCase)
            && !selection.SelectedSide.Equals("Under", StringComparison.OrdinalIgnoreCase))
            return "Monitoreo: lado de apuesta inválido";
        if (!IsHalfLine(selection.LineValue))
            return "Monitoreo: líneas .0/.25/.75 siguen en shadow hasta usar liquidación asiática de cinco estados";
        if (HasExplicitNonApproval(selection.DecisionReason))
            return "Monitoreo: la auditoría del candidato no contiene una aprobación productiva";

        if (selection.MarketType.Equals("HomeTeamCorners", StringComparison.OrdinalIgnoreCase))
            return "Monitoreo: córners local permanece pausado por rendimiento negativo validado";
        if (selection.MarketType.Equals("TotalGoals", StringComparison.OrdinalIgnoreCase))
            return "Monitoreo: goles totales permanece pausado por rendimiento negativo validado";
        if (NormalizeBotKey(botKey) == "F2026" && family == "CORNERS")
            return "Monitoreo: F córners permanece pausado por rendimiento negativo validado";

        var performance = ResolvePerformance(
            scorecards,
            botKey,
            selection.MarketType,
            selection.SelectedSide,
            selection.Source,
            selection.AutomationVersion);
        if (performance is null)
            return "Monitoreo: la versión actual no tiene scorecard Green de 30 días para este mercado, lado y casa";
        var green = performance.PredictiveFixtures >= MinimumPredictiveFixtures
            && performance.TrafficLight.Equals("Green", StringComparison.OrdinalIgnoreCase)
            && !performance.ProductionBlocked;
        var controlledTrial = IsControlledGoalsTrial(performance, family, selection.MarketType);
        if (!green && !controlledTrial && performance.PredictiveFixtures < MinimumPredictiveFixtures)
            return $"Monitoreo: muestra exacta insuficiente para Green ({performance.PredictiveFixtures}/{MinimumPredictiveFixtures}); la prueba 0.5u exige >= {ControlledTrialMinimumPredictiveFixtures}, ROI positivo, calibración <= 5 pp y Brier mejor que mercado";
        if (!green && !controlledTrial)
            return $"Monitoreo: semáforo {performance.TrafficLight} 30d; sólo Green puede entrar al plan productivo ({performance.Recommendation})";

        var minimumEdge = Convert.ToDecimal(definition.MinEdge ?? Convert.ToDouble(DefaultMinimumEdge));
        var minimumExpectedValue = Convert.ToDecimal(
            definition.MinExpectedValue ?? Convert.ToDouble(DefaultMinimumExpectedValue));
        if (selection.ProbabilityEdge is null || selection.ProbabilityEdge < minimumEdge)
            return $"Monitoreo: edge menor al mínimo vigente de {minimumEdge:P1}";
        if (selection.ExpectedValue is null || selection.ExpectedValue < minimumExpectedValue)
            return $"Monitoreo: EV menor al mínimo vigente de {minimumExpectedValue:P1}";

        if (family == "CORNERS"
            && selection.MarketType is not ("HomeTeamCorners" or "AwayTeamCorners"))
            return "Monitoreo: córners totales no forma parte del plan productivo vigente";
        if (family == "GOALS"
            && selection.MarketType is not ("HomeTeamGoals" or "AwayTeamGoals"))
            return "Monitoreo: goles totales queda fuera por rendimiento negativo reciente";
        if (family is "SHOTS" or "SOG")
            return "Monitoreo: aún no existe muestra resuelta suficiente para habilitar este mercado";

        return null;
    }

    private static bool HasExplicitNonApproval(string? decisionReason)
    {
        if (string.IsNullOrWhiteSpace(decisionReason) || !decisionReason.TrimStart().StartsWith('{'))
            return false;
        try
        {
            using var document = JsonDocument.Parse(decisionReason);
            return document.RootElement.TryGetProperty("decision", out var decision)
                && !string.Equals(decision.GetString(), "Approved", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool IsLeagueAllowed(
        IReadOnlyCollection<RecommendationBotLeagueFilterViewModel> filters,
        string family,
        string league)
    {
        var applicable = filters
            .Where(filter => NormalizeFamily(filter.MarketFamily) is "*" || NormalizeFamily(filter.MarketFamily) == family)
            .ToArray();
        if (applicable.Length == 0)
            return true;

        var included = applicable.SelectMany(filter => filter.IncludedLeagues).ToArray();
        var excluded = applicable.SelectMany(filter => filter.ExcludedLeagues).ToArray();
        return (included.Length == 0 || included.Any(pattern => Matches(pattern, league)))
            && !excluded.Any(pattern => Matches(pattern, league));
    }

    private static bool Matches(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(value))
            return false;
        var expression = $"^{Regex.Escape(pattern.Trim()).Replace("\\*", ".*")}$";
        return Regex.IsMatch(value.Trim(), expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsBrazilSerieB(string league)
    {
        var normalized = NormalizeText(league);
        return (normalized.Contains("BRAZIL", StringComparison.Ordinal)
                || normalized.Contains("BRASIL", StringComparison.Ordinal)
                || normalized.Contains("BRASILEIRAO", StringComparison.Ordinal))
            && normalized.Contains("SERIE B", StringComparison.Ordinal);
    }

    private static string ResolveBotKey(BotPickSelectionViewModel selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.BotKey))
            return NormalizeBotKey(selection.BotKey);

        var version = selection.AutomationVersion.Trim();
        foreach (var pair in new[]
        {
            (Suffix: "-C2026", Key: "C2026"),
            (Suffix: "-D2026", Key: "D2026"),
            (Suffix: "-E2026", Key: "E2026"),
            (Suffix: "-F2026", Key: "F2026"),
            (Suffix: "-G2026", Key: "G2026"),
            (Suffix: "-H2026", Key: "H2026"),
            (Suffix: "-A", Key: "A"),
            (Suffix: "-B", Key: "B")
        })
        {
            if (version.EndsWith(pair.Suffix, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }

        if (!string.IsNullOrWhiteSpace(selection.DecisionReason))
        {
            try
            {
                using var document = JsonDocument.Parse(selection.DecisionReason);
                if (document.RootElement.TryGetProperty("botProfile", out var profile))
                    return NormalizeBotKey(profile.GetString());
            }
            catch (JsonException)
            {
            }
        }

        return "UNKNOWN";
    }

    private static string NormalizeBotKey(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "C" => "C2026",
        "D" => "D2026",
        "E" => "E2026",
        "F" => "F2026",
        "G" => "G2026",
        "H" => "H2026",
        { Length: > 0 } normalized => normalized,
        _ => "A"
    };

    private static string NormalizeFamily(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CORNERS" => "CORNERS",
        "GOALS" => "GOALS",
        "SHOTS" => "SHOTS",
        "SOG" or "SHOTS-ON-GOAL" => "SOG",
        "*" => "*",
        _ => "CORNERS"
    };

    private static string EffectiveLeague(BotPickSelectionViewModel selection) =>
        string.IsNullOrWhiteSpace(selection.StandardizedLeague)
            ? selection.League.Trim()
            : selection.StandardizedLeague.Trim();

    private static string FixtureKey(BotPickSelectionViewModel selection) => string.Join('|',
        selection.MatchDate.ToString("yyyy-MM-ddTHH:mm"),
        NormalizeText(selection.StandardizedHomeTeam ?? selection.HomeTeam),
        NormalizeText(selection.StandardizedAwayTeam ?? selection.AwayTeam));

    private static string SignalKey(BotPickSelectionViewModel selection) => string.Join('|',
        selection.MarketType,
        selection.SelectedSide.ToUpperInvariant(),
        selection.LineValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

    private static string NormalizeText(string? value) =>
        Regex.Replace(value?.Trim().ToUpperInvariant() ?? string.Empty, @"\s+", " ");

    private static int MarketPriority(BotPickSelectionViewModel selection, string family) =>
        (family, selection.MarketType) switch
        {
            ("CORNERS", "AwayTeamCorners") => 3,
            ("CORNERS", "TotalCorners") => 1,
            ("CORNERS", "HomeTeamCorners") => 0,
            ("GOALS", "AwayTeamGoals") => 3,
            ("GOALS", "HomeTeamGoals") => 2,
            ("GOALS", "TotalGoals") => 0,
            _ => 1
        };

    private static int BotPriority(string botKey) => NormalizeBotKey(botKey) switch
    {
        "E2026" => 5,
        "D2026" => 4,
        "C2026" => 3,
        "A" => 2,
        "F2026" => 1,
        _ => 0
    };

    private static int LegacyBotPriority(string botKey) => NormalizeBotKey(botKey) switch
    {
        "A" => 5,
        "C2026" => 4,
        "D2026" => 3,
        "E2026" => 2,
        "F2026" => 1,
        _ => 0
    };

    private static int LegacyCornersMarketPriority(string marketType) => marketType switch
    {
        "HomeTeamCorners" => 2,
        "AwayTeamCorners" => 1,
        _ => 0
    };

    private static decimal ResolveStakeUnits(
        BotPickSelectionViewModel selection,
        string family,
        BotPerformanceScorecardViewModel? performance)
    {
        if (IsControlledGoalsTrial(performance, family, selection.MarketType))
            return 0.5m;
        return family == "CORNERS" && selection.MarketType == "AwayTeamCorners" ? 0.5m : 1m;
    }

    private static bool IsControlledGoalsTrial(
        BotPerformanceScorecardViewModel? performance,
        string family,
        string marketType) =>
        performance is not null
        && family.Equals("GOALS", StringComparison.OrdinalIgnoreCase)
        && marketType is "HomeTeamGoals" or "AwayTeamGoals"
        && performance.PredictiveFixtures >= ControlledTrialMinimumPredictiveFixtures
        && performance.PredictiveFixtures < MinimumPredictiveFixtures
        && performance.Yield is > 0m
        && performance.CalibrationGap.HasValue
        && Math.Abs(performance.CalibrationGap.Value) <= 0.05d
        && performance.DeltaBrier is <= 0d
        && !performance.ProductionBlocked;

    private static bool IsHalfLine(decimal line)
    {
        var fractionalHundredths = Math.Abs(decimal.ToInt32(decimal.Round(line * 100m, 0))) % 100;
        return fractionalHundredths == 50;
    }

    private static bool IsQuarterLine(decimal line)
    {
        var fractionalHundredths = Math.Abs(decimal.ToInt32(decimal.Round(line * 100m, 0))) % 100;
        return fractionalHundredths is 25 or 75;
    }

    private static string BotLineage(string botKey) => NormalizeBotKey(botKey) switch
    {
        "A" or "F2026" => "LEGACY",
        "C2026" or "D2026" or "E2026" or "H2026" => "MODELS_2026",
        "G2026" => "MARKET_ANCHORED",
        var value => value
    };

    private static BotPerformanceScorecardViewModel? ResolvePerformance(
        IReadOnlyCollection<BotPerformanceScorecardViewModel> scorecards,
        string botKey,
        string marketType,
        string selectedSide,
        string bookmaker,
        string automationVersion) => scorecards
        .Where(row => row.WindowDays == 30
            && row.Dimension == "BotMarketSideBookmakerVersion"
            && NormalizeBotKey(row.BotKey) == NormalizeBotKey(botKey)
            && string.Equals(row.MarketType, marketType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.SelectedSide, selectedSide, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Bookmaker, bookmaker, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.AutomationVersion, automationVersion, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(row => row.PredictiveFixtures)
        .FirstOrDefault();

    private static string PerformanceLabel(BotPerformanceScorecardViewModel? performance) =>
        performance is null ? "sin scorecard" : $"{performance.TrafficLight} ({performance.PredictiveFixtures} partidos)";

    private static string DisplayBotKey(string botKey) => NormalizeBotKey(botKey).Replace("2026", string.Empty);

    private static string MarketLabel(string marketType) => marketType switch
    {
        "HomeTeamCorners" => "córners local",
        "AwayTeamCorners" => "córners visita",
        "HomeTeamGoals" => "goles local",
        "AwayTeamGoals" => "goles visita",
        _ => marketType
    };

    private static BotPickProductionPlanViewModel Monitor(string reason) => new(
        "monitor",
        0m,
        "Monitoreo",
        reason,
        "bot-production-monitor",
        false);

    private static DateTime GetSantiagoNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SantiagoTimeZone);

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

    private static string DefaultIfEmpty(this string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
