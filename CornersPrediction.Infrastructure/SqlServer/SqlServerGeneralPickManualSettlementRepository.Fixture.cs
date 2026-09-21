using System.Data;
using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed partial class SqlServerGeneralPickManualSettlementRepository
{
    public async Task<GeneralPickSettlementScope> PreviewAsync(long recordId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(cancellationToken);
        var target = await GetFixtureTarget(connection, null, recordId, cancellationToken);
        var matches = await GetFixtureMatches(connection, null, target, cancellationToken);
        return Scope(target, matches);
    }

    private async Task<GeneralPickManualSettlementResult> SettleFixtureAsync(long recordId,
        GeneralPickManualSettlementRequest request, string actor, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = System.Transactions.Transaction.Current is null
            ? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken) : null;
        // Manual operations are rare. Serialize them so double-clicks, retries and
        // corrections from different bots cannot race or create duplicate audit rows.
        await connection.ExecuteAsync(new CommandDefinition("""
            DECLARE @LockResult INT;
            EXEC @LockResult = sys.sp_getapplock @Resource=N'GeneralBotFixtureManualSettlement',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
            IF @LockResult < 0 THROW 50001, 'Otra liquidación está en curso. Vuelve a intentar.', 1;
            """, transaction: transaction, cancellationToken: cancellationToken));
        var existing = await connection.QuerySingleOrDefaultAsync<GeneralPickManualSettlementResult>(
            new CommandDefinition("""
                SELECT RecordId, ActualValue, OutcomeStatus, ProfitLoss, Reason, SettledBy, SettledAtUtc,
                    AppliedToFixture=CAST(1 AS BIT), AffectedEvaluations, AffectedPublishedPicks, AffectedBots
                FROM dbo.GeneralBotFixtureManualSettlements WHERE RequestId=@RequestId;
                """, new { request.RequestId }, transaction, cancellationToken: cancellationToken));
        if (existing is not null)
        {
            if (existing.RecordId != recordId || existing.ActualValue != request.ActualValue
                || existing.Reason != request.Reason!.Trim() || existing.SettledBy != actor)
                throw new ArgumentException("El identificador ya pertenece a otra liquidación.");
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return existing;
        }
        var target = await GetFixtureTarget(connection, transaction, recordId, cancellationToken);
        var matches = await GetFixtureMatches(connection, transaction, target, cancellationToken);
        var scope = Scope(target, matches);
        var outcome = AutomatedBotPickSettlementCalculator.Calculate(target.SelectedSide!, target.LineValue,
            request.ActualValue!.Value, target.Odds!.Value, 1m);
        var status = outcome.Factor switch { 1m => "Win", .5m => "HalfWin", 0m => "Push", -.5m => "HalfLoss", _ => "Loss" };
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT dbo.GeneralBotFixtureManualSettlements
                (RequestId, RecordId, ApiFootballFixtureId, MatchDate, League, HomeTeam, AwayTeam,
                 MarketType, ActualValue, OutcomeStatus, ProfitLoss, Reason, SettledBy,
                 AffectedEvaluations, AffectedPublishedPicks, AffectedBots)
            OUTPUT inserted.Id
            VALUES (@RequestId, @RecordId, @ApiFootballFixtureId, @MatchDate, @League, @HomeTeam, @AwayTeam,
                @MarketType, @ActualValue, @Status, @ProfitLoss, @Reason, @Actor,
                @Evaluations, @PublishedPicks, @Bots);
            """, new { request.RequestId, RecordId = recordId,
                ApiFootballFixtureId = target.ApiFootballFixtureId ?? matches.Select(r => r.ApiFootballFixtureId).FirstOrDefault(id => id > 0),
                target.MatchDate, target.League, target.HomeTeam, target.AwayTeam, target.MarketType,
                request.ActualValue, Status = status, outcome.ProfitLoss, Reason = request.Reason!.Trim(), Actor = actor,
                scope.Evaluations, scope.PublishedPicks, Bots = scope.BotKeys.Length },
            transaction, commandTimeout: 60, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("dbo.sp_ApplyGeneralBotFixtureManualSettlement",
            new { SettlementId = id, NowLocal = LocalNow() }, transaction,
            commandType: CommandType.StoredProcedure, commandTimeout: 60, cancellationToken: cancellationToken));
        var result = await connection.QuerySingleAsync<GeneralPickManualSettlementResult>(new CommandDefinition("""
            SELECT RecordId, ActualValue, OutcomeStatus, ProfitLoss, Reason, SettledBy, SettledAtUtc,
                AppliedToFixture=CAST(1 AS BIT), AffectedEvaluations, AffectedPublishedPicks, AffectedBots
            FROM dbo.GeneralBotFixtureManualSettlements WHERE Id=@Id;
            """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<FixtureTarget> GetFixtureTarget(SqlConnection connection,
        SqlTransaction? transaction, long recordId, CancellationToken cancellationToken)
    {
        if (recordId is 0 or long.MinValue) throw new ArgumentException("El pick no es válido.");
        var target = await connection.QuerySingleOrDefaultAsync<FixtureTarget>(new CommandDefinition(
            FixtureProjectionSql + " WHERE @RecordId>0 AND e.AutomatedBotPickEvaluationId=@RecordId\nUNION ALL\n"
            + PublishedProjectionSql + " WHERE @RecordId<0 AND s.AutomatedCornerBetSelectionId=-@RecordId;",
            new { RecordId = recordId }, transaction, commandTimeout: 60, cancellationToken: cancellationToken));
        if (target is null) throw new KeyNotFoundException("El pick ya no está disponible.");
        if (target.MatchDate > LocalNow())
            throw new ArgumentException("No se puede liquidar un partido que todavía no comienza.");
        if (!Valid(target)) throw new ArgumentException("La señal no tiene un mercado, lado, línea y cuota válidos para liquidar.");
        if (target.ApiFootballFixtureId is null or <= 0
            && (string.IsNullOrWhiteSpace(target.League) || string.IsNullOrWhiteSpace(target.HomeTeam)
                || string.IsNullOrWhiteSpace(target.AwayTeam)))
            throw new ArgumentException("Falta una identidad verificable del partido: se necesita su identificador o liga, equipos y fecha completos.");
        return target with { ApiFootballFixtureId = target.ApiFootballFixtureId > 0 ? target.ApiFootballFixtureId : null };
    }

    private static async Task<FixtureTarget[]> GetFixtureMatches(SqlConnection connection,
        SqlTransaction? transaction, FixtureTarget target, CancellationToken cancellationToken)
    {
        // Separate seeks keep the fixture-id lookup off the date/name scan.
        var exact = "WHERE @ApiFootballFixtureId>0 AND p.ApiFootballFixtureId=@ApiFootballFixtureId "
            + "AND p.MarketType=@MarketType AND p.MatchDate<=@NowLocal";
        var fallback = "WHERE p.MarketType=@MarketType AND p.MatchDate=@MatchDate AND p.MatchDate<=@NowLocal "
            + "AND (@ApiFootballFixtureId IS NULL OR p.ApiFootballFixtureId IS NULL OR p.ApiFootballFixtureId<>@ApiFootballFixtureId) "
            + "AND p.League COLLATE Latin1_General_100_CI_AI=@League COLLATE Latin1_General_100_CI_AI "
            + "AND p.HomeTeam COLLATE Latin1_General_100_CI_AI=@HomeTeam COLLATE Latin1_General_100_CI_AI "
            + "AND p.AwayTeam COLLATE Latin1_General_100_CI_AI=@AwayTeam COLLATE Latin1_General_100_CI_AI";
        var projection = "(" + FixtureProjectionSql + " WITH (INDEX(IX_AutomatedBotPickEvaluations_ResearchPage)) UNION ALL "
            + PublishedProjectionSql + ") AS p ";
        var exactProjection = "(" + FixtureProjectionSql + " WITH (INDEX(IX_AutomatedBotPickEvaluations_ManualFixture)) UNION ALL "
            + PublishedProjectionSql + ") AS p ";
        var sql = "SELECT * FROM " + exactProjection + exact + " UNION ALL SELECT * FROM " + projection
            + fallback + " OPTION (RECOMPILE);";
        var rows = (await connection.QueryAsync<FixtureTarget>(new CommandDefinition(sql,
            new { target.ApiFootballFixtureId, target.MatchDate, target.League, target.HomeTeam,
                target.AwayTeam, target.MarketType, NowLocal = LocalNow() }, transaction,
            commandTimeout: 60, cancellationToken: cancellationToken))).ToArray();
        var knownIds = rows.Where(r => r.ApiFootballFixtureId > 0)
            .Select(r => r.ApiFootballFixtureId).Distinct().ToArray();
        if (knownIds.Length > 1)
            throw new ArgumentException("La identidad del partido es ambigua: hay identificadores de partidos distintos. Revisa el enlace antes de compartir el resultado.");
        return rows.Where(Valid).ToArray();
    }

    private static GeneralPickSettlementScope Scope(FixtureTarget target, FixtureTarget[] rows) => new(
        target.HomeTeam, target.AwayTeam, target.MatchDate, target.MarketType,
        rows.Count(r => r.RecordId > 0), rows.Count(r => r.RecordId < 0),
        rows.Select(r => r.BotKey).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
        target.ApiFootballFixtureId > 0 ? "FixtureId" : "ExactMatchIdentity") { League = target.League };

    private static bool Valid(FixtureTarget row) => row.SelectedSide is "Over" or "Under"
        && row.Odds is > 1m && row.LineValue >= 0 && row.Stake >= 0
        && row.MarketType is "HomeTeamGoals" or "AwayTeamGoals" or "TotalGoals"
            or "HomeTeamCorners" or "AwayTeamCorners" or "TotalCorners"
            or "HomeTeamShots" or "AwayTeamShots" or "TotalShots"
            or "HomeTeamShotsOnGoal" or "AwayTeamShotsOnGoal" or "TotalShotsOnGoal";

    private static DateTime LocalNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"));

    private const string FixtureProjectionSql = """
        SELECT RecordId=e.AutomatedBotPickEvaluationId, e.ApiFootballFixtureId, e.MatchDate,
            League=LTRIM(RTRIM(e.League)), HomeTeam=LTRIM(RTRIM(e.HomeTeam)), AwayTeam=LTRIM(RTRIM(e.AwayTeam)),
            e.MarketType, e.LineValue, e.SelectedSide, Odds=e.SelectedOdds, Stake=CONVERT(DECIMAL(10,2),1), e.BotKey
        FROM dbo.AutomatedBotPickEvaluations AS e
        """;
    private const string PublishedProjectionSql = """
        SELECT RecordId=-s.AutomatedCornerBetSelectionId, s.ApiFootballFixtureId, s.MatchDate,
            League=LTRIM(RTRIM(COALESCE(NULLIF(s.StandardizedLeague,N''),s.League))),
            HomeTeam=LTRIM(RTRIM(COALESCE(NULLIF(s.StandardizedHomeTeam,N''),s.HomeTeam))),
            AwayTeam=LTRIM(RTRIM(COALESCE(NULLIF(s.StandardizedAwayTeam,N''),s.AwayTeam))),
            s.MarketType, s.LineValue, s.SelectedSide, s.Odds, s.Stake, s.BotKey
        FROM dbo.AutomatedCornerBetSelections AS s
        """;
    private sealed record FixtureTarget(long RecordId, long? ApiFootballFixtureId, DateTime MatchDate,
        string League, string HomeTeam, string AwayTeam, string MarketType, decimal LineValue,
        string? SelectedSide, decimal? Odds, decimal Stake, string BotKey);
}
