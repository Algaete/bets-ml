using CornersMLData.Models;
using CornersMLData.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using CornersMLData.Data;

namespace CornersMLData.Controllers
{
    /// <summary>
    /// Endpoints para descubrir y opcionalmente persistir cuotas de partidos proximos desde Betano.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BetanoOddsScrappingController : ControllerBase
    {
        private readonly BetanoUpcomingOddsScraper _betanoUpcomingOddsScraper;
        private readonly BetanoUpcomingOddsRepository _betanoUpcomingOddsRepository;
        private readonly ILogger<BetanoOddsScrappingController> _logger;

        public BetanoOddsScrappingController(
            BetanoUpcomingOddsScraper betanoUpcomingOddsScraper,
            BetanoUpcomingOddsRepository betanoUpcomingOddsRepository,
            ILogger<BetanoOddsScrappingController> logger)
        {
            _betanoUpcomingOddsScraper = betanoUpcomingOddsScraper;
            _betanoUpcomingOddsRepository = betanoUpcomingOddsRepository;
            _logger = logger;
        }

        /// <summary>
        /// Scrapea partidos de futbol proximos desde Betano y extrae corners, goles, tiros, tiros al arco y tarjetas cuando existan.
        /// </summary>
        /// <remarks>
        /// Recibe parametros por <c>query string</c>. Si <c>persist=true</c>, el endpoint inserta o actualiza las cuotas
        /// encontradas en la tabla operativa asociada a Betano.
        /// </remarks>
        /// <param name="take">Cantidad maxima de partidos a procesar. El scraper limita internamente el rango valido.</param>
        /// <param name="persist">Si es <c>true</c>, guarda las cuotas encontradas en la base de datos.</param>
        /// <param name="cancellationToken">Token para cancelar la operacion HTTP.</param>
        [HttpPost("scrape-upcoming-football")]
        [ProducesResponseType(typeof(BetanoUpcomingFootballOddsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ScrapeUpcomingFootball(
            [FromQuery] int take = 10,
            [FromQuery] bool persist = true,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _betanoUpcomingOddsScraper.ScrapeUpcomingFootballAsync(take, cancellationToken);

                if (persist && response.Matches.Count > 0)
                {
                    await _betanoUpcomingOddsRepository.EnsureDatabaseObjectsAsync(cancellationToken);
                    var persistence = await _betanoUpcomingOddsRepository.SincronizarAsync(
                        response.Matches,
                        response.ScrapedAtUtc,
                        cancellationToken);
                    ApplyPersistenceResult(response, persistence);
                    response.PersistedToDatabase = true;
                    response.StoredProcedureName = BetanoUpcomingOddsRepository.UpsertStoredProcedureName;
                }

                return Ok(response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Betano upcoming odds scraping was cancelled after processing request take={Take}.", take);
                return StatusCode(StatusCodes.Status408RequestTimeout, new ProblemDetails
                {
                    Title = "Betano scraping timed out",
                    Detail = "The Betano request was cancelled before scraping and persistence completed.",
                    Status = StatusCodes.Status408RequestTimeout
                });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Betano upcoming odds scraping failed for take={Take}, persist={Persist}.", take, persist);
                return Problem(
                    title: "Betano scraping failed",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Scrapea una URL especifica de Betano, util cuando el listado general no descubre el partido.
        /// </summary>
        [HttpPost("scrape-match")]
        [ProducesResponseType(typeof(BetanoUpcomingFootballOddsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ScrapeMatch(
            [FromQuery] string sourceUrl,
            [FromQuery] bool persist = true,
            CancellationToken cancellationToken = default)
        {
            var response = await _betanoUpcomingOddsScraper.ScrapeMatchByUrlAsync(sourceUrl, cancellationToken);

            if (persist && response.Matches.Count > 0)
            {
                await _betanoUpcomingOddsRepository.EnsureDatabaseObjectsAsync(cancellationToken);
                var persistence = await _betanoUpcomingOddsRepository.SincronizarAsync(
                    response.Matches,
                    response.ScrapedAtUtc,
                    cancellationToken);
                ApplyPersistenceResult(response, persistence);
                response.PersistedToDatabase = true;
                response.StoredProcedureName = BetanoUpcomingOddsRepository.UpsertStoredProcedureName;
            }

            return Ok(response);
        }

        private static void ApplyPersistenceResult(
            BetanoUpcomingFootballOddsResponse response,
            BetanoOddsPersistenceResult persistence)
        {
            response.PersistedCount = persistence.PersistedRows;
            response.PersistenceSkippedMatches = persistence.SkippedMatches;
            response.PersistenceFailedMatches = persistence.FailedMatches;
            response.PersistenceErrors = persistence.Errors.ToList();

            if (persistence.FailedMatches > 0)
            {
                response.Message = $"{response.Message} Persistencia parcial: " +
                    $"{persistence.FailedMatches} partido(s) fallaron después de reintentar; " +
                    $"{persistence.PersistedRows} línea(s) sí quedaron guardadas.";
            }
        }
    }
}
