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
    public sealed class PinnacleUpcomingOddsRepository
    {
        private const string SourceName = "Pinnacle";
        private const string EnsureMarketTypesSql = """
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

        private const string UpsertBatchSql = """
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
        private readonly ILogger<PinnacleUpcomingOddsRepository> _logger;
        private readonly TeamPositionResolver _teamPositionResolver;

        public PinnacleUpcomingOddsRepository(
            IConfiguration configuration,
            ILogger<PinnacleUpcomingOddsRepository> logger,
            TeamPositionResolver teamPositionResolver)
        {
            _configuration = configuration;
            _logger = logger;
            _teamPositionResolver = teamPositionResolver;
        }

        public async Task EnsureDatabaseObjectsAsync(CancellationToken cancellationToken = default)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await conn.ExecuteAsync(new CommandDefinition(
                EnsureMarketTypesSql,
                commandTimeout: 60,
                cancellationToken: cancellationToken));
        }

        public async Task<int> SincronizarAsync(
            IReadOnlyCollection<PinnacleUpcomingFootballOddsMatch> matches,
            DateTime scrapedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (matches == null || matches.Count == 0)
                return 0;

            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            try
            {
                foreach (var match in matches)
                {
                    await NormalizeMatchIdentityAsync(conn, tx, match, cancellationToken);
                }

                var rows = BuildBatchRows(matches);
                var persistedRows = await UpsertBatchAsync(
                    conn,
                    tx,
                    rows,
                    scrapedAtUtc,
                    cancellationToken);

                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Sincronizacion de cuotas Pinnacle completada. FilasPersistidas={FilasPersistidas}, PartidosProcesados={PartidosProcesados}",
                    persistedRows,
                    matches.Count);

                return persistedRows;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private static IReadOnlyCollection<PinnacleOddsRow> BuildBatchRows(
            IReadOnlyCollection<PinnacleUpcomingFootballOddsMatch> matches)
        {
            var rows = new Dictionary<string, PinnacleOddsRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in matches)
            {
                if (match.MatchDateLocal == null)
                    continue;

                AddMarketRows(rows, match, "CornersTotal", match.CornersTotal);
                AddMarketRows(rows, match, "CornersHomeTeam", match.CornersHomeTeam);
                AddMarketRows(rows, match, "CornersAwayTeam", match.CornersAwayTeam);
                AddMarketRows(rows, match, "GoalsTotal", match.GoalsTotal);
                AddMarketRows(rows, match, "GoalsHomeTeam", match.GoalsHomeTeam);
                AddMarketRows(rows, match, "GoalsAwayTeam", match.GoalsAwayTeam);
                AddMarketRows(rows, match, "ShotsOnTargetTotal", match.ShotsOnTargetTotal);
                AddMarketRows(rows, match, "ShotsOnTargetHomeTeam", match.ShotsOnTargetHomeTeam);
                AddMarketRows(rows, match, "ShotsOnTargetAwayTeam", match.ShotsOnTargetAwayTeam);
                AddMarketRows(rows, match, "ShotsTotal", match.ShotsTotal);
                AddMarketRows(rows, match, "ShotsHomeTeam", match.ShotsHomeTeam);
                AddMarketRows(rows, match, "ShotsAwayTeam", match.ShotsAwayTeam);
                AddMarketRows(rows, match, "CardsTotal", match.CardsTotal);
            }

            return rows.Values.ToList();
        }

        private static async Task<int> UpsertBatchAsync(
            SqlConnection conn,
            SqlTransaction tx,
            IReadOnlyCollection<PinnacleOddsRow> rows,
            DateTime capturedAtUtc,
            CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
                return 0;

            var command = new CommandDefinition(
                commandText: UpsertBatchSql,
                parameters: new
                {
                    RowsJson = JsonSerializer.Serialize(rows),
                    CapturedAtUtc = capturedAtUtc
                },
                transaction: tx,
                commandType: CommandType.Text,
                commandTimeout: 120,
                cancellationToken: cancellationToken);

            await conn.ExecuteAsync(command);
            return rows.Count;
        }

        private async Task NormalizeMatchIdentityAsync(
            SqlConnection conn,
            SqlTransaction tx,
            PinnacleUpcomingFootballOddsMatch match,
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

        private static void AddMarketRows(
            IDictionary<string, PinnacleOddsRow> rows,
            PinnacleUpcomingFootballOddsMatch match,
            string marketType,
            BetanoMarketOddsDto? market)
        {
            if (match.MatchDateLocal == null || market?.Lines == null || market.Lines.Count == 0)
                return;

            foreach (var line in market.Lines)
            {
                if (line.OverOdds == null && line.UnderOdds == null)
                    continue;

                var row = new PinnacleOddsRow
                {
                    Source = SourceName,
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

        private sealed class PinnacleOddsRow
        {
            public string Source { get; init; } = SourceName;
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
    }
}
