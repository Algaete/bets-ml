namespace CornersPrediction.Web.Models.BotPicks;

public sealed record BotI2026EvidenceSummary(
    BotI2026ScorecardViewModel Overall,
    int ConfigurationCount,
    double? SettlementCoverage,
    string Conclusion,
    string NextStep)
{
    public bool HasMixedVersions => ConfigurationCount > 1;

    public static BotI2026EvidenceSummary? Create(IReadOnlyList<BotI2026ScorecardViewModel> scorecards)
    {
        var overall = scorecards.Where(row => row.Dimension is "All" or "Overall")
            .OrderBy(row => row.WindowDays == 30 ? 0 : row.WindowDays == 90 ? 1 : 2)
            .FirstOrDefault();
        if (overall is null) return null;

        var versions = scorecards.Where(row => row.WindowDays == overall.WindowDays && row.Dimension == "Configuration")
            .Select(row => row.Segment).Distinct(StringComparer.Ordinal).Count();
        var coverage = overall.ApprovedFixtureVersions is > 0 && overall.Settled <= overall.ApprovedFixtureVersions
            ? (double?)overall.Settled / overall.ApprovedFixtureVersions.Value : null;
        var conclusion = overall.Settled == 0
            ? "Todavía no hay resultados resueltos para medir rendimiento."
            : versions > 1
                ? "Hay resultados de varias versiones: revisa cada configuración por separado."
                : overall.Yield is > 0
                    ? "El rendimiento virtual observado es positivo; todavía no demuestra una ventaja estable."
                    : overall.Yield is < 0
                        ? "El rendimiento virtual observado es negativo; conviene revisar la señal antes de ajustar umbrales."
                        : overall.Yield == 0
                            ? "El rendimiento virtual observado está en equilibrio."
                            : "Hay resultados resueltos, pero el rendimiento no está disponible.";
        var nextStep = overall.MissingOfficialLink is > 0
            ? "Primero revisa los partidos sin enlace oficial: sus resultados no entran al rendimiento aunque existan en API-Football."
            : overall.InvalidOutcomeTimestamp is > 0
                ? "Revisa las fechas de los resultados excluidos; sólo sirven resultados conocidos después de la predicción."
                : overall.AwaitingOfficialResult is > 0
                    ? "Completa los resultados oficiales pendientes antes de comparar segmentos."
                    : overall.Settled == 0
                        ? "Espera resultados oficiales de las señales aprobadas y revisa su cobertura."
                        : "Compara cada configuración y mercado, y valida cualquier ajuste en partidos posteriores sin reutilizar esta muestra para elegirlo y evaluarlo.";
        return new(overall, versions, coverage, conclusion, nextStep);
    }
}
