using CornersMLData.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Data
{
    public sealed class BetanoUpcomingOddsRepository
    {
        public const string UpsertStoredProcedureName = "dbo.sp_UpsertPartidoProximoCuotaBetano";
        private const int MaximumPersistenceAttempts = 3;
        private static readonly SemaphoreSlim DatabaseObjectsLock = new(1, 1);
        private static bool _databaseObjectsVerified;
        private const string UpsertMatchBatchSql = """
            MERGE dbo.PartidosProximosCuotas AS Target
            USING
            (
                SELECT
                    Source,
                    SourceMatchId,
                    SourceUrl,
                    MatchDate,
                    League,
                    StandardizedLeague,
                    HomeTeam,
                    AwayTeam,
                    StandardizedHomeTeam,
                    StandardizedAwayTeam,
                    HomeTeamGender,
                    AwayTeamGender,
                    MarketType,
                    LineValue,
                    OverOdds,
                    UnderOdds
                FROM OPENJSON(@RowsJson)
                WITH
                (
                    Source NVARCHAR(30) '$.Source',
                    SourceMatchId NVARCHAR(100) '$.SourceMatchId',
                    SourceUrl NVARCHAR(1000) '$.SourceUrl',
                    MatchDate DATETIME2 '$.MatchDate',
                    League NVARCHAR(300) '$.League',
                    StandardizedLeague NVARCHAR(300) '$.StandardizedLeague',
                    HomeTeam NVARCHAR(300) '$.HomeTeam',
                    AwayTeam NVARCHAR(300) '$.AwayTeam',
                    StandardizedHomeTeam NVARCHAR(300) '$.StandardizedHomeTeam',
                    StandardizedAwayTeam NVARCHAR(300) '$.StandardizedAwayTeam',
                    HomeTeamGender NVARCHAR(1) '$.HomeTeamGender',
                    AwayTeamGender NVARCHAR(1) '$.AwayTeamGender',
                    MarketType NVARCHAR(50) '$.MarketType',
                    LineValue DECIMAL(10, 2) '$.LineValue',
                    OverOdds DECIMAL(18, 6) '$.OverOdds',
                    UnderOdds DECIMAL(18, 6) '$.UnderOdds'
                )
            ) AS Source
                ON Target.Source = Source.Source
               AND CAST(Target.MatchDate AS DATE) = CAST(Source.MatchDate AS DATE)
               AND COALESCE(Target.StandardizedLeague, Target.League) = COALESCE(Source.StandardizedLeague, Source.League)
               AND COALESCE(Target.StandardizedHomeTeam, Target.HomeTeam) = COALESCE(Source.StandardizedHomeTeam, Source.HomeTeam)
               AND COALESCE(Target.StandardizedAwayTeam, Target.AwayTeam) = COALESCE(Source.StandardizedAwayTeam, Source.AwayTeam)
               AND Target.MarketType = Source.MarketType
               AND Target.LineValue = Source.LineValue
            WHEN MATCHED THEN
                UPDATE SET
                    Target.SourceMatchId = Source.SourceMatchId,
                    Target.SourceUrl = Source.SourceUrl,
                    Target.MatchDate = Source.MatchDate,
                    Target.League = Source.League,
                    Target.StandardizedLeague = Source.StandardizedLeague,
                    Target.HomeTeam = Source.HomeTeam,
                    Target.AwayTeam = Source.AwayTeam,
                    Target.StandardizedHomeTeam = Source.StandardizedHomeTeam,
                    Target.StandardizedAwayTeam = Source.StandardizedAwayTeam,
                    Target.HomeTeamGender = Source.HomeTeamGender,
                    Target.AwayTeamGender = Source.AwayTeamGender,
                    Target.OverOdds = Source.OverOdds,
                    Target.UnderOdds = Source.UnderOdds,
                    Target.UpdatedAtUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT
                (
                    Source,
                    SourceMatchId,
                    SourceUrl,
                    MatchDate,
                    League,
                    StandardizedLeague,
                    HomeTeam,
                    AwayTeam,
                    StandardizedHomeTeam,
                    StandardizedAwayTeam,
                    HomeTeamGender,
                    AwayTeamGender,
                    MarketType,
                    LineValue,
                    OverOdds,
                    UnderOdds,
                    UpdatedAtUtc
                )
                VALUES
                (
                    Source.Source,
                    Source.SourceMatchId,
                    Source.SourceUrl,
                    Source.MatchDate,
                    Source.League,
                    Source.StandardizedLeague,
                    Source.HomeTeam,
                    Source.AwayTeam,
                    Source.StandardizedHomeTeam,
                    Source.StandardizedAwayTeam,
                    Source.HomeTeamGender,
                    Source.AwayTeamGender,
                    Source.MarketType,
                    Source.LineValue,
                    Source.OverOdds,
                    Source.UnderOdds,
                    SYSUTCDATETIME()
                );

            INSERT INTO dbo.CornerOddsSnapshots
            (
                CapturedAtUtc,
                Source,
                SourceMatchId,
                SourceUrl,
                MatchDate,
                League,
                StandardizedLeague,
                HomeTeam,
                AwayTeam,
                StandardizedHomeTeam,
                StandardizedAwayTeam,
                HomeTeamGender,
                AwayTeamGender,
                MarketType,
                LineValue,
                OverOdds,
                UnderOdds
            )
            SELECT
                @CapturedAtUtc,
                Source,
                SourceMatchId,
                SourceUrl,
                MatchDate,
                League,
                StandardizedLeague,
                HomeTeam,
                AwayTeam,
                StandardizedHomeTeam,
                StandardizedAwayTeam,
                HomeTeamGender,
                AwayTeamGender,
                MarketType,
                LineValue,
                OverOdds,
                UnderOdds
            FROM OPENJSON(@RowsJson)
            WITH
            (
                Source NVARCHAR(30) '$.Source',
                SourceMatchId NVARCHAR(100) '$.SourceMatchId',
                SourceUrl NVARCHAR(1000) '$.SourceUrl',
                MatchDate DATETIME2 '$.MatchDate',
                League NVARCHAR(300) '$.League',
                StandardizedLeague NVARCHAR(300) '$.StandardizedLeague',
                HomeTeam NVARCHAR(300) '$.HomeTeam',
                AwayTeam NVARCHAR(300) '$.AwayTeam',
                StandardizedHomeTeam NVARCHAR(300) '$.StandardizedHomeTeam',
                StandardizedAwayTeam NVARCHAR(300) '$.StandardizedAwayTeam',
                HomeTeamGender NVARCHAR(1) '$.HomeTeamGender',
                AwayTeamGender NVARCHAR(1) '$.AwayTeamGender',
                MarketType NVARCHAR(50) '$.MarketType',
                LineValue DECIMAL(10, 2) '$.LineValue',
                OverOdds DECIMAL(18, 6) '$.OverOdds',
                UnderOdds DECIMAL(18, 6) '$.UnderOdds'
            );
            """;

        private readonly IConfiguration _configuration;
        private readonly ILogger<BetanoUpcomingOddsRepository> _logger;
        private readonly TeamPositionResolver _teamPositionResolver;

        public BetanoUpcomingOddsRepository(
            IConfiguration configuration,
            ILogger<BetanoUpcomingOddsRepository> logger,
            TeamPositionResolver teamPositionResolver)
        {
            _configuration = configuration;
            _logger = logger;
            _teamPositionResolver = teamPositionResolver;
        }

        public async Task EnsureDatabaseObjectsAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _databaseObjectsVerified))
                return;

            await DatabaseObjectsLock.WaitAsync(cancellationToken);
            try
            {
                if (Volatile.Read(ref _databaseObjectsVerified))
                    return;

                const string sql = """
IF OBJECT_ID(N'dbo.sp_UpsertPartidoProximoCuotaBetano', N'P') IS NULL
    THROW 50010, 'No existe dbo.sp_UpsertPartidoProximoCuotaBetano.', 1;

DECLARE @Definition NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_UpsertPartidoProximoCuotaBetano'));

IF @Definition IS NULL
    THROW 50011, 'No se pudo leer dbo.sp_UpsertPartidoProximoCuotaBetano.', 1;

IF @Definition NOT LIKE N'%GoalsHomeTeam%'
   OR @Definition NOT LIKE N'%GoalsAwayTeam%'
   OR @Definition NOT LIKE N'%ShotsHomeTeam%'
   OR @Definition NOT LIKE N'%ShotsAwayTeam%'
   OR @Definition NOT LIKE N'%ShotsOnTargetHomeTeam%'
   OR @Definition NOT LIKE N'%ShotsOnTargetAwayTeam%'
BEGIN
    SET @Definition = REPLACE(@Definition, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
    SET @Definition = REPLACE(@Definition, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
    SET @Definition = REPLACE(
        @Definition,
        N'@MarketType NOT IN (N''CornersTotal'', N''CornersHomeTeam'', N''CornersAwayTeam'', N''ShotsOnTargetTotal'', N''GoalsTotal'', N''ShotsTotal'', N''CardsTotal'')',
        N'@MarketType NOT IN (N''CornersTotal'', N''CornersHomeTeam'', N''CornersAwayTeam'', N''GoalsTotal'', N''GoalsHomeTeam'', N''GoalsAwayTeam'', N''ShotsTotal'', N''ShotsHomeTeam'', N''ShotsAwayTeam'', N''ShotsOnTargetTotal'', N''ShotsOnTargetHomeTeam'', N''ShotsOnTargetAwayTeam'', N''CardsTotal'')');
    SET @Definition = REPLACE(
        @Definition,
        N'@MarketType NOT IN (N''CornersTotal'', N''ShotsOnTargetTotal'')',
        N'@MarketType NOT IN (N''CornersTotal'', N''CornersHomeTeam'', N''CornersAwayTeam'', N''GoalsTotal'', N''GoalsHomeTeam'', N''GoalsAwayTeam'', N''ShotsTotal'', N''ShotsHomeTeam'', N''ShotsAwayTeam'', N''ShotsOnTargetTotal'', N''ShotsOnTargetHomeTeam'', N''ShotsOnTargetAwayTeam'', N''CardsTotal'')');
    SET @Definition = REPLACE(
        @Definition,
        N'@MarketType NOT IN (N''CornersTotal'', N''CornersHomeTeam'', N''CornersAwayTeam'', N''ShotsOnTargetTotal'')',
        N'@MarketType NOT IN (N''CornersTotal'', N''CornersHomeTeam'', N''CornersAwayTeam'', N''GoalsTotal'', N''GoalsHomeTeam'', N''GoalsAwayTeam'', N''ShotsTotal'', N''ShotsHomeTeam'', N''ShotsAwayTeam'', N''ShotsOnTargetTotal'', N''ShotsOnTargetHomeTeam'', N''ShotsOnTargetAwayTeam'', N''CardsTotal'')');

    IF @Definition NOT LIKE N'%GoalsHomeTeam%'
       OR @Definition NOT LIKE N'%GoalsAwayTeam%'
       OR @Definition NOT LIKE N'%ShotsHomeTeam%'
       OR @Definition NOT LIKE N'%ShotsAwayTeam%'
       OR @Definition NOT LIKE N'%ShotsOnTargetHomeTeam%'
       OR @Definition NOT LIKE N'%ShotsOnTargetAwayTeam%'
        THROW 50012, 'No se pudo actualizar el mercado permitido de Betano.', 1;

    EXEC sys.sp_executesql @Definition;
END;

IF OBJECT_ID(N'dbo.PartidosProximosCuotas', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.PartidosProximosCuotas')
         AND name = N'CK_PartidosProximosCuotas_MarketType'
         AND
         (
             definition NOT LIKE N'%GoalsHomeTeam%'
             OR definition NOT LIKE N'%GoalsAwayTeam%'
             OR definition NOT LIKE N'%ShotsHomeTeam%'
             OR definition NOT LIKE N'%ShotsAwayTeam%'
             OR definition NOT LIKE N'%ShotsOnTargetHomeTeam%'
             OR definition NOT LIKE N'%ShotsOnTargetAwayTeam%'
         )
   )
BEGIN
    ALTER TABLE dbo.PartidosProximosCuotas
        DROP CONSTRAINT CK_PartidosProximosCuotas_MarketType;
    ALTER TABLE dbo.PartidosProximosCuotas WITH CHECK
        ADD CONSTRAINT CK_PartidosProximosCuotas_MarketType CHECK
        (
            MarketType IN
            (
                N'CornersTotal', N'CornersHomeTeam', N'CornersAwayTeam',
                N'GoalsTotal', N'GoalsHomeTeam', N'GoalsAwayTeam',
                N'ShotsTotal', N'ShotsHomeTeam', N'ShotsAwayTeam',
                N'ShotsOnTargetTotal', N'ShotsOnTargetHomeTeam', N'ShotsOnTargetAwayTeam',
                N'CardsTotal'
            )
        );
END;
""";

                var connStr = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connStr))
                    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync(cancellationToken);
                await conn.ExecuteAsync(new CommandDefinition(
                    sql,
                    commandTimeout: 180,
                    cancellationToken: cancellationToken));

                Volatile.Write(ref _databaseObjectsVerified, true);
            }
            finally
            {
                DatabaseObjectsLock.Release();
            }
        }

        public async Task<BetanoOddsPersistenceResult> SincronizarAsync(
            IReadOnlyCollection<BetanoUpcomingFootballOddsMatch> matches,
            DateTime scrapedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (matches == null || matches.Count == 0)
                return BetanoOddsPersistenceResult.Empty;

            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            var persistedRows = 0;
            var skippedMatches = 0;
            var failedMatches = 0;
            var errors = new List<string>();

            foreach (var match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!CanPersistMatch(match))
                {
                    skippedMatches++;
                    _logger.LogWarning(
                        "Se omite cuota Betano incompleta antes de normalizar. Url={Url}, League={League}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}, MatchDate={MatchDate}",
                        match.SourceUrl,
                        match.League,
                        match.HomeTeam,
                        match.AwayTeam,
                        match.MatchDateLocal);
                    continue;
                }

                var result = await PersistMatchWithRetryAsync(
                    connStr,
                    match,
                    scrapedAtUtc,
                    cancellationToken);

                if (result.Skipped)
                {
                    skippedMatches++;
                    continue;
                }

                if (result.Error is not null)
                {
                    failedMatches++;
                    errors.Add(BuildPersistenceError(match, result.Error));
                    continue;
                }

                persistedRows += result.PersistedRows;
            }

            _logger.LogInformation(
                "Sincronizacion de cuotas Betano completada. FilasPersistidas={FilasPersistidas}, PartidosProcesados={PartidosProcesados}, PartidosOmitidos={PartidosOmitidos}, PartidosFallidos={PartidosFallidos}",
                persistedRows,
                matches.Count,
                skippedMatches,
                failedMatches);

            return new BetanoOddsPersistenceResult(
                persistedRows,
                skippedMatches,
                failedMatches,
                errors);
        }

        private async Task<BetanoMatchPersistenceAttempt> PersistMatchWithRetryAsync(
            string connectionString,
            BetanoUpcomingFootballOddsMatch match,
            DateTime capturedAtUtc,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= MaximumPersistenceAttempts; attempt++)
            {
                try
                {
                    await using var conn = new SqlConnection(connectionString);
                    await conn.OpenAsync(cancellationToken);

                    // Identity resolution is read-only. Keep it outside the write
                    // transaction so a slow mapping lookup does not retain locks
                    // over every market line for this match.
                    await NormalizeMatchIdentityAsync(conn, null, match, cancellationToken);
                    if (!CanPersistMatch(match))
                    {
                        _logger.LogWarning(
                            "Se omite cuota Betano incompleta despues de normalizar. Url={Url}, League={League}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}, MatchDate={MatchDate}",
                            match.SourceUrl,
                            match.League,
                            match.HomeTeam,
                            match.AwayTeam,
                            match.MatchDateLocal);
                        return BetanoMatchPersistenceAttempt.SkippedMatch;
                    }

                    await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
                    try
                    {
                        var rows = await UpsertMatchMarketsAsync(
                            conn,
                            tx,
                            match,
                            capturedAtUtc,
                            cancellationToken);
                        await tx.CommitAsync(cancellationToken);
                        return new BetanoMatchPersistenceAttempt(rows, false, null);
                    }
                    catch
                    {
                        try { await tx.RollbackAsync(CancellationToken.None); } catch { }
                        throw;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsTransientSqlFailure(exception) && attempt < MaximumPersistenceAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(500 * attempt);
                    _logger.LogWarning(
                        exception,
                        "Fallo SQL transitorio guardando cuota Betano. Partido={HomeTeam} vs {AwayTeam}, Intento={Attempt}/{MaximumAttempts}, ReintentoEnMs={DelayMs}",
                        match.HomeTeam,
                        match.AwayTeam,
                        attempt,
                        MaximumPersistenceAttempts,
                        delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "No se pudo persistir cuota Betano despues de {Attempt} intento(s). Partido={HomeTeam} vs {AwayTeam}, Url={Url}",
                        attempt,
                        match.HomeTeam,
                        match.AwayTeam,
                        match.SourceUrl);
                    return new BetanoMatchPersistenceAttempt(0, false, exception.Message);
                }
            }

            return new BetanoMatchPersistenceAttempt(0, false, "Persistence attempts were exhausted.");
        }

        private static async Task<int> UpsertMatchMarketsAsync(
            SqlConnection conn,
            SqlTransaction tx,
            BetanoUpcomingFootballOddsMatch match,
            DateTime capturedAtUtc,
            CancellationToken cancellationToken)
        {
            if (match.MatchDateLocal == null)
                return 0;

            var rows = BuildMatchRows(match);
            if (rows.Count == 0)
                return 0;

            await conn.ExecuteAsync(new CommandDefinition(
                commandText: UpsertMatchBatchSql,
                parameters: new
                {
                    RowsJson = JsonSerializer.Serialize(rows),
                    CapturedAtUtc = capturedAtUtc
                },
                transaction: tx,
                commandType: CommandType.Text,
                commandTimeout: 120,
                cancellationToken: cancellationToken));

            return rows.Count;
        }

        private async Task NormalizeMatchIdentityAsync(
            SqlConnection conn,
            SqlTransaction? tx,
            BetanoUpcomingFootballOddsMatch match,
            CancellationToken cancellationToken)
        {
            match.League = CanonicalNameCatalog.CanonicalizeLeague(match.League);
            match.HomeTeam = CanonicalNameCatalog.CanonicalizeTeam(match.HomeTeam);
            match.AwayTeam = CanonicalNameCatalog.CanonicalizeTeam(match.AwayTeam);
            match.StandardizedLeague = match.League;
            match.StandardizedHomeTeam = match.HomeTeam;
            match.StandardizedAwayTeam = match.AwayTeam;

            var identity = await _teamPositionResolver.ResolveIdentityAsync(
                conn,
                match.League,
                match.HomeTeam,
                match.AwayTeam,
                match.HomeTeamGender,
                match.AwayTeamGender,
                tx,
                cancellationToken);

            match.League = CanonicalNameCatalog.CanonicalizeLeague(identity.StandardizedLeague);
            match.HomeTeam = CanonicalNameCatalog.CanonicalizeTeam(identity.PreferredHomeTeam);
            match.AwayTeam = CanonicalNameCatalog.CanonicalizeTeam(identity.PreferredAwayTeam);
            match.StandardizedLeague = match.League;
            match.StandardizedHomeTeam = match.HomeTeam;
            match.StandardizedAwayTeam = match.AwayTeam;
        }

        private static IReadOnlyCollection<BetanoOddsRow> BuildMatchRows(
            BetanoUpcomingFootballOddsMatch match)
        {
            var rows = new Dictionary<string, BetanoOddsRow>(StringComparer.OrdinalIgnoreCase);
            AddMarketRows(rows, match, "CornersTotal", match.CornersTotal);
            AddMarketRows(rows, match, "CornersHomeTeam", match.CornersHomeTeam);
            AddMarketRows(rows, match, "CornersAwayTeam", match.CornersAwayTeam);
            AddMarketRows(rows, match, "ShotsOnTargetTotal", match.ShotsOnTargetTotal);
            AddMarketRows(rows, match, "ShotsOnTargetHomeTeam", match.ShotsOnTargetHomeTeam);
            AddMarketRows(rows, match, "ShotsOnTargetAwayTeam", match.ShotsOnTargetAwayTeam);
            AddMarketRows(rows, match, "GoalsTotal", match.GoalsTotal);
            AddMarketRows(rows, match, "GoalsHomeTeam", match.GoalsHomeTeam);
            AddMarketRows(rows, match, "GoalsAwayTeam", match.GoalsAwayTeam);
            AddMarketRows(rows, match, "ShotsTotal", match.ShotsTotal);
            AddMarketRows(rows, match, "ShotsHomeTeam", match.ShotsHomeTeam);
            AddMarketRows(rows, match, "ShotsAwayTeam", match.ShotsAwayTeam);
            AddMarketRows(rows, match, "CardsTotal", match.CardsTotal);
            return rows.Values.ToArray();
        }

        private static void AddMarketRows(
            IDictionary<string, BetanoOddsRow> rows,
            BetanoUpcomingFootballOddsMatch match,
            string marketType,
            BetanoMarketOddsDto? market)
        {
            if (match.MatchDateLocal == null || market?.Lines == null || market.Lines.Count == 0)
                return;

            foreach (var line in market.Lines)
            {
                if (line.OverOdds == null && line.UnderOdds == null)
                    continue;

                var row = new BetanoOddsRow
                {
                    SourceMatchId = NormalizeNullable(match.SourceMatchId),
                    SourceUrl = NormalizeNullable(match.SourceUrl),
                    MatchDate = match.MatchDateLocal.Value,
                    League = NormalizeRequired(match.League),
                    StandardizedLeague = NormalizeRequired(match.StandardizedLeague),
                    HomeTeam = NormalizeRequired(match.HomeTeam),
                    AwayTeam = NormalizeRequired(match.AwayTeam),
                    StandardizedHomeTeam = NormalizeRequired(match.StandardizedHomeTeam),
                    StandardizedAwayTeam = NormalizeRequired(match.StandardizedAwayTeam),
                    HomeTeamGender = NormalizeGender(match.HomeTeamGender),
                    AwayTeamGender = NormalizeGender(match.AwayTeamGender),
                    MarketType = marketType,
                    LineValue = line.Line,
                    OverOdds = line.OverOdds,
                    UnderOdds = line.UnderOdds
                };

                var key = string.Join(
                    '|',
                    row.MatchDate.ToString("yyyyMMdd"),
                    row.StandardizedLeague,
                    row.StandardizedHomeTeam,
                    row.StandardizedAwayTeam,
                    row.MarketType,
                    row.LineValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                rows[key] = row;
            }
        }

        private static string NormalizeRequired(string? value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static string? NormalizeNullable(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string NormalizeGender(string? value)
            => string.Equals(value?.Trim(), "U", StringComparison.OrdinalIgnoreCase) ? "U" : "M";

        private static bool CanPersistMatch(BetanoUpcomingFootballOddsMatch match)
        {
            return match.MatchDateLocal != null
                && !string.IsNullOrWhiteSpace(match.League)
                && !string.IsNullOrWhiteSpace(match.HomeTeam)
                && !string.IsNullOrWhiteSpace(match.AwayTeam)
                && HasAnyPersistableMarket(match);
        }

        private static bool HasAnyPersistableMarket(BetanoUpcomingFootballOddsMatch match)
        {
            return HasAnyLine(match.CornersTotal)
                || HasAnyLine(match.CornersHomeTeam)
                || HasAnyLine(match.CornersAwayTeam)
                || HasAnyLine(match.ShotsOnTargetTotal)
                || HasAnyLine(match.ShotsOnTargetHomeTeam)
                || HasAnyLine(match.ShotsOnTargetAwayTeam)
                || HasAnyLine(match.GoalsTotal)
                || HasAnyLine(match.GoalsHomeTeam)
                || HasAnyLine(match.GoalsAwayTeam)
                || HasAnyLine(match.ShotsTotal)
                || HasAnyLine(match.ShotsHomeTeam)
                || HasAnyLine(match.ShotsAwayTeam)
                || HasAnyLine(match.CardsTotal);
        }

        private static bool HasAnyLine(BetanoMarketOddsDto? market)
        {
            return market?.Lines.Any(line => line.OverOdds != null || line.UnderOdds != null) == true;
        }

        private static bool IsTransientSqlFailure(Exception exception)
        {
            if (exception is TimeoutException)
                return true;

            if (exception is not SqlException sqlException)
                return false;

            return sqlException.Errors
                .Cast<SqlError>()
                .Any(error => error.Number is -2 or 1205 or 40197 or 40501 or 40613 or 49918 or 49919 or 49920);
        }

        private static string BuildPersistenceError(
            BetanoUpcomingFootballOddsMatch match,
            string error)
        {
            var safeError = error.Length <= 300 ? error : error[..300] + "...";
            return $"{match.HomeTeam} vs {match.AwayTeam}: {safeError}";
        }

        private sealed class BetanoOddsRow
        {
            public string Source { get; init; } = "Betano";
            public string? SourceMatchId { get; init; }
            public string? SourceUrl { get; init; }
            public DateTime MatchDate { get; init; }
            public string League { get; init; } = string.Empty;
            public string StandardizedLeague { get; init; } = string.Empty;
            public string HomeTeam { get; init; } = string.Empty;
            public string AwayTeam { get; init; } = string.Empty;
            public string StandardizedHomeTeam { get; init; } = string.Empty;
            public string StandardizedAwayTeam { get; init; } = string.Empty;
            public string HomeTeamGender { get; init; } = "M";
            public string AwayTeamGender { get; init; } = "M";
            public string MarketType { get; init; } = string.Empty;
            public decimal LineValue { get; init; }
            public decimal? OverOdds { get; init; }
            public decimal? UnderOdds { get; init; }
        }

        private sealed record BetanoMatchPersistenceAttempt(
            int PersistedRows,
            bool Skipped,
            string? Error)
        {
            public static readonly BetanoMatchPersistenceAttempt SkippedMatch = new(0, true, null);
        }
    }

    public sealed record BetanoOddsPersistenceResult(
        int PersistedRows,
        int SkippedMatches,
        int FailedMatches,
        IReadOnlyList<string> Errors)
    {
        public static readonly BetanoOddsPersistenceResult Empty = new(0, 0, 0, Array.Empty<string>());
    }
}
