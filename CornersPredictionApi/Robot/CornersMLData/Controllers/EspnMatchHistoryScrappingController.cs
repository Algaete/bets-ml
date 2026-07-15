using CornersMLData.Data;
using CornersMLData.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Controllers
{
    /// <summary>
    /// Endpoints para poblar historial de partidos desde ESPN por dia, rango, temporada o multiples ligas.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class EspnMatchHistoryScrappingController : ControllerBase
    {
        private static readonly HttpClient HttpClient = BuildHttpClient();

        private static readonly Dictionary<string, EspnLeaguePreset> LeaguePresets =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["chi.1"] = new EspnLeaguePreset("chi.1", "Liga de Primera", false),
                ["chile"] = new EspnLeaguePreset("chi.1", "Liga de Primera", false),
                ["liga chilena"] = new EspnLeaguePreset("chi.1", "Liga de Primera", false),
                ["conmebol.libertadores"] = new EspnLeaguePreset("conmebol.libertadores", "Copa Libertadores", false),
                ["libertadores"] = new EspnLeaguePreset("conmebol.libertadores", "Copa Libertadores", false),
                ["conmebol.sudamericana"] = new EspnLeaguePreset("conmebol.sudamericana", "Copa Sudamericana", false),
                ["sudamericana"] = new EspnLeaguePreset("conmebol.sudamericana", "Copa Sudamericana", false),
                ["eng.1"] = new EspnLeaguePreset("eng.1", "Premier League", false),
                ["esp.1"] = new EspnLeaguePreset("esp.1", "LaLiga", false),
                ["ita.1"] = new EspnLeaguePreset("ita.1", "Serie A", false),
                ["ger.1"] = new EspnLeaguePreset("ger.1", "Bundesliga", false),
                ["fra.1"] = new EspnLeaguePreset("fra.1", "Ligue 1", false),
                ["arg.1"] = new EspnLeaguePreset("arg.1", "Liga Profesional Argentina", false),
                ["bra.1"] = new EspnLeaguePreset("bra.1", "Brasileirão", false),
                ["per.1"] = new EspnLeaguePreset("per.1", "Liga 1 Peru", false),
                ["peru"] = new EspnLeaguePreset("per.1", "Liga 1 Peru", false),
                ["liga peruana"] = new EspnLeaguePreset("per.1", "Liga 1 Peru", false),
                ["liga 1 peru"] = new EspnLeaguePreset("per.1", "Liga 1 Peru", false)
            };

        private readonly IConfiguration _configuration;
        private readonly MatchHistoryRepository _matchHistoryRepository;
        private readonly ILogger<EspnMatchHistoryScrappingController> _logger;
        private readonly TimeZoneInfo _chileTimeZone;

        public EspnMatchHistoryScrappingController(
            IConfiguration configuration,
            MatchHistoryRepository matchHistoryRepository,
            ILogger<EspnMatchHistoryScrappingController> logger)
        {
            _configuration = configuration;
            _matchHistoryRepository = matchHistoryRepository;
            _logger = logger;
            _chileTimeZone = ResolveChileTimeZone();
        }

        /// <summary>
        /// Retorna los presets de ligas soportadas por el scraper de ESPN y su mapeo hacia la liga de base de datos.
        /// </summary>
        /// <remarks>
        /// Endpoint de consulta sin parametros. Expone las claves de liga admitidas por la API para usarlas en los endpoints de scraping.
        /// </remarks>
        [HttpGet("league-presets")]
        [ProducesResponseType(typeof(List<EspnLeaguePresetResponse>), StatusCodes.Status200OK)]
        public IActionResult GetLeaguePresets()
        {
            var unique = LeaguePresets.Values
                .GroupBy(x => x.LeagueKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.DbLeague)
                .Select(x => new EspnLeaguePresetResponse
                {
                    LeagueKey = x.LeagueKey,
                    DbLeague = x.DbLeague,
                    DefaultIsKnockout = x.DefaultIsKnockout
                })
                .ToList();

            return Ok(unique);
        }

        /// <summary>
        /// Wrapper de conveniencia para ejecutar el scraper de ESPN sobre un rango de fechas en todas las ligas activas detectadas.
        /// </summary>
        /// <remarks>
        /// Recibe <c>fromDate</c> y <c>toDate</c> por <c>query string</c>. Internamente llama al endpoint multi-liga con los valores
        /// operativos por defecto para procesamiento y persistencia.
        /// </remarks>
        /// <param name="fromDate">Fecha inicial del rango.</param>
        /// <param name="toDate">Fecha final del rango.</param>
        [HttpPost("sincronizar/rango-fechas")]
        [ProducesResponseType(typeof(EspnMultiLeagueBatchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public Task<IActionResult> SincronizarRangoFechas(
            [FromQuery] DateTime fromDate,
            [FromQuery] DateTime toDate)
        {
            return ScrapeDateRangeAllLeagues(
                fromDate: fromDate,
                toDate: toDate,
                takePerLeague: 10000,
                parallelism: 4,
                dryRun: false,
                onlyCompleted: true,
                backwards: true,
                unknownFormationIfMissing: false,
                maxLeagues: 300);
        }

        /// <summary>
        /// Scrapea una sola fecha para una liga ESPN determinada y escribe los partidos encontrados en <c>MatchHistory</c>.
        /// </summary>
        /// <remarks>
        /// Recibe todos los parametros por <c>query string</c>. Es util para reprocesar un dia puntual sin recorrer la temporada completa.
        /// </remarks>
        /// <param name="league">Clave de liga ESPN, por ejemplo <c>chi.1</c> o <c>conmebol.libertadores</c>.</param>
        /// <param name="day">Dia a consultar. Si no se indica, se usa la fecha actual en horario de Chile.</param>
        /// <param name="take">Cantidad maxima de partidos a procesar.</param>
        /// <param name="parallelism">Paralelismo de procesamiento interno.</param>
        /// <param name="dryRun">Si es <c>true</c>, calcula el lote sin persistir cambios.</param>
        /// <param name="onlyCompleted">Si es <c>true</c>, limita la corrida a partidos finalizados.</param>
        /// <param name="backwards">Si es <c>true</c>, recorre los resultados en orden descendente.</param>
        /// <param name="dbLeague">Nombre opcional de liga a guardar en base de datos.</param>
        /// <param name="isKnockout">Permite forzar si la competicion debe marcarse como eliminacion directa.</param>
        /// <param name="unknownFormationIfMissing">Si es <c>true</c>, rellena la formacion faltante con <c>Unknown</c>.</param>
        [HttpPost("scrape-day")]
        [ProducesResponseType(typeof(EspnHistoryBatchResponse), StatusCodes.Status200OK)]
        public Task<IActionResult> ScrapeDay(
            [FromQuery] string league = "chi.1",
            [FromQuery] DateTime? day = null,
            [FromQuery] int take = 10000,
            [FromQuery] int parallelism = 4,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool onlyCompleted = true,
            [FromQuery] bool backwards = true,
            [FromQuery] string? dbLeague = null,
            [FromQuery] bool? isKnockout = null,
            [FromQuery] bool unknownFormationIfMissing = false)
        {
            var runDay = day?.Date
                ?? TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _chileTimeZone).Date;

            return ScrapeSeason(
                league: league,
                season: runDay.Year,
                take: take,
                parallelism: parallelism,
                dryRun: dryRun,
                onlyCompleted: onlyCompleted,
                backwards: backwards,
                dbLeague: dbLeague,
                isKnockout: isKnockout,
                unknownFormationIfMissing: unknownFormationIfMissing,
                fromDate: runDay,
                toDate: runDay);
        }

        /// <summary>
        /// Scrapea un rango de fechas dentro de una misma temporada para una liga ESPN determinada.
        /// </summary>
        /// <remarks>
        /// Recibe todos los parametros por <c>query string</c>. <c>fromDate</c> y <c>toDate</c> deben pertenecer al mismo anio
        /// para que el scraper opere dentro de una sola temporada ESPN.
        /// </remarks>
        /// <param name="league">Clave de liga ESPN.</param>
        /// <param name="fromDate">Fecha inicial del rango.</param>
        /// <param name="toDate">Fecha final del rango.</param>
        /// <param name="take">Cantidad maxima de partidos a procesar.</param>
        /// <param name="parallelism">Paralelismo de procesamiento interno.</param>
        /// <param name="dryRun">Si es <c>true</c>, no persiste cambios.</param>
        /// <param name="onlyCompleted">Si es <c>true</c>, usa solo partidos completados.</param>
        /// <param name="backwards">Si es <c>true</c>, procesa desde las fechas mas recientes.</param>
        /// <param name="dbLeague">Nombre opcional de liga a guardar en base de datos.</param>
        /// <param name="isKnockout">Permite forzar si la competencia es de eliminacion directa.</param>
        /// <param name="unknownFormationIfMissing">Si es <c>true</c>, rellena formaciones faltantes con <c>Unknown</c>.</param>
        [HttpPost("scrape-date-range")]
        [ProducesResponseType(typeof(EspnHistoryBatchResponse), StatusCodes.Status200OK)]
        public Task<IActionResult> ScrapeDateRange(
            [FromQuery] string league = "chi.1",
            [FromQuery] DateTime fromDate = default,
            [FromQuery] DateTime toDate = default,
            [FromQuery] int take = 10000,
            [FromQuery] int parallelism = 4,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool onlyCompleted = true,
            [FromQuery] bool backwards = true,
            [FromQuery] string? dbLeague = null,
            [FromQuery] bool? isKnockout = null,
            [FromQuery] bool unknownFormationIfMissing = false)
        {
            if (fromDate == default || toDate == default)
                return Task.FromResult<IActionResult>(BadRequest("Debes indicar fromDate y toDate."));

            var start = fromDate.Date;
            var end = toDate.Date;
            if (end < start)
                (start, end) = (end, start);

            if (start.Year != end.Year)
            {
                return Task.FromResult<IActionResult>(
                    BadRequest("fromDate y toDate deben pertenecer al mismo año para este endpoint."));
            }

            return ScrapeSeason(
                league: league,
                season: start.Year,
                take: take,
                parallelism: parallelism,
                dryRun: dryRun,
                onlyCompleted: onlyCompleted,
                backwards: backwards,
                dbLeague: dbLeague,
                isKnockout: isKnockout,
                unknownFormationIfMissing: unknownFormationIfMissing,
                fromDate: start,
                toDate: end);
        }

        /// <summary>
        /// Scrapea un rango de fechas en todas las ligas activas detectadas por ESPN y agrega los resultados de cada corrida.
        /// </summary>
        /// <remarks>
        /// Recibe todos los parametros por <c>query string</c>. Descubre ligas activas en el rango indicado, filtra competiciones femeninas
        /// y ejecuta una corrida independiente por liga y por anio cuando el rango cruza temporadas.
        /// </remarks>
        /// <param name="fromDate">Fecha inicial del rango.</param>
        /// <param name="toDate">Fecha final del rango.</param>
        /// <param name="takePerLeague">Cantidad maxima de partidos por liga.</param>
        /// <param name="parallelism">Paralelismo de procesamiento por corrida individual.</param>
        /// <param name="dryRun">Si es <c>true</c>, calcula sin persistir.</param>
        /// <param name="onlyCompleted">Si es <c>true</c>, limita a partidos completados.</param>
        /// <param name="backwards">Si es <c>true</c>, procesa desde lo mas reciente.</param>
        /// <param name="unknownFormationIfMissing">Si es <c>true</c>, rellena formaciones faltantes con <c>Unknown</c>.</param>
        /// <param name="maxLeagues">Cantidad maxima de ligas activas a considerar.</param>
        [HttpPost("scrape-date-range-all-leagues")]
        [ProducesResponseType(typeof(EspnMultiLeagueBatchResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ScrapeDateRangeAllLeagues(
            [FromQuery] DateTime fromDate = default,
            [FromQuery] DateTime toDate = default,
            [FromQuery] int takePerLeague = 10000,
            [FromQuery] int parallelism = 4,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool onlyCompleted = true,
            [FromQuery] bool backwards = true,
            [FromQuery] bool unknownFormationIfMissing = false,
            [FromQuery] int maxLeagues = 300)
        {
            if (fromDate == default || toDate == default)
                return BadRequest("Debes indicar fromDate y toDate.");

            var start = fromDate.Date;
            var end = toDate.Date;
            if (end < start)
                (start, end) = (end, start);

            if (takePerLeague <= 0) takePerLeague = 10000;
            if (takePerLeague > 20000) takePerLeague = 20000;

            if (parallelism <= 0) parallelism = 4;
            if (parallelism > 16) parallelism = 16;

            if (maxLeagues <= 0) maxLeagues = 300;
            if (maxLeagues > 500) maxLeagues = 500;

            var activeLeagues = await FetchActiveSoccerLeaguesAsync(start, end, maxLeagues);
            var leagueKeys = activeLeagues
                .Where(x => !IsWomenLeague(x.LeagueKey, x.LeagueName))
                .Select(x => x.LeagueKey)
                .ToList();
            var responses = new List<EspnHistoryBatchResponse>();

            foreach (var leagueKey in leagueKeys)
            {
                foreach (var yearRange in SplitDateRangeByYear(start, end))
                {
                    var response = await ScrapeLeagueInternalAsync(
                        league: leagueKey,
                        season: yearRange.Year,
                        take: takePerLeague,
                        parallelism: parallelism,
                        dryRun: dryRun,
                        onlyCompleted: onlyCompleted,
                        backwards: backwards,
                        dbLeague: null,
                        isKnockout: null,
                        unknownFormationIfMissing: unknownFormationIfMissing,
                        fromDate: yearRange.From,
                        toDate: yearRange.To);

                    responses.Add(response);
                }
            }

            var nonEmptyResponses = responses
                .Where(x => x.TotalDiscovered > 0 || x.TotalProcessed > 0 || x.ScoreboardErrors.Any())
                .ToList();

            var multiLeagueResponse = new EspnMultiLeagueBatchResponse
            {
                Message = "Scraping ESPN multi-liga finalizado.",
                DateFrom = start,
                DateTo = end,
                TotalLeagues = leagueKeys.Count,
                TotalLeagueRuns = responses.Count,
                TotalDiscovered = responses.Sum(x => x.TotalDiscovered),
                TotalProcessed = responses.Sum(x => x.TotalProcessed),
                Inserted = responses.Sum(x => x.Inserted),
                Updated = responses.Sum(x => x.Updated),
                Duplicates = responses.Sum(x => x.Duplicates),
                Skipped = responses.Sum(x => x.Skipped),
                Errors = responses.Sum(x => x.Errors),
                DryRuns = responses.Sum(x => x.DryRuns),
                ScoreboardErrors = responses.SelectMany(x => x.ScoreboardErrors).ToList(),
                DailyBreakdown = BuildMultiLeagueDailyBreakdown(nonEmptyResponses),
                PerLeague = nonEmptyResponses
            };

            _logger.LogInformation(
                "Resumen final ESPN multi-liga. From={FromDate:yyyy-MM-dd}, To={ToDate:yyyy-MM-dd}, Inserted={Inserted}, Updated={Updated}, Duplicates={Duplicates}, Skipped={Skipped}, Errors={Errors}",
                multiLeagueResponse.DateFrom,
                multiLeagueResponse.DateTo,
                multiLeagueResponse.Inserted,
                multiLeagueResponse.Updated,
                multiLeagueResponse.Duplicates,
                multiLeagueResponse.Skipped,
                multiLeagueResponse.Errors);

            return Ok(multiLeagueResponse);
        }

        /// <summary>
        /// Scrapea una temporada completa o un subrango de una liga ESPN y persiste el historial resultante.
        /// </summary>
        /// <remarks>
        /// Recibe todos los parametros por <c>query string</c>. Es el endpoint base del scraper ESPN y acepta temporada completa
        /// o un subrango opcional usando <c>fromDate</c> y <c>toDate</c>.
        /// </remarks>
        /// <param name="league">Clave de liga ESPN.</param>
        /// <param name="season">Temporada numerica a consultar.</param>
        /// <param name="take">Cantidad maxima de partidos a procesar.</param>
        /// <param name="parallelism">Paralelismo de procesamiento interno.</param>
        /// <param name="dryRun">Si es <c>true</c>, no persiste cambios.</param>
        /// <param name="onlyCompleted">Si es <c>true</c>, usa solo partidos completados.</param>
        /// <param name="backwards">Si es <c>true</c>, procesa desde lo mas reciente.</param>
        /// <param name="dbLeague">Nombre opcional de liga a guardar en base de datos.</param>
        /// <param name="isKnockout">Permite forzar si la competencia es de eliminacion directa.</param>
        /// <param name="unknownFormationIfMissing">Si es <c>true</c>, rellena formaciones faltantes con <c>Unknown</c>.</param>
        /// <param name="fromDate">Fecha inicial opcional para acotar la temporada.</param>
        /// <param name="toDate">Fecha final opcional para acotar la temporada.</param>
        [HttpPost("scrape-season")]
        [ProducesResponseType(typeof(EspnHistoryBatchResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ScrapeSeason(
            [FromQuery] string league = "chi.1",
            [FromQuery] int season = 2026,
            [FromQuery] int take = 1000,
            [FromQuery] int parallelism = 4,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool onlyCompleted = true,
            [FromQuery] bool backwards = true,
            [FromQuery] string? dbLeague = null,
            [FromQuery] bool? isKnockout = null,
            [FromQuery] bool unknownFormationIfMissing = false,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            var response = await ScrapeLeagueInternalAsync(
                league: league,
                season: season,
                take: take,
                parallelism: parallelism,
                dryRun: dryRun,
                onlyCompleted: onlyCompleted,
                backwards: backwards,
                dbLeague: dbLeague,
                isKnockout: isKnockout,
                unknownFormationIfMissing: unknownFormationIfMissing,
                fromDate: fromDate,
                toDate: toDate);

            return Ok(response);
        }

        private async Task<EspnHistoryBatchResponse> ScrapeLeagueInternalAsync(
            string league,
            int season,
            int take,
            int parallelism,
            bool dryRun,
            bool onlyCompleted,
            bool backwards,
            string? dbLeague,
            bool? isKnockout,
            bool unknownFormationIfMissing,
            DateTime? fromDate,
            DateTime? toDate)
        {
            if (string.IsNullOrWhiteSpace(league))
                return new EspnHistoryBatchResponse
                {
                    Message = "Debes indicar la liga ESPN (ej: chi.1, libertadores)."
                };

            if (season < 2000 || season > 2100)
                return new EspnHistoryBatchResponse
                {
                    Message = "El parámetro 'season' está fuera de rango."
                };

            if (take <= 0) take = 1000;
            if (take > 20000) take = 20000;

            if (parallelism <= 0) parallelism = 4;
            if (parallelism > 16) parallelism = 16;

            var preset = ResolveLeaguePreset(league);
            var leagueKey = preset?.LeagueKey ?? league.Trim();
            var resolvedLeagueName = NormalizeNullable(dbLeague) ?? preset?.DbLeague ?? "Liga";
            var resolvedKnockout = isKnockout ?? preset?.DefaultIsKnockout ?? false;
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (!dryRun && string.IsNullOrWhiteSpace(connStr))
            {
                return new EspnHistoryBatchResponse
                {
                    Message = "Connection string 'DefaultConnection' is not configured.",
                    LeagueKey = leagueKey,
                    League = resolvedLeagueName,
                    Season = season.ToString(CultureInfo.InvariantCulture)
                };
            }

            var effectiveDateRange = BuildDateRange(season, fromDate, toDate);

            var discoveredEvents = new List<EspnEventCandidate>();
            var seenEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scoreboardErrors = new List<string>();

            // ESPN suele truncar resultados cuando se consulta un rango anual completo.
            // Para histórico usamos ventanas cortas para recuperar más partidos.
            const int rangeChunkDays = 7;
            for (var chunkFrom = effectiveDateRange.From; chunkFrom <= effectiveDateRange.To; chunkFrom = chunkFrom.AddDays(rangeChunkDays))
            {
                var chunkTo = chunkFrom.AddDays(rangeChunkDays - 1);
                if (chunkTo > effectiveDateRange.To)
                    chunkTo = effectiveDateRange.To;

                try
                {
                    var rangeScoreboard = await FetchScoreboardRangeAsync(leagueKey, chunkFrom, chunkTo);
                    if (rangeScoreboard == null)
                        continue;

                    if (string.Equals(resolvedLeagueName, "Liga", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(rangeScoreboard.LeagueName))
                    {
                        resolvedLeagueName = rangeScoreboard.LeagueName!;
                    }

                    foreach (var candidate in rangeScoreboard.Events)
                    {
                        if (candidate.MatchDate < effectiveDateRange.From || candidate.MatchDate > effectiveDateRange.To)
                            continue;

                        if (onlyCompleted && !candidate.Completed)
                            continue;

                        if (!seenEventIds.Add(candidate.EventId))
                            continue;

                        discoveredEvents.Add(candidate);
                    }
                }
                catch (Exception ex)
                {
                    scoreboardErrors.Add($"range {chunkFrom:yyyy-MM-dd}..{chunkTo:yyyy-MM-dd}: {ex.Message}");
                }
            }

            // Fallback HTML para rangos cortos: algunas páginas de resultados muestran partidos
            // que el scoreboard API de la liga no devuelve, especialmente en amistosos.
            var totalDays = (effectiveDateRange.To - effectiveDateRange.From).Days + 1;
            if (totalDays > 0 && totalDays <= 14)
            {
                var htmlDates = Enumerable.Range(0, totalDays)
                    .Select(offset => effectiveDateRange.From.AddDays(offset))
                    .ToList();

                htmlDates = backwards
                    ? htmlDates.OrderByDescending(x => x).ToList()
                    : htmlDates.OrderBy(x => x).ToList();

                foreach (var date in htmlDates)
                {
                    try
                    {
                        var htmlCandidates = await FetchLeagueResultsPageCandidatesAsync(leagueKey, date);
                        foreach (var candidate in htmlCandidates)
                        {
                            if (candidate.MatchDate < effectiveDateRange.From || candidate.MatchDate > effectiveDateRange.To)
                                continue;

                            if (onlyCompleted && !candidate.Completed)
                                continue;

                            if (!seenEventIds.Add(candidate.EventId))
                                continue;

                            discoveredEvents.Add(candidate);
                        }
                    }
                    catch (Exception ex)
                    {
                        scoreboardErrors.Add($"html {date:yyyy-MM-dd}: {ex.Message}");
                    }
                }
            }

            // Fallback para ligas/temporadas donde ESPN no responde bien al rango completo.
            if (!discoveredEvents.Any())
            {
                var calendarDates = await FetchLeagueCalendarDatesAsync(leagueKey, season, effectiveDateRange.From, effectiveDateRange.To);
                var orderedDates = backwards
                    ? calendarDates.OrderByDescending(x => x).ToList()
                    : calendarDates.OrderBy(x => x).ToList();

                foreach (var date in orderedDates)
                {
                    EspnScoreboardDay? scoreboard;

                    try
                    {
                        scoreboard = await FetchScoreboardDayAsync(leagueKey, date);
                    }
                    catch (Exception ex)
                    {
                        scoreboardErrors.Add($"{date:yyyy-MM-dd}: {ex.Message}");
                        continue;
                    }

                    if (scoreboard == null)
                        continue;

                    if (string.Equals(resolvedLeagueName, "Liga", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(scoreboard.LeagueName))
                    {
                        resolvedLeagueName = scoreboard.LeagueName!;
                    }

                    foreach (var candidate in scoreboard.Events)
                    {
                        if (candidate.MatchDate < effectiveDateRange.From || candidate.MatchDate > effectiveDateRange.To)
                            continue;

                        if (onlyCompleted && !candidate.Completed)
                            continue;

                        if (!seenEventIds.Add(candidate.EventId))
                            continue;

                        discoveredEvents.Add(candidate);
                    }
                }
            }

            var toProcess = discoveredEvents
                .OrderByDescending(x => x.MatchDate)
                .Take(take)
                .ToList();

            if (!toProcess.Any())
            {
                return new EspnHistoryBatchResponse
                {
                    Message = "No se encontraron partidos para procesar en el rango indicado.",
                    LeagueKey = leagueKey,
                    League = resolvedLeagueName,
                    Season = season.ToString(CultureInfo.InvariantCulture),
                    DateFrom = effectiveDateRange.From,
                    DateTo = effectiveDateRange.To,
                    TotalDiscovered = discoveredEvents.Count,
                    TotalProcessed = 0,
                    ScoreboardErrors = scoreboardErrors
                };
            }

            var resultsBag = new ConcurrentBag<EspnHistoryProcessResult>();
            var indexCounter = 0;

            await Parallel.ForEachAsync(
                toProcess,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                async (candidate, cancellationToken) =>
                {
                    var index = Interlocked.Increment(ref indexCounter);
                    var result = await ProcessEventCandidateAsync(
                        candidate,
                        index,
                        resolvedLeagueName,
                        season,
                        resolvedKnockout,
                        leagueKey,
                        connStr,
                        dryRun,
                        unknownFormationIfMissing,
                        cancellationToken);

                    resultsBag.Add(result);
                });

            var orderedResults = resultsBag.OrderBy(x => x.Index).ToList();
            var dailyBreakdown = BuildDailyBreakdown(toProcess, orderedResults);

            var response = new EspnHistoryBatchResponse
            {
                Message = "Scraping ESPN finalizado.",
                LeagueKey = leagueKey,
                League = resolvedLeagueName,
                Season = season.ToString(CultureInfo.InvariantCulture),
                DateFrom = effectiveDateRange.From,
                DateTo = effectiveDateRange.To,
                TotalDiscovered = discoveredEvents.Count,
                TotalProcessed = orderedResults.Count,
                Inserted = orderedResults.Count(x => x.Status == "INSERTED"),
                Updated = orderedResults.Count(x => x.Status == "UPDATED"),
                Duplicates = orderedResults.Count(x => x.DuplicateDetected),
                Skipped = orderedResults.Count(x => x.Status == "SKIPPED"),
                Errors = orderedResults.Count(x => x.Status == "ERROR"),
                DryRuns = orderedResults.Count(x => x.Status == "DRY_RUN"),
                ScoreboardErrors = scoreboardErrors,
                DailyBreakdown = dailyBreakdown,
                Results = orderedResults
            };

            _logger.LogInformation(
                "Resumen final ESPN. LeagueKey={LeagueKey}, Season={Season}, Inserted={Inserted}, Updated={Updated}, Duplicates={Duplicates}, Skipped={Skipped}, Errors={Errors}",
                response.LeagueKey,
                response.Season,
                response.Inserted,
                response.Updated,
                response.Duplicates,
                response.Skipped,
                response.Errors);

            return response;
        }

        private async Task<EspnHistoryProcessResult> ProcessEventCandidateAsync(
            EspnEventCandidate candidate,
            int index,
            string dbLeague,
            int season,
            bool isKnockout,
            string leagueKey,
            string? connStr,
            bool dryRun,
            bool unknownFormationIfMissing,
            CancellationToken cancellationToken)
        {
            MatchHistoryUpsertDto? row = null;

            try
            {
                var summary = await FetchMatchSummaryAsync(leagueKey, candidate.EventId, cancellationToken);
                row = BuildHistoryRowFromEspn(candidate, summary, dbLeague, season, isKnockout, unknownFormationIfMissing);
                var validationError = ValidateHistoryRow(row);

                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    return new EspnHistoryProcessResult
                    {
                        Index = index,
                        EventId = candidate.EventId,
                        MatchDate = candidate.MatchDate,
                        HomeTeam = candidate.HomeTeam,
                        AwayTeam = candidate.AwayTeam,
                        Status = "SKIPPED",
                        Error = validationError,
                        Match = row
                    };
                }

                if (dryRun)
                {
                    return new EspnHistoryProcessResult
                    {
                        Index = index,
                        EventId = candidate.EventId,
                        MatchDate = candidate.MatchDate,
                        HomeTeam = row.HomeTeam,
                        AwayTeam = row.AwayTeam,
                        Status = "DRY_RUN",
                        Match = row
                    };
                }

                var persistResult = await _matchHistoryRepository.UpsertMatchHistoryAsync(row, cancellationToken);

                return new EspnHistoryProcessResult
                {
                    Index = index,
                    EventId = candidate.EventId,
                    MatchDate = candidate.MatchDate,
                    HomeTeam = row.HomeTeam,
                    AwayTeam = row.AwayTeam,
                    Status = persistResult.Status == MatchHistoryPersistStatus.Inserted ? "INSERTED" : "UPDATED",
                    InsertedId = persistResult.MatchId,
                    DuplicateDetected = persistResult.DuplicateDetected,
                    Match = row
                };
            }
            catch (SqlException sqlEx) when (MatchHistoryRepository.IsControlledSqlException(sqlEx))
            {
                _logger.LogWarning(
                    "Error SQL controlado en scraping ESPN. SQL={SqlNumber}, Message={SqlMessage}, EventId={EventId}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}",
                    sqlEx.Number,
                    sqlEx.Message,
                    candidate.EventId,
                    candidate.HomeTeam,
                    candidate.AwayTeam);

                return new EspnHistoryProcessResult
                {
                    Index = index,
                    EventId = candidate.EventId,
                    MatchDate = candidate.MatchDate,
                    HomeTeam = candidate.HomeTeam,
                    AwayTeam = candidate.AwayTeam,
                    Status = "SKIPPED",
                    Error = $"SQL {sqlEx.Number}: {sqlEx.Message}",
                    Detail = sqlEx.Message,
                    Match = row
                };
            }
            catch (Exception ex)
            {
                return new EspnHistoryProcessResult
                {
                    Index = index,
                    EventId = candidate.EventId,
                    MatchDate = candidate.MatchDate,
                    HomeTeam = candidate.HomeTeam,
                    AwayTeam = candidate.AwayTeam,
                    Status = "ERROR",
                    Error = ex.Message,
                    Detail = ex.ToString(),
                    Match = row
                };
            }
        }

        private (DateTime From, DateTime To) BuildDateRange(int season, DateTime? fromDate, DateTime? toDate)
        {
            var start = fromDate?.Date ?? new DateTime(season, 1, 1);
            var end = toDate?.Date ?? new DateTime(season, 12, 31);

            var chileToday = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _chileTimeZone).Date;
            if (season == chileToday.Year && end > chileToday)
                end = chileToday;

            if (end < start)
                (start, end) = (end, start);

            return (start, end);
        }

        private static List<EspnHistoryDailySummary> BuildDailyBreakdown(
            List<EspnEventCandidate> selectedEvents,
            List<EspnHistoryProcessResult> orderedResults)
        {
            var discoveredByDate = selectedEvents
                .GroupBy(x => x.MatchDate.Date)
                .ToDictionary(x => x.Key, x => x.Count());

            var resultsByDate = orderedResults
                .GroupBy(x => x.MatchDate.Date)
                .ToDictionary(x => x.Key, x => x.ToList());

            var allDates = discoveredByDate.Keys
                .Union(resultsByDate.Keys)
                .OrderBy(x => x)
                .ToList();

            var daily = new List<EspnHistoryDailySummary>(allDates.Count);

            foreach (var date in allDates)
            {
                resultsByDate.TryGetValue(date, out var rows);
                rows ??= new List<EspnHistoryProcessResult>();

                daily.Add(new EspnHistoryDailySummary
                {
                    Date = date,
                    Discovered = discoveredByDate.TryGetValue(date, out var discovered) ? discovered : 0,
                    Processed = rows.Count,
                    Inserted = rows.Count(x => x.Status == "INSERTED"),
                    Updated = rows.Count(x => x.Status == "UPDATED"),
                    Duplicates = rows.Count(x => x.DuplicateDetected),
                    Skipped = rows.Count(x => x.Status == "SKIPPED"),
                    Errors = rows.Count(x => x.Status == "ERROR"),
                    DryRuns = rows.Count(x => x.Status == "DRY_RUN")
                });
            }

            return daily;
        }

        private static List<EspnHistoryDailySummary> BuildMultiLeagueDailyBreakdown(
            List<EspnHistoryBatchResponse> responses)
        {
            var aggregate = new Dictionary<DateTime, EspnHistoryDailySummary>();

            foreach (var response in responses)
            {
                foreach (var day in response.DailyBreakdown)
                {
                    if (!aggregate.TryGetValue(day.Date.Date, out var existing))
                    {
                        existing = new EspnHistoryDailySummary
                        {
                            Date = day.Date.Date
                        };

                        aggregate[day.Date.Date] = existing;
                    }

                    existing.Discovered += day.Discovered;
                    existing.Processed += day.Processed;
                    existing.Inserted += day.Inserted;
                    existing.Updated += day.Updated;
                    existing.Duplicates += day.Duplicates;
                    existing.Skipped += day.Skipped;
                    existing.Errors += day.Errors;
                    existing.DryRuns += day.DryRuns;
                }
            }

            return aggregate.Values
                .OrderBy(x => x.Date)
                .ToList();
        }

        private static List<EspnDateRangeSegment> SplitDateRangeByYear(DateTime from, DateTime to)
        {
            var result = new List<EspnDateRangeSegment>();
            var cursor = from.Date;
            var end = to.Date;

            while (cursor <= end)
            {
                var segmentEnd = new DateTime(cursor.Year, 12, 31);
                if (segmentEnd > end)
                    segmentEnd = end;

                result.Add(new EspnDateRangeSegment
                {
                    Year = cursor.Year,
                    From = cursor,
                    To = segmentEnd
                });

                cursor = segmentEnd.AddDays(1);
            }

            return result;
        }

        private async Task<List<string>> FetchAllSoccerLeagueKeysAsync(int maxLeagues)
        {
            const string url = "https://sports.core.api.espn.com/v2/sports/soccer/leagues?limit=500";
            using var doc = await FetchJsonDocumentWithRetryAsync(url, CancellationToken.None);

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!doc.RootElement.TryGetProperty("items", out var itemsElement)
                || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in itemsElement.EnumerateArray())
            {
                var rawRef = GetStringOrNull(item, "$ref");
                if (string.IsNullOrWhiteSpace(rawRef))
                    continue;

                var marker = "/leagues/";
                var idx = rawRef!.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    continue;

                var leagueKey = rawRef.Substring(idx + marker.Length);
                var queryIdx = leagueKey.IndexOf("?", StringComparison.Ordinal);
                if (queryIdx >= 0)
                    leagueKey = leagueKey.Substring(0, queryIdx);

                leagueKey = leagueKey.Trim();
                if (string.IsNullOrWhiteSpace(leagueKey))
                    continue;

                if (!seen.Add(leagueKey))
                    continue;

                result.Add(leagueKey);
                if (result.Count >= maxLeagues)
                    break;
            }

            return result;
        }

        private async Task<List<EspnActiveLeagueInfo>> FetchActiveSoccerLeaguesAsync(DateTime from, DateTime to, int maxLeagues)
        {
            var allLeagueKeys = await FetchAllSoccerLeagueKeysAsync(maxLeagues);
            var active = new List<EspnActiveLeagueInfo>();

            foreach (var leagueKey in allLeagueKeys)
            {
                try
                {
                    var scoreboard = await FetchScoreboardRangeAsync(leagueKey, from, to);
                    if (scoreboard == null || !scoreboard.Events.Any())
                        continue;

                    active.Add(new EspnActiveLeagueInfo
                    {
                        LeagueKey = leagueKey,
                        LeagueName = NormalizeNullable(scoreboard.LeagueName)
                    });
                }
                catch
                {
                    // Ignore probe failures here; the league-level scrape will surface real errors if needed.
                }
            }

            return active;
        }

        private static HttpClient BuildHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; FootballDataRep/1.0; +https://www.espn.cl/futbol/resultados)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            return client;
        }

        private async Task<List<DateTime>> FetchLeagueCalendarDatesAsync(
            string leagueKey,
            int season,
            DateTime from,
            DateTime to)
        {
            var url = $"https://site.api.espn.com/apis/site/v2/sports/soccer/{leagueKey}/scoreboard";
            using var rootDoc = await FetchJsonDocumentWithRetryAsync(url, CancellationToken.None);

            var result = new HashSet<DateTime>();

            if (rootDoc.RootElement.TryGetProperty("leagues", out var leaguesElement)
                && leaguesElement.ValueKind == JsonValueKind.Array
                && leaguesElement.GetArrayLength() > 0)
            {
                var leagueElement = leaguesElement[0];
                if (leagueElement.TryGetProperty("calendar", out var calendarElement)
                    && calendarElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in calendarElement.EnumerateArray())
                    {
                        AppendCalendarDatesFromElement(item, season, from, to, result);
                    }
                }
            }

            if (!result.Any())
            {
                for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
                    result.Add(date);
            }

            return result.OrderBy(x => x).ToList();
        }

        private void AppendCalendarDatesFromElement(
            JsonElement element,
            int season,
            DateTime from,
            DateTime to,
            HashSet<DateTime> target)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var raw = element.GetString();
                if (TryParseEspnDate(raw, out var parsed))
                {
                    var chileDate = TimeZoneInfo.ConvertTime(parsed, _chileTimeZone).Date;
                    if (chileDate.Year == season && chileDate >= from && chileDate <= to)
                    {
                        target.Add(chileDate);
                    }
                }

                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                    AppendCalendarDatesFromElement(child, season, from, to, target);

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            if (element.TryGetProperty("entries", out var entries)
                && entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                    AppendCalendarDatesFromElement(entry, season, from, to, target);
            }

            var startRaw = GetStringOrNull(element, "startDate");
            var endRaw = GetStringOrNull(element, "endDate");

            if (!TryParseEspnDate(startRaw, out var startParsed))
                return;

            var startDate = TimeZoneInfo.ConvertTime(startParsed, _chileTimeZone).Date;
            var endDate = startDate;

            if (TryParseEspnDate(endRaw, out var endParsed))
                endDate = TimeZoneInfo.ConvertTime(endParsed, _chileTimeZone).Date;

            if (endDate < startDate)
                (startDate, endDate) = (endDate, startDate);

            if (endDate < from || startDate > to)
                return;

            if (startDate < from) startDate = from;
            if (endDate > to) endDate = to;

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (date.Year == season)
                    target.Add(date);
            }
        }

        private async Task<EspnScoreboardDay?> FetchScoreboardDayAsync(string leagueKey, DateTime date)
        {
            var dateParam = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var url = $"https://site.api.espn.com/apis/site/v2/sports/soccer/{leagueKey}/scoreboard?dates={dateParam}";

            using var doc = await FetchJsonDocumentWithRetryAsync(url, CancellationToken.None);
            return ParseScoreboardDayDocument(doc);
        }

        private async Task<EspnScoreboardDay?> FetchScoreboardRangeAsync(string leagueKey, DateTime from, DateTime to)
        {
            var fromParam = from.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var toParam = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var url = $"https://site.api.espn.com/apis/site/v2/sports/soccer/{leagueKey}/scoreboard?dates={fromParam}-{toParam}&limit=5000";

            using var doc = await FetchJsonDocumentWithRetryAsync(url, CancellationToken.None);
            return ParseScoreboardDayDocument(doc);
        }

        private async Task<List<EspnEventCandidate>> FetchLeagueResultsPageCandidatesAsync(string leagueKey, DateTime date)
        {
            var url = $"https://www.espn.cl/futbol/resultados/_/liga/{leagueKey}/_/fecha/{date:yyyyMMdd}";
            var html = await FetchStringWithRetryAsync(url, CancellationToken.None);
            return ParseLeagueResultsPageCandidates(html, date);
        }

        private static List<EspnEventCandidate> ParseLeagueResultsPageCandidates(string html, DateTime date)
        {
            var result = new List<EspnEventCandidate>();
            if (string.IsNullOrWhiteSpace(html))
                return result;

            var eventPattern = new Regex(
                @"<section class=""Scoreboard[^""]*"" id=""(?<id>\d+)"">(?<body>.*?)(?=<section class=""Scoreboard[^""]*"" id=""\d+""|<section class=""Card PageFooter""|</body>)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in eventPattern.Matches(html))
            {
                var eventId = NormalizeNullable(match.Groups["id"].Value);
                var body = match.Groups["body"].Value;
                if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(body))
                    continue;

                var teamNames = Regex.Matches(
                        body,
                        @"ScoreCell__TeamName[^>]*>(?<name>.*?)</div>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase)
                    .Select(x => StripHtml(x.Groups["name"].Value))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(2)
                    .ToList();

                if (teamNames.Count < 2)
                    continue;

                var scoreValues = Regex.Matches(
                        body,
                        @"ScoreCell__Score[^>]*>(?<score>\d+)</div>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase)
                    .Select(x => ParseIntOrNull(x.Groups["score"].Value))
                    .Where(x => x.HasValue)
                    .Take(2)
                    .ToList();

                result.Add(new EspnEventCandidate
                {
                    EventId = eventId!,
                    MatchDate = date.Date,
                    MatchDateUtc = new DateTimeOffset(date.Date, TimeSpan.Zero),
                    HomeTeam = NormalizeRequired(teamNames[0]),
                    AwayTeam = NormalizeRequired(teamNames[1]),
                    HomeGoals = scoreValues.Count > 0 ? scoreValues[0] : null,
                    AwayGoals = scoreValues.Count > 1 ? scoreValues[1] : null,
                    Completed = true,
                    StatusDescription = "Final"
                });
            }

            return result;
        }

        private EspnScoreboardDay ParseScoreboardDayDocument(JsonDocument doc)
        {
            var day = new EspnScoreboardDay();

            if (doc.RootElement.TryGetProperty("leagues", out var leaguesElement)
                && leaguesElement.ValueKind == JsonValueKind.Array
                && leaguesElement.GetArrayLength() > 0)
            {
                day.LeagueName = GetStringOrNull(leaguesElement[0], "name");
            }

            if (!doc.RootElement.TryGetProperty("events", out var eventsElement)
                || eventsElement.ValueKind != JsonValueKind.Array)
            {
                return day;
            }

            foreach (var evt in eventsElement.EnumerateArray())
            {
                var eventId = GetStringOrNull(evt, "id");
                if (string.IsNullOrWhiteSpace(eventId))
                    continue;

                var competitions = evt.TryGetProperty("competitions", out var competitionsElement)
                    && competitionsElement.ValueKind == JsonValueKind.Array
                    && competitionsElement.GetArrayLength() > 0
                    ? competitionsElement[0]
                    : default;

                if (competitions.ValueKind == JsonValueKind.Undefined)
                    continue;

                var competitors = competitions.TryGetProperty("competitors", out var competitorsElement)
                    && competitorsElement.ValueKind == JsonValueKind.Array
                    ? competitorsElement.EnumerateArray().ToList()
                    : new List<JsonElement>();

                var home = competitors.FirstOrDefault(x => string.Equals(GetStringOrNull(x, "homeAway"), "home", StringComparison.OrdinalIgnoreCase));
                var away = competitors.FirstOrDefault(x => string.Equals(GetStringOrNull(x, "homeAway"), "away", StringComparison.OrdinalIgnoreCase));

                if (home.ValueKind == JsonValueKind.Undefined || away.ValueKind == JsonValueKind.Undefined)
                    continue;

                var homeName = GetNestedStringOrNull(home, "team", "displayName");
                var awayName = GetNestedStringOrNull(away, "team", "displayName");

                if (string.IsNullOrWhiteSpace(homeName) || string.IsNullOrWhiteSpace(awayName))
                    continue;

                var competitionDateRaw = GetStringOrNull(competitions, "date") ?? GetStringOrNull(evt, "date");
                if (!TryParseEspnDate(competitionDateRaw, out var matchUtc))
                    continue;

                var statusType = evt.TryGetProperty("status", out var statusElement)
                    && statusElement.TryGetProperty("type", out var typeElement)
                    ? typeElement
                    : default;

                var completed = statusType.ValueKind != JsonValueKind.Undefined
                    && GetBoolOrDefault(statusType, "completed");
                var statusDescription = statusType.ValueKind != JsonValueKind.Undefined
                    ? GetStringOrNull(statusType, "description")
                    : null;

                day.Events.Add(new EspnEventCandidate
                {
                    EventId = eventId!,
                    MatchDate = TimeZoneInfo.ConvertTime(matchUtc, _chileTimeZone).Date,
                    MatchDateUtc = matchUtc,
                    HomeTeam = homeName!,
                    AwayTeam = awayName!,
                    HomeGoals = ParseIntOrNull(GetStringOrNull(home, "score")),
                    AwayGoals = ParseIntOrNull(GetStringOrNull(away, "score")),
                    Completed = completed,
                    StatusDescription = statusDescription
                });
            }

            return day;
        }

        private static async Task<JsonDocument> FetchJsonDocumentWithRetryAsync(string url, CancellationToken cancellationToken)
        {
            Exception? last = null;

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await HttpClient.SendAsync(req, cancellationToken);

                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                        throw new Exception($"HTTP {(int)resp.StatusCode} en ESPN ({url}). Body: {Truncate(body, 240)}");
                    }

                    await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < 3)
                        await Task.Delay(500 * attempt, cancellationToken);
                }
            }

            throw new Exception($"Error consultando ESPN: {url}", last);
        }

        private static async Task<string> FetchStringWithRetryAsync(string url, CancellationToken cancellationToken)
        {
            Exception? last = null;

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await HttpClient.SendAsync(req, cancellationToken);

                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                        throw new Exception($"HTTP {(int)resp.StatusCode} en ESPN ({url}). Body: {Truncate(body, 240)}");
                    }

                    return await resp.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < 3)
                        await Task.Delay(500 * attempt, cancellationToken);
                }
            }

            throw new Exception($"Error consultando ESPN HTML: {url}", last);
        }

        private async Task<EspnSummaryDto> FetchMatchSummaryAsync(string leagueKey, string eventId, CancellationToken cancellationToken)
        {
            var url = $"https://site.api.espn.com/apis/site/v2/sports/soccer/{leagueKey}/summary?event={eventId}";
            using var doc = await FetchJsonDocumentWithRetryAsync(url, cancellationToken);

            var summary = new EspnSummaryDto();

            if (doc.RootElement.TryGetProperty("rosters", out var rostersElement)
                && rostersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var roster in rostersElement.EnumerateArray())
                {
                    var homeAway = GetStringOrNull(roster, "homeAway");
                    var teamName = GetNestedStringOrNull(roster, "team", "displayName");
                    var formation = NormalizeNullable(GetStringOrNull(roster, "formation"));

                    if (!string.IsNullOrWhiteSpace(homeAway))
                    {
                        summary.FormationByHomeAway[homeAway!] = formation;
                    }

                    if (!string.IsNullOrWhiteSpace(teamName))
                    {
                        summary.TeamNameByHomeAway[homeAway ?? ""] = teamName;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("boxscore", out var boxscoreElement)
                && boxscoreElement.TryGetProperty("teams", out var teamsElement)
                && teamsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var teamEntry in teamsElement.EnumerateArray())
                {
                    var teamName = GetNestedStringOrNull(teamEntry, "team", "displayName");
                    if (string.IsNullOrWhiteSpace(teamName))
                        continue;

                    var normalizedTeamKey = NormalizeTeamKey(teamName!);
                    var stats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (teamEntry.TryGetProperty("statistics", out var statsElement)
                        && statsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var stat in statsElement.EnumerateArray())
                        {
                            var statName = GetStringOrNull(stat, "name");
                            var displayValue = NormalizeNullable(GetStringOrNull(stat, "displayValue"));
                            if (string.IsNullOrWhiteSpace(statName) || string.IsNullOrWhiteSpace(displayValue))
                                continue;

                            stats[statName!] = displayValue!;
                        }
                    }

                    summary.StatsByTeamKey[normalizedTeamKey] = stats;
                }
            }

            return summary;
        }

        private MatchHistoryUpsertDto BuildHistoryRowFromEspn(
            EspnEventCandidate candidate,
            EspnSummaryDto summary,
            string league,
            int season,
            bool isKnockout,
            bool unknownFormationIfMissing)
        {
            var homeTeam = NormalizeRequired(candidate.HomeTeam);
            var awayTeam = NormalizeRequired(candidate.AwayTeam);

            var homeKey = NormalizeTeamKey(homeTeam);
            var awayKey = NormalizeTeamKey(awayTeam);
            var homeStats = summary.StatsByTeamKey.TryGetValue(homeKey, out var hs) ? hs : null;
            var awayStats = summary.StatsByTeamKey.TryGetValue(awayKey, out var aws) ? aws : null;

            var homeCorners = homeStats == null ? null : ParseNullableInt(GetStatValue(homeStats, "wonCorners"));
            var awayCorners = awayStats == null ? null : ParseNullableInt(GetStatValue(awayStats, "wonCorners"));
            var homeShots = homeStats == null ? null : ParseNullableInt(GetStatValue(homeStats, "totalShots"));
            var awayShots = awayStats == null ? null : ParseNullableInt(GetStatValue(awayStats, "totalShots"));
            var homeShotsOnGoal = homeStats == null ? null : ParseNullableInt(GetStatValue(homeStats, "shotsOnTarget"));
            var awayShotsOnGoal = awayStats == null ? null : ParseNullableInt(GetStatValue(awayStats, "shotsOnTarget"));
            var homePossession = homeStats == null ? null : ParseNullablePossession(GetStatValue(homeStats, "possessionPct"));
            var awayPossession = awayStats == null ? null : ParseNullablePossession(GetStatValue(awayStats, "possessionPct"));

            var homeFormation = NormalizeFormation(summary.FormationByHomeAway.TryGetValue("home", out var hf) ? hf : null);
            var awayFormation = NormalizeFormation(summary.FormationByHomeAway.TryGetValue("away", out var af) ? af : null);

            if (unknownFormationIfMissing)
            {
                homeFormation ??= "Unknown";
                awayFormation ??= "Unknown";
            }

            return new MatchHistoryUpsertDto
            {
                League = NormalizeRequired(league),
                Season = season.ToString(CultureInfo.InvariantCulture),
                MatchDate = candidate.MatchDate.Date,
                IsKnockout = isKnockout,
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                HomeFormation = homeFormation,
                AwayFormation = awayFormation,
                HomeGoals = candidate.HomeGoals,
                AwayGoals = candidate.AwayGoals,
                HomeCorners = homeCorners,
                AwayCorners = awayCorners,
                HomeShots = homeShots,
                AwayShots = awayShots,
                HomeShotsOnGoal = homeShotsOnGoal,
                AwayShotsOnGoal = awayShotsOnGoal,
                HomePossession = homePossession,
                AwayPossession = awayPossession,
                SourceMatchId = NormalizeNullable(candidate.EventId),
                HomeTeamGender = "M",
                AwayTeamGender = "M"
            };
        }

        private static string? GetStatValue(Dictionary<string, string> stats, string statName)
        {
            if (stats.TryGetValue(statName, out var value))
                return value;

            return null;
        }

        private static int? ParseNullableInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Replace("%", "").Trim();
            if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return null;

            return result;
        }

        private static decimal? ParseNullablePossession(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Replace("%", "").Replace(",", ".").Trim();
            if (!decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
                return null;

            return Math.Round(result, 2, MidpointRounding.AwayFromZero);
        }

        private static string? ValidateHistoryRow(MatchHistoryUpsertDto row)
        {
            if (row.HomeTeam.Equals(row.AwayTeam, StringComparison.OrdinalIgnoreCase))
                return "HomeTeam y AwayTeam no pueden ser iguales.";

            if (row.HomePossession.HasValue && (row.HomePossession < 0 || row.HomePossession > 100))
                return "HomePossession fuera de rango.";

            if (row.AwayPossession.HasValue && (row.AwayPossession < 0 || row.AwayPossession > 100))
                return "AwayPossession fuera de rango.";

            return null;
        }

        private static EspnLeaguePreset? ResolveLeaguePreset(string league)
        {
            return LeaguePresets.TryGetValue(league.Trim(), out var preset) ? preset : null;
        }

        private static bool TryParseEspnDate(string? raw, out DateTimeOffset parsed)
        {
            return DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed);
        }

        private static string? GetStringOrNull(JsonElement element, string propName)
        {
            if (!element.TryGetProperty(propName, out var prop))
                return null;

            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static string? GetNestedStringOrNull(JsonElement element, string parent, string child)
        {
            if (!element.TryGetProperty(parent, out var parentElement))
                return null;

            return GetStringOrNull(parentElement, child);
        }

        private static bool GetBoolOrDefault(JsonElement element, string propName)
        {
            if (!element.TryGetProperty(propName, out var prop))
                return false;

            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;

            if (prop.ValueKind == JsonValueKind.String
                && bool.TryParse(prop.GetString(), out var boolValue))
            {
                return boolValue;
            }

            return false;
        }

        private static int? ParseIntOrNull(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static string NormalizeRequired(string? value)
        {
            var normalized = NormalizeNullable(value);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new Exception("String requerido vacío.");

            return normalized;
        }

        private static string? NormalizeNullable(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = Regex.Replace(value, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string? StripHtml(string? value)
        {
            var withoutTags = Regex.Replace(value ?? string.Empty, "<.*?>", " ");
            return NormalizeNullable(System.Net.WebUtility.HtmlDecode(withoutTags));
        }

        private static string? NormalizeFormation(string? value)
        {
            var normalized = NormalizeNullable(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            return Regex.IsMatch(normalized, @"^\d(?:-\d){2,4}$")
                ? normalized
                : null;
        }

        private static string NormalizeTeamKey(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var chars = normalized
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .Select(c => char.ToLowerInvariant(c))
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                .ToArray();

            return Regex.Replace(new string(chars), @"\s+", " ").Trim();
        }

        private static string Truncate(string value, int maxChars)
        {
            if (value.Length <= maxChars)
                return value;

            return value.Substring(0, maxChars) + "...";
        }

        private static bool IsWomenLeague(string? leagueKey, string? leagueName)
        {
            var normalizedKey = NormalizeNullable(leagueKey)?.ToLowerInvariant() ?? string.Empty;
            if (normalizedKey.Contains(".w.")
                || normalizedKey.EndsWith(".w", StringComparison.Ordinal)
                || normalizedKey.Contains("women")
                || normalizedKey.Contains("female")
                || normalizedKey.Contains("fifa.w")
                || normalizedKey.Contains("uefa.w")
                || normalizedKey.Contains("conmebol.w")
                || normalizedKey.Contains("nwsl")
                || normalizedKey.Contains("nsl"))
            {
                return true;
            }

            var normalizedName = NormalizeNullable(leagueName)?.ToLowerInvariant() ?? string.Empty;
            return normalizedName.Contains("women")
                || normalizedName.Contains("female")
                || normalizedName.Contains("femen")
                || normalizedName.Contains("femin")
                || normalizedName.Contains("liga f")
                || normalizedName.Contains("première ligue")
                || normalizedName.Contains("premiere ligue")
                || normalizedName.Contains("nwsl")
                || normalizedName.Contains("northern super league");
        }

        private static TimeZoneInfo ResolveChileTimeZone()
        {
            var candidates = new[]
            {
                "America/Santiago",
                "Pacific SA Standard Time"
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(candidate);
                }
                catch
                {
                    // Try next.
                }
            }

            return TimeZoneInfo.Utc;
        }

        private sealed class EspnLeaguePreset
        {
            public EspnLeaguePreset(string leagueKey, string dbLeague, bool defaultIsKnockout)
            {
                LeagueKey = leagueKey;
                DbLeague = dbLeague;
                DefaultIsKnockout = defaultIsKnockout;
            }

            public string LeagueKey { get; }
            public string DbLeague { get; }
            public bool DefaultIsKnockout { get; }
        }

        private sealed class EspnScoreboardDay
        {
            public string? LeagueName { get; set; }
            public List<EspnEventCandidate> Events { get; } = new();
        }

        private sealed class EspnSummaryDto
        {
            public Dictionary<string, string?> FormationByHomeAway { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> TeamNameByHomeAway { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, string>> StatsByTeamKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class EspnEventCandidate
        {
            public string EventId { get; set; } = "";
            public DateTime MatchDate { get; set; }
            public DateTimeOffset MatchDateUtc { get; set; }
            public string HomeTeam { get; set; } = "";
            public string AwayTeam { get; set; } = "";
            public int? HomeGoals { get; set; }
            public int? AwayGoals { get; set; }
            public bool Completed { get; set; }
            public string? StatusDescription { get; set; }
        }

        private sealed class EspnDateRangeSegment
        {
            public int Year { get; set; }
            public DateTime From { get; set; }
            public DateTime To { get; set; }
        }

        private sealed class EspnActiveLeagueInfo
        {
            public string LeagueKey { get; set; } = "";
            public string? LeagueName { get; set; }
        }

        public sealed class EspnLeaguePresetResponse
        {
            public string LeagueKey { get; set; } = "";
            public string DbLeague { get; set; } = "";
            public bool DefaultIsKnockout { get; set; }
        }

        public sealed class EspnHistoryProcessResult
        {
            public int Index { get; set; }
            public string EventId { get; set; } = "";
            public DateTime MatchDate { get; set; }
            public string? HomeTeam { get; set; }
            public string? AwayTeam { get; set; }
            public string Status { get; set; } = "";
            public long? InsertedId { get; set; }
            public bool DuplicateDetected { get; set; }
            public string? Error { get; set; }
            public string? Detail { get; set; }
            public MatchHistoryUpsertDto? Match { get; set; }
        }

        public sealed class EspnHistoryBatchResponse
        {
            public string? Message { get; set; }
            public string LeagueKey { get; set; } = "";
            public string League { get; set; } = "";
            public string Season { get; set; } = "";
            public DateTime DateFrom { get; set; }
            public DateTime DateTo { get; set; }
            public int TotalDiscovered { get; set; }
            public int TotalProcessed { get; set; }
            public int Inserted { get; set; }
            public int Updated { get; set; }
            public int Duplicates { get; set; }
            public int Skipped { get; set; }
            public int Errors { get; set; }
            public int DryRuns { get; set; }
            public List<string> ScoreboardErrors { get; set; } = new();
            public List<EspnHistoryDailySummary> DailyBreakdown { get; set; } = new();
            public List<EspnHistoryProcessResult> Results { get; set; } = new();
        }

        public sealed class EspnHistoryDailySummary
        {
            public DateTime Date { get; set; }
            public int Discovered { get; set; }
            public int Processed { get; set; }
            public int Inserted { get; set; }
            public int Updated { get; set; }
            public int Duplicates { get; set; }
            public int Skipped { get; set; }
            public int Errors { get; set; }
            public int DryRuns { get; set; }
        }

        public sealed class EspnMultiLeagueBatchResponse
        {
            public string? Message { get; set; }
            public DateTime DateFrom { get; set; }
            public DateTime DateTo { get; set; }
            public int TotalLeagues { get; set; }
            public int TotalLeagueRuns { get; set; }
            public int TotalDiscovered { get; set; }
            public int TotalProcessed { get; set; }
            public int Inserted { get; set; }
            public int Updated { get; set; }
            public int Duplicates { get; set; }
            public int Skipped { get; set; }
            public int Errors { get; set; }
            public int DryRuns { get; set; }
            public List<string> ScoreboardErrors { get; set; } = new();
            public List<EspnHistoryDailySummary> DailyBreakdown { get; set; } = new();
            public List<EspnHistoryBatchResponse> PerLeague { get; set; } = new();
        }
    }
}
