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

        public BetanoOddsScrappingController(
            BetanoUpcomingOddsScraper betanoUpcomingOddsScraper,
            BetanoUpcomingOddsRepository betanoUpcomingOddsRepository)
        {
            _betanoUpcomingOddsScraper = betanoUpcomingOddsScraper;
            _betanoUpcomingOddsRepository = betanoUpcomingOddsRepository;
        }

        /// <summary>
        /// Scrapea partidos de futbol proximos desde Betano y extrae mercados de corners y tiros al arco cuando existan.
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
            var response = await _betanoUpcomingOddsScraper.ScrapeUpcomingFootballAsync(take, cancellationToken);

            if (persist && response.Matches.Count > 0)
            {
                await _betanoUpcomingOddsRepository.EnsureDatabaseObjectsAsync(cancellationToken);
                response.PersistedCount = await _betanoUpcomingOddsRepository.SincronizarAsync(
                    response.Matches,
                    response.ScrapedAtUtc,
                    cancellationToken);
                response.PersistedToDatabase = true;
                response.StoredProcedureName = BetanoUpcomingOddsRepository.UpsertStoredProcedureName;
            }

            return Ok(response);
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
                response.PersistedCount = await _betanoUpcomingOddsRepository.SincronizarAsync(
                    response.Matches,
                    response.ScrapedAtUtc,
                    cancellationToken);
                response.PersistedToDatabase = true;
                response.StoredProcedureName = BetanoUpcomingOddsRepository.UpsertStoredProcedureName;
            }

            return Ok(response);
        }
    }
}
