using CornersMLData.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Data
{
    public sealed class TeamPositionResolver
    {
        private readonly ILogger<TeamPositionResolver> _logger;
        private FifaRankingSourceMetadata? _fifaSourceMetadata;
        private bool _fifaSourceResolved;

        public TeamPositionResolver(ILogger<TeamPositionResolver> logger)
        {
            _logger = logger;
        }

        public async Task<ResolvedMatchIdentity> ResolveIdentityAsync(
            SqlConnection conn,
            string league,
            string homeTeam,
            string awayTeam,
            string? homeTeamGender = "M",
            string? awayTeamGender = "M",
            SqlTransaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            var canonicalLeague = CanonicalNameCatalog.CanonicalizeLeague(league);
            var canonicalHomeTeam = CanonicalNameCatalog.CanonicalizeTeam(homeTeam);
            var canonicalAwayTeam = CanonicalNameCatalog.CanonicalizeTeam(awayTeam);
            var normalizedHomeGender = NormalizeGender(homeTeamGender);
            var normalizedAwayGender = NormalizeGender(awayTeamGender);
            var standardizedLeague = await ResolveStandardizedLeagueAsync(conn, canonicalLeague, tx, cancellationToken);
            var standardizedHomeTeam = await ResolveStandardizedTeamAsync(
                conn,
                canonicalHomeTeam,
                standardizedLeague,
                canonicalLeague,
                normalizedHomeGender,
                tx,
                cancellationToken);
            var standardizedAwayTeam = await ResolveStandardizedTeamAsync(
                conn,
                canonicalAwayTeam,
                standardizedLeague,
                canonicalLeague,
                normalizedAwayGender,
                tx,
                cancellationToken);

            var preferredHomeTeam = canonicalHomeTeam;
            var preferredAwayTeam = canonicalAwayTeam;
            var isNationalTeamsMatch = await IsNationalTeamsMatchAsync(
                conn,
                standardizedLeague,
                standardizedHomeTeam,
                standardizedAwayTeam,
                normalizedHomeGender,
                normalizedAwayGender,
                tx,
                cancellationToken);

            if (isNationalTeamsMatch)
            {
                preferredHomeTeam = NormalizePreferredNationalTeamName(
                    await ResolvePreferredNationalTeamNameAsync(
                        conn,
                        canonicalHomeTeam,
                        standardizedHomeTeam,
                        normalizedHomeGender,
                        tx,
                        cancellationToken));
                preferredAwayTeam = NormalizePreferredNationalTeamName(
                    await ResolvePreferredNationalTeamNameAsync(
                        conn,
                        canonicalAwayTeam,
                        standardizedAwayTeam,
                        normalizedAwayGender,
                        tx,
                        cancellationToken));
                standardizedHomeTeam = preferredHomeTeam;
                standardizedAwayTeam = preferredAwayTeam;
            }

            return new ResolvedMatchIdentity
            {
                StandardizedLeague = CanonicalNameCatalog.CanonicalizeLeague(standardizedLeague),
                StandardizedHomeTeam = CanonicalNameCatalog.CanonicalizeTeam(standardizedHomeTeam),
                StandardizedAwayTeam = CanonicalNameCatalog.CanonicalizeTeam(standardizedAwayTeam),
                PreferredHomeTeam = CanonicalNameCatalog.CanonicalizeTeam(preferredHomeTeam),
                PreferredAwayTeam = CanonicalNameCatalog.CanonicalizeTeam(preferredAwayTeam)
            };
        }

        public async Task EnrichMatchHistoryAsync(
            SqlConnection conn,
            MatchHistoryUpsertDto matchDto,
            SqlTransaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            if (matchDto == null)
                throw new ArgumentNullException(nameof(matchDto));

            var context = await ResolveContextAsync(
                conn,
                matchDto.League,
                matchDto.HomeTeam,
                matchDto.AwayTeam,
                NormalizeGender(matchDto.HomeTeamGender),
                NormalizeGender(matchDto.AwayTeamGender),
                tx,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(context.PreferredHomeTeam))
                matchDto.HomeTeam = context.PreferredHomeTeam;

            if (!string.IsNullOrWhiteSpace(context.PreferredAwayTeam))
                matchDto.AwayTeam = context.PreferredAwayTeam;

            matchDto.TotalTeams = context.TotalTeams;
            matchDto.HomeTeamPosition = context.HomeTeamPosition;
            matchDto.AwayTeamPosition = context.AwayTeamPosition;
        }

        public async Task EnrichUpcomingMatchAsync(
            SqlConnection conn,
            PartidoProximoUpsertDto matchDto,
            SqlTransaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            if (matchDto == null)
                throw new ArgumentNullException(nameof(matchDto));

            var gender = NormalizeGenero(matchDto.Genero);
            var context = await ResolveContextAsync(
                conn,
                matchDto.Liga,
                matchDto.EquipoLocal,
                matchDto.EquipoVisita,
                gender,
                gender,
                tx,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(context.PreferredHomeTeam))
                matchDto.EquipoLocal = context.PreferredHomeTeam;

            if (!string.IsNullOrWhiteSpace(context.PreferredAwayTeam))
                matchDto.EquipoVisita = context.PreferredAwayTeam;

            matchDto.TotalTeams = context.TotalTeams;
            matchDto.HomeTeamPosition = context.HomeTeamPosition;
            matchDto.AwayTeamPosition = context.AwayTeamPosition;
        }

        public IReadOnlyCollection<string> BuildEquivalentTeamNames(string team)
        {
            var clean = NormalizeRequired(team);
            if (clean.Length == 0)
                return Array.Empty<string>();

            return CanonicalNameCatalog.GetEquivalentTeamNames(clean);
        }

        private async Task<ResolvedTeamContext> ResolveContextAsync(
            SqlConnection conn,
            string league,
            string homeTeam,
            string awayTeam,
            string homeTeamGender,
            string awayTeamGender,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            var canonicalLeague = CanonicalNameCatalog.CanonicalizeLeague(league);
            var canonicalHomeTeam = CanonicalNameCatalog.CanonicalizeTeam(homeTeam);
            var canonicalAwayTeam = CanonicalNameCatalog.CanonicalizeTeam(awayTeam);
            var standardizedLeague = await ResolveStandardizedLeagueAsync(conn, canonicalLeague, tx, cancellationToken);
            var standardizedHomeTeam = await ResolveStandardizedTeamAsync(conn, canonicalHomeTeam, standardizedLeague, canonicalLeague, homeTeamGender, tx, cancellationToken);
            var standardizedAwayTeam = await ResolveStandardizedTeamAsync(conn, canonicalAwayTeam, standardizedLeague, canonicalLeague, awayTeamGender, tx, cancellationToken);

            var isNationalTeamsMatch = await IsNationalTeamsMatchAsync(
                conn,
                standardizedLeague,
                standardizedHomeTeam,
                standardizedAwayTeam,
                homeTeamGender,
                awayTeamGender,
                tx,
                cancellationToken);

            var preferredHomeTeam = canonicalHomeTeam;
            var preferredAwayTeam = canonicalAwayTeam;

            if (isNationalTeamsMatch)
            {
                preferredHomeTeam = await ResolvePreferredNationalTeamNameAsync(
                    conn,
                    canonicalHomeTeam,
                    standardizedHomeTeam,
                    homeTeamGender,
                    tx,
                    cancellationToken);
                preferredHomeTeam = NormalizePreferredNationalTeamName(preferredHomeTeam);

                preferredAwayTeam = await ResolvePreferredNationalTeamNameAsync(
                    conn,
                    canonicalAwayTeam,
                    standardizedAwayTeam,
                    awayTeamGender,
                    tx,
                    cancellationToken);
                preferredAwayTeam = NormalizePreferredNationalTeamName(preferredAwayTeam);

                standardizedHomeTeam = preferredHomeTeam;
                standardizedAwayTeam = preferredAwayTeam;
            }

            PositionLookupResult positions;
            if (isNationalTeamsMatch)
            {
                positions = await ResolveFifaRankingAsync(
                    conn,
                    standardizedHomeTeam,
                    standardizedAwayTeam,
                    homeTeamGender,
                    tx,
                    cancellationToken);
            }
            else
            {
                positions = await ResolveClubStandingsAsync(
                    conn,
                    standardizedLeague,
                    standardizedHomeTeam,
                    standardizedAwayTeam,
                    tx,
                    cancellationToken);
            }

            return new ResolvedTeamContext
            {
                StandardizedLeague = CanonicalNameCatalog.CanonicalizeLeague(standardizedLeague),
                StandardizedHomeTeam = CanonicalNameCatalog.CanonicalizeTeam(standardizedHomeTeam),
                StandardizedAwayTeam = CanonicalNameCatalog.CanonicalizeTeam(standardizedAwayTeam),
                PreferredHomeTeam = CanonicalNameCatalog.CanonicalizeTeam(preferredHomeTeam),
                PreferredAwayTeam = CanonicalNameCatalog.CanonicalizeTeam(preferredAwayTeam),
                TotalTeams = positions.TotalTeams,
                HomeTeamPosition = positions.HomeTeamPosition,
                AwayTeamPosition = positions.AwayTeamPosition
            };
        }

        private static string NormalizeGenero(string? genero)
        {
            if (string.IsNullOrWhiteSpace(genero))
                return "M";

            var normalized = genero.Trim().ToLowerInvariant();
            return normalized.Contains("fem") || normalized.Contains("women")
                ? "F"
                : "M";
        }

        private static string NormalizeGender(string? gender)
        {
            if (string.IsNullOrWhiteSpace(gender))
                return "M";

            var normalized = gender.Trim().ToUpperInvariant();
            return normalized is "F" or "U" ? normalized : "M";
        }

        private static string NormalizeRequired(string value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private async Task<string> ResolveStandardizedLeagueAsync(
            SqlConnection conn,
            string league,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            var leagueClean = NormalizeRequired(league);
            if (leagueClean.Length == 0)
                return leagueClean;

            const string sql = """
SELECT TOP (1) lm.StandardizedLeague
FROM dbo.LeagueMapping lm
WHERE LTRIM(RTRIM(lm.SourceLeague)) = @LeagueClean
   OR LTRIM(RTRIM(lm.StandardizedLeague)) = @LeagueClean
ORDER BY
    CASE
        WHEN LTRIM(RTRIM(lm.StandardizedLeague)) = @LeagueClean THEN 0
        WHEN LTRIM(RTRIM(lm.SourceLeague)) = @LeagueClean THEN 1
        ELSE 2
    END,
    lm.MappingId DESC;
""";

            var command = new CommandDefinition(
                sql,
                new { LeagueClean = leagueClean },
                tx,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            var standardized = await conn.QueryFirstOrDefaultAsync<string?>(command);
            return CanonicalNameCatalog.CanonicalizeLeague(string.IsNullOrWhiteSpace(standardized) ? leagueClean : standardized.Trim());
        }

        private async Task<string> ResolveStandardizedTeamAsync(
            SqlConnection conn,
            string team,
            string standardizedLeague,
            string league,
            string teamGender,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            var teamClean = NormalizeRequired(team);
            var leagueClean = NormalizeRequired(league);
            var standardizedLeagueClean = NormalizeRequired(standardizedLeague);
            if (teamClean.Length == 0)
                return teamClean;

            const string sql = """
SELECT TOP (1) tm.StandardizedTeam
FROM dbo.TeamMapping tm
WHERE (
        LTRIM(RTRIM(tm.SourceTeam)) = @TeamClean
        OR LTRIM(RTRIM(tm.StandardizedTeam)) = @TeamClean
      )
  AND (
        LTRIM(RTRIM(tm.League)) = @LeagueClean
        OR LTRIM(RTRIM(tm.League)) = @StandardizedLeagueClean
        OR LTRIM(RTRIM(tm.League)) = 'GLOBAL'
      )
ORDER BY
    CASE
        WHEN LTRIM(RTRIM(tm.League)) = @StandardizedLeagueClean THEN 0
        WHEN LTRIM(RTRIM(tm.League)) = @LeagueClean THEN 1
        WHEN LTRIM(RTRIM(tm.League)) = 'GLOBAL' THEN 2
        ELSE 3
    END,
    tm.MappingId DESC;
""";

            var command = new CommandDefinition(
                sql,
                new
                {
                    TeamClean = teamClean,
                    LeagueClean = leagueClean,
                    StandardizedLeagueClean = standardizedLeagueClean
                },
                tx,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            var standardized = await conn.QueryFirstOrDefaultAsync<string?>(command);
            return CanonicalNameCatalog.CanonicalizeTeam(string.IsNullOrWhiteSpace(standardized) ? teamClean : standardized.Trim());
        }

        private async Task<string> ResolvePreferredNationalTeamNameAsync(
            SqlConnection conn,
            string originalTeam,
            string fallbackTeam,
            string teamGender,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            var teamClean = NormalizeRequired(originalTeam);
            if (teamClean.Length == 0)
                return NormalizeRequired(fallbackTeam);

            var fallbackClean = NormalizeRequired(fallbackTeam);
            var lookupCandidates = BuildNationalTeamLookupCandidates(teamClean, fallbackClean);

            const string globalSql = """
SELECT TOP (1) tm.StandardizedTeam
FROM dbo.TeamMapping tm
WHERE LTRIM(RTRIM(tm.League)) = 'GLOBAL'
  AND (
        LTRIM(RTRIM(tm.SourceTeam)) IN @TeamCandidates
        OR LTRIM(RTRIM(tm.StandardizedTeam)) IN @TeamCandidates
      )
ORDER BY tm.MappingId DESC;
""";

            var command = new CommandDefinition(
                globalSql,
                new
                {
                    TeamCandidates = lookupCandidates
                },
                tx,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            var globalStandardized = await conn.QueryFirstOrDefaultAsync<string?>(command);
            if (!string.IsNullOrWhiteSpace(globalStandardized))
                return globalStandardized.Trim();

            return ResolveNationalAliasFallback(teamClean, fallbackClean);
        }

        private static string[] BuildNationalTeamLookupCandidates(string originalTeam, string fallbackTeam)
        {
            return CanonicalNameCatalog.GetEquivalentTeamNames(originalTeam)
                .Concat(CanonicalNameCatalog.GetEquivalentTeamNames(fallbackTeam))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string ResolveNationalAliasFallback(string originalTeam, string fallbackTeam)
        {
            var preferred = CanonicalNameCatalog.CanonicalizeTeam(originalTeam);
            return string.IsNullOrWhiteSpace(preferred)
                ? CanonicalNameCatalog.CanonicalizeTeam(fallbackTeam)
                : preferred;
        }

        private static string NormalizePreferredNationalTeamName(string value)
        {
            var clean = NormalizeRequired(value);
            if (clean.Length == 0)
                return clean;

            return CanonicalNameCatalog.CanonicalizeTeam(clean);
        }

        private static string NormalizeNationalAliasKey(string value)
        {
            return CanonicalNameCatalog.NormalizeKey(value);
        }

        private async Task<bool> IsNationalTeamsMatchAsync(
            SqlConnection conn,
            string standardizedLeague,
            string standardizedHomeTeam,
            string standardizedAwayTeam,
            string homeTeamGender,
            string awayTeamGender,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            var homeIsCountry = await IsCountryTeamAsync(conn, standardizedHomeTeam, homeTeamGender, tx, cancellationToken);
            var awayIsCountry = await IsCountryTeamAsync(conn, standardizedAwayTeam, awayTeamGender, tx, cancellationToken);

            if (homeIsCountry && awayIsCountry)
                return true;

            return ContainsNationalCompetitionKeyword(standardizedLeague);
        }

        private Task<bool> IsCountryTeamAsync(
            SqlConnection conn,
            string standardizedTeam,
            string teamGender,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            _ = conn;
            _ = teamGender;
            _ = tx;
            _ = cancellationToken;

            if (string.IsNullOrWhiteSpace(standardizedTeam))
                return Task.FromResult(false);

            if (CanonicalNameCatalog.IsKnownNationalTeam(standardizedTeam))
                return Task.FromResult(true);

            return Task.FromResult(Regex.IsMatch(standardizedTeam, @"\bU(?:17|19|20|21|23)\b", RegexOptions.IgnoreCase));
        }

        private static bool ContainsNationalCompetitionKeyword(string? leagueName)
        {
            var league = NormalizeNationalAliasKey(leagueName ?? string.Empty);
            if (league.Length == 0)
                return false;

            return league.Contains("mundial")
                || league.Contains("world cup")
                || league.Contains("copa america")
                || league.Contains("euro")
                || league.Contains("nations league")
                || league.Contains("qualif")
                || league.Contains("eliminat")
                || league.Contains("fifa")
                || league.Contains("amistoso internacional")
                || league.Contains("international friendly")
                || league.Contains("gold cup")
                || league.Contains("africa cup")
                || league.Contains("selecciones");
        }

        private async Task<PositionLookupResult> ResolveClubStandingsAsync(
            SqlConnection conn,
            string standardizedLeague,
            string standardizedHomeTeam,
            string standardizedAwayTeam,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            const string sql = """
;WITH LatestSnapshot AS
(
    SELECT TOP (1) ts.SnapshotDate
    FROM dbo.TeamStandingsSnapshot ts
    WHERE LTRIM(RTRIM(ts.League)) = @League
    ORDER BY ts.SnapshotDate DESC, ts.TeamStandingsSnapshotId DESC
)
SELECT
    TotalTeams = CASE WHEN EXISTS (SELECT 1 FROM LatestSnapshot) THEN COUNT(1) END,
    HomeTeamPosition = MAX(CASE WHEN LTRIM(RTRIM(ts.Team)) = @HomeTeam THEN ts.Position END),
    AwayTeamPosition = MAX(CASE WHEN LTRIM(RTRIM(ts.Team)) = @AwayTeam THEN ts.Position END)
FROM dbo.TeamStandingsSnapshot ts
WHERE LTRIM(RTRIM(ts.League)) = @League
  AND ts.SnapshotDate = (SELECT SnapshotDate FROM LatestSnapshot);
""";

            var command = new CommandDefinition(
                sql,
                new
                {
                    League = standardizedLeague.Trim(),
                    HomeTeam = standardizedHomeTeam.Trim(),
                    AwayTeam = standardizedAwayTeam.Trim()
                },
                tx,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            var result = await conn.QueryFirstOrDefaultAsync<PositionLookupResult>(command);
            return result ?? new PositionLookupResult();
        }

        private async Task<PositionLookupResult> ResolveFifaRankingAsync(
            SqlConnection conn,
            string standardizedHomeTeam,
            string standardizedAwayTeam,
            string teamGender,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            var metadata = await GetFifaRankingSourceMetadataAsync(conn, tx, cancellationToken);
            if (metadata == null)
            {
                _logger.LogInformation(
                    "No se encontro tabla de ranking FIFA para {HomeTeam} vs {AwayTeam}. Se enviaran posiciones nulas.",
                    standardizedHomeTeam,
                    standardizedAwayTeam);

                return new PositionLookupResult();
            }

            var teamPredicate = $"LTRIM(RTRIM([{metadata.TeamColumn}]))";
            var rankingPredicate = $"TRY_CONVERT(INT, [{metadata.RankingColumn}])";
            var dateOrder = metadata.DateColumn == null
                ? "1"
                : $"[{metadata.DateColumn}] DESC";

            var sql = $"""
WITH LatestSnapshot AS
(
    SELECT TOP (1) {BuildSnapshotProjection(metadata)}
    FROM [{metadata.SchemaName}].[{metadata.TableName}]
    {BuildWhereClause(metadata, applyLatestSnapshot: false)}
    ORDER BY {dateOrder}
),
SnapshotRows AS
(
    SELECT
        TeamName = {teamPredicate},
        TeamRanking = {rankingPredicate}
    FROM [{metadata.SchemaName}].[{metadata.TableName}]
    {BuildWhereClause(metadata, applyLatestSnapshot: true)}
)
SELECT
    TotalTeams = COUNT(1),
    HomeTeamPosition = MAX(CASE WHEN SnapshotRows.TeamName = @HomeTeam THEN SnapshotRows.TeamRanking END),
    AwayTeamPosition = MAX(CASE WHEN SnapshotRows.TeamName = @AwayTeam THEN SnapshotRows.TeamRanking END)
FROM SnapshotRows
WHERE SnapshotRows.TeamRanking IS NOT NULL;
""";

            var parameters = new DynamicParameters();
            parameters.Add("HomeTeam", standardizedHomeTeam.Trim());
            parameters.Add("AwayTeam", standardizedAwayTeam.Trim());
            if (metadata.GenderColumn != null)
                parameters.Add("TeamGender", teamGender);

            var command = new CommandDefinition(
                sql,
                parameters,
                tx,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            var result = await conn.QueryFirstOrDefaultAsync<PositionLookupResult>(command);
            return result ?? new PositionLookupResult();
        }

        private static string BuildSnapshotProjection(FifaRankingSourceMetadata metadata)
        {
            if (metadata.DateColumn == null)
                return "SnapshotKey = CAST(1 AS INT)";

            return $"SnapshotKey = [{metadata.DateColumn}]";
        }

        private static string BuildWhereClause(FifaRankingSourceMetadata metadata, bool applyLatestSnapshot)
        {
            var filters = new System.Collections.Generic.List<string>();
            if (metadata.GenderColumn != null)
                filters.Add($"LTRIM(RTRIM([{metadata.GenderColumn}])) = @TeamGender");

            if (applyLatestSnapshot && metadata.DateColumn != null)
                filters.Add($"[{metadata.DateColumn}] = (SELECT TOP (1) SnapshotKey FROM LatestSnapshot)");

            return filters.Count == 0
                ? string.Empty
                : "WHERE " + string.Join(" AND ", filters);
        }

        private async Task<FifaRankingSourceMetadata?> GetFifaRankingSourceMetadataAsync(
            SqlConnection conn,
            SqlTransaction? tx,
            CancellationToken cancellationToken)
        {
            if (_fifaSourceResolved)
                return _fifaSourceMetadata;

            const string sql = """
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    TeamColumn = MAX(CASE WHEN c.name IN ('Team', 'Country', 'TeamName', 'NationalTeam') THEN c.name END),
    RankingColumn = MAX(CASE WHEN c.name IN ('Ranking', 'Position', 'Rank') THEN c.name END),
    DateColumn = MAX(CASE WHEN c.name IN ('SnapshotDate', 'RankingDate', 'CreatedAtUtc', 'CreatedAt', 'Date') THEN c.name END),
    GenderColumn = MAX(CASE WHEN c.name IN ('TeamGender', 'Gender') THEN c.name END),
    Score =
        CASE WHEN t.name LIKE '%Fifa%' THEN 100 ELSE 0 END +
        CASE WHEN t.name LIKE '%Ranking%' THEN 50 ELSE 0 END +
        CASE WHEN t.name LIKE '%National%' THEN 25 ELSE 0 END
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
INNER JOIN sys.columns c ON c.object_id = t.object_id
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name
HAVING MAX(CASE WHEN c.name IN ('Team', 'Country', 'TeamName', 'NationalTeam') THEN 1 ELSE 0 END) = 1
   AND MAX(CASE WHEN c.name IN ('Ranking', 'Position', 'Rank') THEN 1 ELSE 0 END) = 1
   AND (
        t.name LIKE '%Fifa%'
        OR t.name LIKE '%Ranking%'
        OR t.name LIKE '%National%'
        OR t.name LIKE '%Country%'
   )
ORDER BY Score DESC, s.name, t.name;
""";

            var command = new CommandDefinition(
                sql,
                transaction: tx,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            _fifaSourceMetadata = await conn.QueryFirstOrDefaultAsync<FifaRankingSourceMetadata>(command);
            _fifaSourceResolved = true;
            return _fifaSourceMetadata;
        }

        private sealed class ResolvedTeamContext : PositionLookupResult
        {
            public string StandardizedLeague { get; set; } = string.Empty;
            public string StandardizedHomeTeam { get; set; } = string.Empty;
            public string StandardizedAwayTeam { get; set; } = string.Empty;
            public string PreferredHomeTeam { get; set; } = string.Empty;
            public string PreferredAwayTeam { get; set; } = string.Empty;
        }

        private sealed class FifaRankingSourceMetadata
        {
            public string SchemaName { get; init; } = string.Empty;
            public string TableName { get; init; } = string.Empty;
            public string TeamColumn { get; init; } = string.Empty;
            public string RankingColumn { get; init; } = string.Empty;
            public string? DateColumn { get; init; }
            public string? GenderColumn { get; init; }
        }

        private class PositionLookupResult
        {
            public int? TotalTeams { get; init; }
            public int? HomeTeamPosition { get; init; }
            public int? AwayTeamPosition { get; init; }
        }
    }
}
