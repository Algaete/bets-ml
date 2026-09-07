using System.Data;
using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerGeneralPickManualSettlementRepository(IConfiguration configuration)
    : IGeneralPickManualSettlementRepository
{
    public async Task<GeneralPickManualSettlementResult> SettleAsync(long recordId,
        GeneralPickManualSettlementRequest request, string actor, CancellationToken cancellationToken)
    {
        GeneralPickManualSettlement.Validate(recordId, request, actor);
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = System.Transactions.Transaction.Current is null
            ? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken) : null;
        var target = await connection.QuerySingleOrDefaultAsync<Target>(new CommandDefinition("""
            SELECT RecordId = CASE WHEN PublishedSelectionId IS NOT NULL THEN -PublishedSelectionId
                       ELSE AutomatedBotPickEvaluationId END,
                PublishedSelectionId, MatchDate, LineValue, SelectedSide, Odds = SelectedOdds, BotKey
            FROM dbo.AutomatedBotPickEvaluations WITH (UPDLOCK)
            WHERE @RecordId > 0 AND AutomatedBotPickEvaluationId = @RecordId
            UNION ALL
            SELECT -AutomatedCornerBetSelectionId, AutomatedCornerBetSelectionId,
                MatchDate, LineValue, SelectedSide, Odds, BotKey
            FROM dbo.AutomatedCornerBetSelections WITH (UPDLOCK)
            WHERE @RecordId < 0 AND AutomatedCornerBetSelectionId = -@RecordId;
            """, new { RecordId = recordId }, transaction, commandTimeout: 60, cancellationToken: cancellationToken));
        if (target is null) throw new KeyNotFoundException("El pick ya no está disponible.");
        if (target.BotKey is "G2026" or "I2026")
            throw new ArgumentException("Este bot se administra desde su laboratorio.");
        var existing = await connection.QuerySingleOrDefaultAsync<GeneralPickManualSettlementResult>(new CommandDefinition("""
            SELECT RecordId, ActualValue, OutcomeStatus, ProfitLoss, Reason, SettledBy, SettledAtUtc
            FROM dbo.GeneralBotPickManualSettlements WHERE RequestId = @RequestId;
            """, new { request.RequestId }, transaction, cancellationToken: cancellationToken));
        if (existing is not null)
        {
            if (existing.RecordId != target.RecordId || existing.ActualValue != request.ActualValue
                || existing.Reason != request.Reason!.Trim() || existing.SettledBy != actor)
                throw new ArgumentException("El identificador ya pertenece a otra liquidación.");
            return existing;
        }
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"));
        if (DateTime.SpecifyKind(target.MatchDate, DateTimeKind.Unspecified) > localNow)
            throw new ArgumentException("No se puede liquidar un partido que todavía no comienza.");
        if (target.SelectedSide is not ("Over" or "Under") || target.Odds is null or <= 1m)
            throw new ArgumentException("La señal no tiene un lado y una cuota válidos para liquidar.");
        var outcome = AutomatedBotPickSettlementCalculator.Calculate(target.SelectedSide,
            target.LineValue, request.ActualValue!.Value, target.Odds.Value, 1m);
        var status = outcome.Factor switch { 1m => "Win", .5m => "HalfWin", 0m => "Push", -.5m => "HalfLoss", _ => "Loss" };
        if (target.PublishedSelectionId is > 0)
        {
            var parameters = new DynamicParameters();
            parameters.Add("AutomatedCornerBetSelectionId", target.PublishedSelectionId);
            parameters.Add("ActualValue", request.ActualValue);
            parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            await connection.ExecuteAsync(new CommandDefinition("dbo.sp_ResolveAutomatedCornerBetSelection",
                parameters, transaction, commandType: CommandType.StoredProcedure, commandTimeout: 60,
                cancellationToken: cancellationToken));
            if (parameters.Get<int>("RowsAffected") != 1)
                throw new KeyNotFoundException("No se encontró el pick publicado asociado.");
        }
        var saved = await connection.QuerySingleAsync<GeneralPickManualSettlementResult>(new CommandDefinition("""
            INSERT dbo.GeneralBotPickManualSettlements
                (RecordId, RequestId, ActualValue, OutcomeStatus, SettlementFactor, ProfitLoss, Reason, SettledBy)
            OUTPUT inserted.RecordId, inserted.ActualValue, inserted.OutcomeStatus, inserted.ProfitLoss,
                inserted.Reason, inserted.SettledBy, inserted.SettledAtUtc
            VALUES (@RecordId, @RequestId, @ActualValue, @Status, @Factor, @ProfitLoss, @Reason, @Actor);
            """, new { target.RecordId, request.RequestId, request.ActualValue, Status = status,
                outcome.Factor, outcome.ProfitLoss, Reason = request.Reason!.Trim(), Actor = actor },
            transaction, cancellationToken: cancellationToken));
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    private sealed record Target(long RecordId, long? PublishedSelectionId, DateTime MatchDate,
        decimal LineValue, string? SelectedSide, decimal? Odds, string BotKey);
}
