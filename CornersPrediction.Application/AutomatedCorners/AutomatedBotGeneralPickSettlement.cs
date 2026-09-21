namespace CornersPrediction.Application.AutomatedCorners;

public sealed record GeneralPickManualSettlementRequest(int? ActualValue, string? Reason, Guid RequestId,
    bool ApplyToFixture = false);
public sealed record GeneralPickManualSettlementResult(long RecordId, int ActualValue,
    string OutcomeStatus, decimal ProfitLoss, string Reason, string SettledBy, DateTime SettledAtUtc)
{
    public GeneralPickManualSettlementResult() : this(0, 0, "", 0m, "", "", default) { }
    public bool AppliedToFixture { get; init; }
    public int AffectedEvaluations { get; init; }
    public int AffectedPublishedPicks { get; init; }
    public int AffectedBots { get; init; }
}

public sealed record GeneralPickSettlementScope(string HomeTeam, string AwayTeam, DateTime MatchDate,
    string MarketType, int Evaluations, int PublishedPicks, string[] BotKeys, string MatchMethod)
{
    public string League { get; init; } = "";
}

public interface IGeneralPickManualSettlementRepository
{
    Task<GeneralPickSettlementScope> PreviewAsync(long recordId, CancellationToken cancellationToken);
    Task<GeneralPickManualSettlementResult> SettleAsync(long recordId,
        GeneralPickManualSettlementRequest request, string actor, CancellationToken cancellationToken);
}

public static class GeneralPickManualSettlement
{
    public static void Validate(long recordId, GeneralPickManualSettlementRequest request, string actor)
    {
        if (recordId == 0 || recordId == long.MinValue)
            throw new ArgumentException("El pick no es válido.");
        if (request.ActualValue is null or < 0 or > 1000)
            throw new ArgumentException("Ingresa el resultado real del mercado: un entero entre 0 y 1000.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("Indica la fuente o motivo de la liquidación (máximo 1000 caracteres).");
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("Falta el identificador de la liquidación.");
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256)
            throw new ArgumentException("Falta el usuario responsable de la liquidación.");
    }
}
