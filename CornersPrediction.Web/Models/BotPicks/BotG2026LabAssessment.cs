namespace CornersPrediction.Web.Models.BotPicks;

public sealed record BotG2026LabAssessment(string Title, string Meaning, string NextStep, string Tone)
{
    public static BotG2026LabAssessment From(BotG2026IndexViewModel model)
    {
        var overall = model.Scorecards.FirstOrDefault(row => row.Dimension == "Overall");
        if (!model.RuntimeStatusAvailable)
            return new("Estado del modelo sin confirmar",
                "La consulta del runtime falló. Los resultados históricos disponibles no confirman que exista un modelo activo.",
                "Recuperar el estado del runtime antes de decidir sobre el modelo.", "warning");
        if (!model.RuntimeStatus.Available)
            return new("G está reuniendo datos; todavía no hay un modelo activo",
                "Los candidatos conservan cuotas, señales y resultados para investigación. Una probabilidad tomada del mercado no demuestra capacidad predictiva de G.",
                "Validar el conjunto v1.1: resultados disponibles, cuotas previas al partido, linaje temporal y evidencia de Football Intelligence. Después, entrenar y evaluar por fechas con partidos separados.", "warning");
        if (!model.ScorecardsAvailable || overall is null)
            return new("Modelo disponible; resultados por comprobar",
                "El artefacto está cargado, pero faltan las métricas de este período para evaluar su comportamiento.",
                "Recuperar los scorecards de este período; la ausencia de métricas no equivale a rendimiento cero.", "warning");
        if (overall.PairedProbabilityScored == 0 || !overall.DeltaBrier.HasValue)
            return new("Todavía no hay una comparación válida con el mercado",
                "Faltan resultados binarios con calibración disponible y probabilidades de G y del mercado en las mismas filas.",
                "Revisar cobertura de resultados y calibración antes de interpretar Brier, log-loss o una posible mejora.", "warning");
        return new("Hay evidencia para revisar; G sigue en shadow",
            overall.DeltaBrier < 0
                ? "G tiene menor error Brier que el mercado en esta muestra. Esto describe el período consultado; todavía requiere validación temporal y estabilidad por segmento."
                : "G todavía no mejora el Brier del mercado en esta muestra. Conviene revisar calibración y segmentos antes de proponer su uso.",
            "Comparar por versión y mercado en un período futuro reservado, agrupando todas las cuotas del mismo partido. El volumen de filas no autoriza una promoción.", "info");
    }

    public static string Stage(BotG2026ScorecardViewModel row) => row.CalibratedCandidates == 0
        ? "Recolección" : "Shadow · revisar";
}
