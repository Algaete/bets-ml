namespace CornersPrediction.Web.Models.BotPicks;

/// <summary>Descriptive conclusions only; this surface cannot promote a bot or fit a model.</summary>
public sealed record BotH2026EvidenceSummary(
    string Headline,
    string Detail,
    string NextStep,
    bool MixedConfigurations,
    IReadOnlyList<BotH2026ScorecardViewModel> Configurations)
{
    public static BotH2026EvidenceSummary From(BotH2026IndexViewModel model)
    {
        if (!model.ScorecardsAvailable)
            return new("Evidencia no disponible", "La consulta no respondió. No se puede concluir que H tenga cero picks o cero resultados.",
                "Reintenta el resumen cuando termine la ejecución de bots.", false, []);

        var configurations = model.Scorecards
            .Where(row => row.Dimension == "Configuration" && row.WindowDays == 30 && row.Evaluations > 0)
            .OrderBy(row => row.ConfigurationVersion, StringComparer.Ordinal).ToArray();
        var overall = model.Scorecards.FirstOrDefault(row => row.Dimension == "Overall" && row.WindowDays == 30);
        if (overall is null || overall.Evaluations == 0)
            return new("Sin capturas en los últimos 30 días", "Todavía no hay evidencia en esta ventana y configuración.",
                "Comprueba la última captura y revisa la ventana de 90 días antes de cambiar el modelo.", false, configurations);

        var mixed = configurations.Length > 1;
        var row = configurations.Length == 1 ? configurations[0] : overall;
        if (row.Approved == 0)
            return new("H aún no tiene apuestas virtuales aprobadas", $"Hay {row.Evaluations:N0} evaluaciones, pero ninguna primera aprobación por partido y configuración.",
                "Revisa por qué se rechazan las señales; explorar umbrales no valida un modelo nuevo.", mixed, configurations);
        if (row.SafelySettled == 0)
            return new("Faltan resultados para medir H", $"Hay {row.Approved:N0} primeras aprobaciones y ninguna liquidación segura. El rendimiento todavía no es calculable.",
                "Revisa los enlaces a partidos y la disponibilidad de córners oficiales antes de calibrar.", mixed, configurations);

        var headline = mixed ? "Compara cada versión por separado"
            : row.ProfitLoss > 0 ? "Resultado virtual positivo; falta validación independiente"
            : row.ProfitLoss < 0 ? "La muestra liquidada pierde unidades"
            : "La muestra liquidada está en equilibrio";
        var detail = $"{row.Evaluations:N0} evaluaciones generan {row.ApprovedSignals:N0} señales aprobadas, "
            + $"{row.Approved:N0} primeras aprobaciones y {row.SafelySettled:N0} resultados seguros. "
            + $"Quedan {row.UnsafeOrUnavailable:N0} aprobaciones sin resultado utilizable.";
        var nextStep = mixed
            ? "Elige una configuración exacta. El total puede incluir el mismo partido en varias versiones."
            : row.UnsafeOrUnavailable > 0
                ? "Completa los resultados faltantes y separa una muestra futura antes de ajustar probabilidades."
                : "Congela una configuración y evalúala en partidos futuros; el resultado histórico solo orienta la siguiente prueba.";
        return new(headline, detail, nextStep, mixed, configurations);
    }

    public static double? PairedDelta(long? count, double? model, double? market) =>
        count > 0 && model.HasValue && market.HasValue && double.IsFinite(model.Value) && double.IsFinite(market.Value)
            ? model.Value - market.Value : null;
}
