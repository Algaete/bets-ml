using CornersMLData.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Data
{
    public sealed class MatchHistoryRepository
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MatchHistoryRepository> _logger;
        private readonly TeamPositionResolver _teamPositionResolver;

        public MatchHistoryRepository(
            IConfiguration configuration,
            ILogger<MatchHistoryRepository> logger,
            TeamPositionResolver teamPositionResolver)
        {
            _configuration = configuration;
            _logger = logger;
            _teamPositionResolver = teamPositionResolver;
        }

        public async Task<MatchHistoryPersistResult> UpsertMatchHistoryAsync(
            MatchHistoryUpsertDto matchDto,
            CancellationToken cancellationToken = default)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            var existingMatch = await FindExistingMatchAsync(conn, matchDto, cancellationToken);
            if (existingMatch != null)
            {
                matchDto.SourceMatchId = existingMatch.SourceMatchId ?? matchDto.SourceMatchId;
                await UpdateMatchHistoryAsync(conn, existingMatch.Id, matchDto, cancellationToken);

                _logger.LogInformation(
                    "Partido existente actualizado por fecha y equipos canonicos. Id={Id}, League={League}, MatchDate={MatchDate:yyyy-MM-dd}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}",
                    existingMatch.Id,
                    matchDto.League,
                    matchDto.MatchDate.Date,
                    matchDto.HomeTeam,
                    matchDto.AwayTeam);

                return new MatchHistoryPersistResult
                {
                    Status = MatchHistoryPersistStatus.Updated,
                    MatchId = existingMatch.Id,
                    DuplicateDetected = true
                };
            }

            try
            {
                var insertedId = await InsertMatchHistoryAsync(conn, matchDto, cancellationToken);
                _logger.LogInformation(
                    "Partido insertado correctamente. Id={Id}, SourceMatchId={SourceMatchId}, League={League}, Season={Season}, MatchDate={MatchDate:yyyy-MM-dd}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}",
                    insertedId,
                    matchDto.SourceMatchId,
                    matchDto.League,
                    matchDto.Season,
                    matchDto.MatchDate.Date,
                    matchDto.HomeTeam,
                    matchDto.AwayTeam);

                return new MatchHistoryPersistResult
                {
                    Status = MatchHistoryPersistStatus.Inserted,
                    MatchId = insertedId
                };
            }
            catch (SqlException sqlEx) when (IsDuplicateSqlException(sqlEx))
            {
                _logger.LogWarning(
                    "Partido duplicado detectado. SQL={SqlNumber}, SourceMatchId={SourceMatchId}, League={League}, Season={Season}, MatchDate={MatchDate:yyyy-MM-dd}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}",
                    sqlEx.Number,
                    matchDto.SourceMatchId,
                    matchDto.League,
                    matchDto.Season,
                    matchDto.MatchDate.Date,
                    matchDto.HomeTeam,
                    matchDto.AwayTeam);

                var existing = await FindExistingMatchAsync(conn, matchDto, cancellationToken);
                if (existing == null)
                {
                    throw new InvalidOperationException(
                        "El SP reportó duplicado pero no se pudo encontrar el partido existente para actualizar.",
                        sqlEx);
                }

                await UpdateMatchHistoryAsync(conn, existing.Id, matchDto, cancellationToken);
                _logger.LogInformation(
                    "Partido actualizado correctamente. Id={Id}, SourceMatchId={SourceMatchId}, League={League}, Season={Season}, MatchDate={MatchDate:yyyy-MM-dd}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}",
                    existing.Id,
                    matchDto.SourceMatchId,
                    matchDto.League,
                    matchDto.Season,
                    matchDto.MatchDate.Date,
                    matchDto.HomeTeam,
                    matchDto.AwayTeam);

                return new MatchHistoryPersistResult
                {
                    Status = MatchHistoryPersistStatus.Updated,
                    MatchId = existing.Id,
                    DuplicateDetected = true
                };
            }
        }

        public async Task<long> InsertMatchHistoryAsync(
            MatchHistoryUpsertDto matchDto,
            CancellationToken cancellationToken = default)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            return await InsertMatchHistoryAsync(conn, matchDto, cancellationToken);
        }

        public async Task UpdateMatchHistoryAsync(
            long id,
            MatchHistoryUpsertDto matchDto,
            CancellationToken cancellationToken = default)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await UpdateMatchHistoryAsync(conn, id, matchDto, cancellationToken);
        }

        public async Task<ExistingMatchLookupResult?> FindExistingMatchAsync(
            MatchHistoryUpsertDto matchDto,
            CancellationToken cancellationToken = default)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            return await FindExistingMatchAsync(conn, matchDto, cancellationToken);
        }

        internal static bool IsControlledSqlException(SqlException sqlEx)
            => (sqlEx.Number >= 50001 && sqlEx.Number <= 50021) || sqlEx.Number == 50099;

        internal static bool IsDuplicateSqlException(SqlException sqlEx) => sqlEx.Number == 50018;

        internal async Task<long> InsertMatchHistoryAsync(
            SqlConnection conn,
            MatchHistoryUpsertDto matchDto,
            CancellationToken cancellationToken)
        {
            try
            {
                await _teamPositionResolver.EnrichMatchHistoryAsync(conn, matchDto, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudieron resolver posiciones para MatchHistory. League={League}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}. Se continuara con valores nulos.",
                    matchDto.League,
                    matchDto.HomeTeam,
                    matchDto.AwayTeam);
            }

            await using var cmd = BuildStoredProcedureCommand(conn, "dbo.sp_InsertMatchHistory");
            MapMatchParameters(cmd, matchDto);
            EnsureOutputParameter(cmd, "@InsertedId", SqlDbType.BigInt);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            var insertedParam = cmd.Parameters
                .Cast<SqlParameter>()
                .FirstOrDefault(x => x.ParameterName.Equals("@InsertedId", StringComparison.OrdinalIgnoreCase));

            if (insertedParam?.Value == null || insertedParam.Value == DBNull.Value)
                throw new InvalidOperationException("dbo.sp_InsertMatchHistory no devolvió @InsertedId.");

            return Convert.ToInt64(insertedParam.Value);
        }

        internal async Task UpdateMatchHistoryAsync(
            SqlConnection conn,
            long id,
            MatchHistoryUpsertDto matchDto,
            CancellationToken cancellationToken)
        {
            try
            {
                await _teamPositionResolver.EnrichMatchHistoryAsync(conn, matchDto, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudieron resolver posiciones para actualizar MatchHistory. Id={Id}, League={League}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}. Se continuara con valores nulos.",
                    id,
                    matchDto.League,
                    matchDto.HomeTeam,
                    matchDto.AwayTeam);
            }

            await using var cmd = BuildStoredProcedureCommand(conn, "dbo.sp_UpdateMatchHistory");
            SetParameterIfExists(cmd, "@Id", id);
            SetParameterIfExists(cmd, "@MatchHistoryId", id);
            SetParameterIfExists(cmd, "@ExistingId", id);
            MapMatchParameters(cmd, matchDto);
            EnsureOutputParameter(cmd, "@RowsAffected", SqlDbType.Int);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        internal async Task<ExistingMatchLookupResult?> FindExistingMatchAsync(
            SqlConnection conn,
            MatchHistoryUpsertDto matchDto,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(matchDto.SourceMatchId))
            {
                const string bySourceSql = @"
SELECT TOP (1) Id, SourceMatchId
FROM dbo.MatchHistory WITH (NOLOCK)
WHERE SourceMatchId = @SourceMatchId
ORDER BY Id DESC;";

                await using var bySourceCmd = new SqlCommand(bySourceSql, conn);
                bySourceCmd.Parameters.AddWithValue("@SourceMatchId", matchDto.SourceMatchId!);

                await using var reader = await bySourceCmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    return new ExistingMatchLookupResult
                    {
                        Id = reader.GetInt64(0),
                        SourceMatchId = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1))
                    };
                }
            }

            const string byNaturalKeySql = @"
SELECT TOP (1) Id, SourceMatchId
FROM dbo.MatchHistory WITH (NOLOCK)
WHERE MatchDate = @MatchDate
  AND ISNULL(HomeTeamGender, 'M') = @HomeTeamGender
  AND ISNULL(AwayTeamGender, 'M') = @AwayTeamGender
  AND
  (
      (
          dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, ''), HomeTeam)) COLLATE Latin1_General_100_CI_AI =
              dbo.fn_CanonicalTeamName(@HomeTeam) COLLATE Latin1_General_100_CI_AI
          AND dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, ''), AwayTeam)) COLLATE Latin1_General_100_CI_AI =
              dbo.fn_CanonicalTeamName(@AwayTeam) COLLATE Latin1_General_100_CI_AI
      )
      OR
      (
          @HomeGoals IS NOT NULL
          AND @AwayGoals IS NOT NULL
          AND @HomeCorners IS NOT NULL
          AND @AwayCorners IS NOT NULL
          AND HomeGoals = @HomeGoals
          AND AwayGoals = @AwayGoals
          AND HomeCorners = @HomeCorners
          AND AwayCorners = @AwayCorners
          AND
          (
              dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, ''), HomeTeam)) COLLATE Latin1_General_100_CI_AI =
                  dbo.fn_CanonicalTeamName(@HomeTeam) COLLATE Latin1_General_100_CI_AI
              OR dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, ''), AwayTeam)) COLLATE Latin1_General_100_CI_AI =
                  dbo.fn_CanonicalTeamName(@AwayTeam) COLLATE Latin1_General_100_CI_AI
          )
      )
  )
ORDER BY CASE WHEN ApiFootballFixtureId IS NOT NULL THEN 0 ELSE 1 END, Id DESC;";

            await using var byNaturalKeyCmd = new SqlCommand(byNaturalKeySql, conn);
            byNaturalKeyCmd.Parameters.AddWithValue("@MatchDate", matchDto.MatchDate.Date);
            byNaturalKeyCmd.Parameters.AddWithValue("@HomeTeam", matchDto.HomeTeam);
            byNaturalKeyCmd.Parameters.AddWithValue("@AwayTeam", matchDto.AwayTeam);
            byNaturalKeyCmd.Parameters.Add("@HomeGoals", SqlDbType.Int).Value = matchDto.HomeGoals ?? (object)DBNull.Value;
            byNaturalKeyCmd.Parameters.Add("@AwayGoals", SqlDbType.Int).Value = matchDto.AwayGoals ?? (object)DBNull.Value;
            byNaturalKeyCmd.Parameters.Add("@HomeCorners", SqlDbType.Int).Value = matchDto.HomeCorners ?? (object)DBNull.Value;
            byNaturalKeyCmd.Parameters.Add("@AwayCorners", SqlDbType.Int).Value = matchDto.AwayCorners ?? (object)DBNull.Value;
            byNaturalKeyCmd.Parameters.AddWithValue(
                "@HomeTeamGender",
                string.IsNullOrWhiteSpace(matchDto.HomeTeamGender) ? "M" : matchDto.HomeTeamGender.Trim());
            byNaturalKeyCmd.Parameters.AddWithValue(
                "@AwayTeamGender",
                string.IsNullOrWhiteSpace(matchDto.AwayTeamGender) ? "M" : matchDto.AwayTeamGender.Trim());

            await using var naturalReader = await byNaturalKeyCmd.ExecuteReaderAsync(cancellationToken);
            if (await naturalReader.ReadAsync(cancellationToken))
            {
                return new ExistingMatchLookupResult
                {
                    Id = naturalReader.GetInt64(0),
                    SourceMatchId = naturalReader.IsDBNull(1) ? null : Convert.ToString(naturalReader.GetValue(1))
                };
            }

            return null;
        }

        private static SqlCommand BuildStoredProcedureCommand(SqlConnection conn, string procedureName)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = procedureName;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 60;
            SqlCommandBuilder.DeriveParameters(cmd);
            return cmd;
        }

        private static void MapMatchParameters(SqlCommand cmd, MatchHistoryUpsertDto matchDto)
        {
            SetParameterIfExists(cmd, "@League", matchDto.League);
            SetParameterIfExists(cmd, "@Season", matchDto.Season);
            SetParameterIfExists(cmd, "@MatchDate", matchDto.MatchDate.Date);
            SetParameterIfExists(cmd, "@HomeTeam", matchDto.HomeTeam);
            SetParameterIfExists(cmd, "@AwayTeam", matchDto.AwayTeam);
            SetParameterIfExists(cmd, "@HomeFormation", matchDto.HomeFormation);
            SetParameterIfExists(cmd, "@AwayFormation", matchDto.AwayFormation);
            SetParameterIfExists(cmd, "@HomeGoals", matchDto.HomeGoals);
            SetParameterIfExists(cmd, "@AwayGoals", matchDto.AwayGoals);
            SetParameterIfExists(cmd, "@HomeCorners", matchDto.HomeCorners);
            SetParameterIfExists(cmd, "@AwayCorners", matchDto.AwayCorners);
            SetParameterIfExists(cmd, "@HomeShots", matchDto.HomeShots);
            SetParameterIfExists(cmd, "@AwayShots", matchDto.AwayShots);
            SetParameterIfExists(cmd, "@HomeShotsOnGoal", matchDto.HomeShotsOnGoal);
            SetParameterIfExists(cmd, "@AwayShotsOnGoal", matchDto.AwayShotsOnGoal);
            SetParameterIfExists(cmd, "@HomePossession", matchDto.HomePossession);
            SetParameterIfExists(cmd, "@AwayPossession", matchDto.AwayPossession);
            SetParameterIfExists(cmd, "@IsKnockout", matchDto.IsKnockout);
            SetParameterIfExists(cmd, "@SourceMatchId", ParseSourceMatchId(matchDto.SourceMatchId));
            SetParameterIfExists(cmd, "@HomeTeamGender", string.IsNullOrWhiteSpace(matchDto.HomeTeamGender) ? "M" : matchDto.HomeTeamGender.Trim());
            SetParameterIfExists(cmd, "@AwayTeamGender", string.IsNullOrWhiteSpace(matchDto.AwayTeamGender) ? "M" : matchDto.AwayTeamGender.Trim());
            SetParameterIfExists(cmd, "@TotalTeams", matchDto.TotalTeams);
            SetParameterIfExists(cmd, "@HomeTeamPosition", matchDto.HomeTeamPosition);
            SetParameterIfExists(cmd, "@AwayTeamPosition", matchDto.AwayTeamPosition);
        }

        private static long? ParseSourceMatchId(string? sourceMatchId)
        {
            if (string.IsNullOrWhiteSpace(sourceMatchId))
                return null;

            return long.TryParse(sourceMatchId.Trim(), out var parsed) ? parsed : null;
        }

        private static void SetParameterIfExists(SqlCommand cmd, string parameterName, object? value)
        {
            var parameter = cmd.Parameters
                .Cast<SqlParameter>()
                .FirstOrDefault(x => x.ParameterName.Equals(parameterName, StringComparison.OrdinalIgnoreCase));

            if (parameter == null || parameter.Direction == ParameterDirection.ReturnValue)
                return;

            parameter.Value = value ?? DBNull.Value;
        }

        private static void EnsureOutputParameter(SqlCommand cmd, string parameterName, SqlDbType dbType)
        {
            var parameter = cmd.Parameters
                .Cast<SqlParameter>()
                .FirstOrDefault(x => x.ParameterName.Equals(parameterName, StringComparison.OrdinalIgnoreCase));

            if (parameter == null)
            {
                parameter = cmd.Parameters.Add(parameterName, dbType);
            }

            parameter.Direction = ParameterDirection.Output;
        }
    }

    public sealed class ExistingMatchLookupResult
    {
        public long Id { get; set; }
        public string? SourceMatchId { get; set; }
    }

    public sealed class MatchHistoryPersistResult
    {
        public MatchHistoryPersistStatus Status { get; set; }
        public long MatchId { get; set; }
        public bool DuplicateDetected { get; set; }
    }

    public enum MatchHistoryPersistStatus
    {
        Inserted = 1,
        Updated = 2
    }
}
