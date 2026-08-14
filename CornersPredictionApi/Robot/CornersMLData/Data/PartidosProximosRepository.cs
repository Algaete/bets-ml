using CornersMLData.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Data
{
    public sealed class PartidosProximosRepository
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<PartidosProximosRepository> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly TeamPositionResolver _teamPositionResolver;

        public PartidosProximosRepository(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            TeamPositionResolver teamPositionResolver,
            ILogger<PartidosProximosRepository> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _teamPositionResolver = teamPositionResolver;
            _logger = logger;
        }

        public async Task EnsureDatabaseObjectsAsync(CancellationToken cancellationToken = default)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            var scriptPath = Path.Combine(_environment.ContentRootPath, "SqlScripts", "Create_PartidosProximos.sql");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("No se encontró el script SQL de PartidosProximos.", scriptPath);

            var script = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            var batches = SplitSqlBatches(script);

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;

                await conn.ExecuteAsync(new CommandDefinition(
                    commandText: batch,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
            }
        }

        public async Task<int> SincronizarAsync(
            IReadOnlyCollection<PartidoProximoUpsertDto> partidos,
            PartidosProximosSyncOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (partidos == null || partidos.Count == 0)
                return 0;

            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
            options ??= new PartidosProximosSyncOptions();

            try
            {
                foreach (var partido in partidos)
                {
                    partido.Liga = CanonicalNameCatalog.CanonicalizeLeague(partido.Liga);
                    partido.EquipoLocal = CanonicalNameCatalog.CanonicalizeTeam(partido.EquipoLocal);
                    partido.EquipoVisita = CanonicalNameCatalog.CanonicalizeTeam(partido.EquipoVisita);

                    try
                    {
                        if (options.EnrichPositions)
                        {
                            await _teamPositionResolver.EnrichUpcomingMatchAsync(conn, partido, tx, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "No se pudieron resolver posiciones para PartidoProximo. Liga={Liga}, EquipoLocal={EquipoLocal}, EquipoVisita={EquipoVisita}. Se continuara con valores nulos.",
                            partido.Liga,
                            partido.EquipoLocal,
                            partido.EquipoVisita);
                    }

                    if (options.NormalizeAliases)
                    {
                        await NormalizeExistingAliasesAsync(conn, tx, partido, cancellationToken);
                    }
                    await UpsertPartidoProximoAsync(conn, tx, partido, cancellationToken);
                }

                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Sincronizacion de partidos proximos completada. TotalProcesados={TotalProcesados}",
                    partidos.Count);

                return partidos.Count;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private static async Task UpsertPartidoProximoAsync(
            SqlConnection conn,
            SqlTransaction tx,
            PartidoProximoUpsertDto partido,
            CancellationToken cancellationToken)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@FechaPartido", partido.FechaPartido);
            parameters.Add("@EquipoLocal", partido.EquipoLocal);
            parameters.Add("@EquipoVisita", partido.EquipoVisita);
            parameters.Add("@Liga", partido.Liga);
            parameters.Add("@Genero", partido.Genero);
            parameters.Add("@EsKnockout", partido.EsKnockout);
            parameters.Add("@TotalTeams", partido.TotalTeams);
            parameters.Add("@HomeTeamPosition", partido.HomeTeamPosition);
            parameters.Add("@AwayTeamPosition", partido.AwayTeamPosition);
            parameters.Add("@DataSource", partido.DataSource);
            parameters.Add("@ExternalFixtureId", partido.ExternalFixtureId);
            parameters.Add("@FixtureStatus", partido.FixtureStatus);

            var command = new CommandDefinition(
                commandText: "dbo.sp_UpsertPartidoProximo",
                parameters: parameters,
                transaction: tx,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 60,
                cancellationToken: cancellationToken);

            await conn.ExecuteAsync(command);
        }

        private async Task NormalizeExistingAliasesAsync(
            SqlConnection conn,
            SqlTransaction tx,
            PartidoProximoUpsertDto partido,
            CancellationToken cancellationToken)
        {
            var leagueCandidates = CanonicalNameCatalog.GetEquivalentLeagueNames(partido.Liga);
            var homeCandidates = _teamPositionResolver.BuildEquivalentTeamNames(partido.EquipoLocal);
            var awayCandidates = _teamPositionResolver.BuildEquivalentTeamNames(partido.EquipoVisita);

            if (leagueCandidates.Count == 0 || homeCandidates.Count == 0 || awayCandidates.Count == 0)
                return;

            var cleanupParameters = new DynamicParameters();
            cleanupParameters.Add("@FechaPartido", partido.FechaPartido);
            cleanupParameters.Add("@Liga", partido.Liga);
            cleanupParameters.Add("@EquipoLocal", partido.EquipoLocal);
            cleanupParameters.Add("@EquipoVisita", partido.EquipoVisita);
            cleanupParameters.Add("@LeagueCandidates", leagueCandidates);
            cleanupParameters.Add("@HomeCandidates", homeCandidates);
            cleanupParameters.Add("@AwayCandidates", awayCandidates);

            const string deleteDuplicatesSql = """
DELETE pp
FROM dbo.PartidosProximos pp
WHERE pp.FechaPartido = @FechaPartido
  AND pp.Liga IN @LeagueCandidates
  AND pp.EquipoLocal IN @HomeCandidates
  AND pp.EquipoVisita IN @AwayCandidates
  AND NOT (pp.EquipoLocal = @EquipoLocal AND pp.EquipoVisita = @EquipoVisita)
  AND EXISTS
  (
      SELECT 1
      FROM dbo.PartidosProximos exacto
      WHERE exacto.FechaPartido = @FechaPartido
        AND exacto.Liga = @Liga
        AND exacto.EquipoLocal = @EquipoLocal
        AND exacto.EquipoVisita = @EquipoVisita
  );
""";

            await conn.ExecuteAsync(new CommandDefinition(
                commandText: deleteDuplicatesSql,
                parameters: cleanupParameters,
                transaction: tx,
                commandType: CommandType.Text,
                commandTimeout: 60,
                cancellationToken: cancellationToken));

            const string deleteSwappedDuplicatesSql = """
DELETE pp
FROM dbo.PartidosProximos pp
WHERE pp.FechaPartido = @FechaPartido
  AND pp.Liga IN @LeagueCandidates
  AND pp.EquipoLocal IN @AwayCandidates
  AND pp.EquipoVisita IN @HomeCandidates
  AND NOT (pp.EquipoLocal = @EquipoLocal AND pp.EquipoVisita = @EquipoVisita);
""";

            await conn.ExecuteAsync(new CommandDefinition(
                commandText: deleteSwappedDuplicatesSql,
                parameters: cleanupParameters,
                transaction: tx,
                commandType: CommandType.Text,
                commandTimeout: 60,
                cancellationToken: cancellationToken));

            var updateParameters = new DynamicParameters();
            updateParameters.Add("@FechaPartido", partido.FechaPartido);
            updateParameters.Add("@Liga", partido.Liga);
            updateParameters.Add("@Genero", partido.Genero);
            updateParameters.Add("@EsKnockout", partido.EsKnockout);
            updateParameters.Add("@TotalTeams", partido.TotalTeams);
            updateParameters.Add("@HomeTeamPosition", partido.HomeTeamPosition);
            updateParameters.Add("@AwayTeamPosition", partido.AwayTeamPosition);
            updateParameters.Add("@EquipoLocal", partido.EquipoLocal);
            updateParameters.Add("@EquipoVisita", partido.EquipoVisita);
            updateParameters.Add("@LeagueCandidates", leagueCandidates);
            updateParameters.Add("@HomeCandidates", homeCandidates);
            updateParameters.Add("@AwayCandidates", awayCandidates);

            const string updateAliasesSql = """
UPDATE pp
SET pp.Liga = @Liga,
    pp.EquipoLocal = @EquipoLocal,
    pp.EquipoVisita = @EquipoVisita,
    pp.Genero = @Genero,
    pp.EsKnockout = @EsKnockout,
    pp.TotalTeams = @TotalTeams,
    pp.HomeTeamPosition = @HomeTeamPosition,
    pp.AwayTeamPosition = @AwayTeamPosition,
    pp.FechaActualizacion = SYSUTCDATETIME()
FROM dbo.PartidosProximos pp
WHERE pp.FechaPartido = @FechaPartido
  AND pp.Liga IN @LeagueCandidates
  AND pp.EquipoLocal IN @HomeCandidates
  AND pp.EquipoVisita IN @AwayCandidates
  AND NOT (pp.Liga = @Liga AND pp.EquipoLocal = @EquipoLocal AND pp.EquipoVisita = @EquipoVisita);
""";

            await conn.ExecuteAsync(new CommandDefinition(
                commandText: updateAliasesSql,
                parameters: updateParameters,
                transaction: tx,
                commandType: CommandType.Text,
                commandTimeout: 60,
                cancellationToken: cancellationToken));
        }

        private static List<string> SplitSqlBatches(string script)
        {
            return Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
    }
}
