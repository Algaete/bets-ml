using CornersMLData.Data;
using CornersMLData.Models;
using CornersMLData.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Controllers
{
    /// <summary>
    /// Endpoints para descubrir y opcionalmente persistir cuotas de partidos proximos desde Pinnacle.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PinnacleOddsScrappingController : ControllerBase
    {
        private readonly PinnacleUpcomingOddsScraper _pinnacleUpcomingOddsScraper;
        private readonly PinnacleUpcomingOddsRepository _pinnacleUpcomingOddsRepository;

        public PinnacleOddsScrappingController(
            PinnacleUpcomingOddsScraper pinnacleUpcomingOddsScraper,
            PinnacleUpcomingOddsRepository pinnacleUpcomingOddsRepository)
        {
            _pinnacleUpcomingOddsScraper = pinnacleUpcomingOddsScraper;
            _pinnacleUpcomingOddsRepository = pinnacleUpcomingOddsRepository;
        }

        /// <summary>
        /// Scrapea partidos de futbol proximos desde Pinnacle y extrae corners, goles, tiros, tiros al arco y tarjetas cuando existan.
        /// </summary>
        /// <remarks>
        /// Recibe parametros por <c>query string</c>. Si <c>persist=true</c>, el endpoint guarda las cuotas nuevas en base
        /// de datos para que luego puedan ser evaluadas por el bot.
        /// </remarks>
        /// <param name="take">Cantidad maxima de partidos a procesar. El scraper recorre la vista de highlights y entra a cada partido.</param>
        /// <param name="persist">Si es <c>true</c>, inserta o actualiza las cuotas encontradas en la base de datos.</param>
        /// <param name="cancellationToken">Token para cancelar la operacion HTTP.</param>
        [HttpPost("scrape-upcoming-football")]
        [ProducesResponseType(typeof(PinnacleUpcomingFootballOddsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ScrapeUpcomingFootball(
            [FromQuery] int take = 10,
            [FromQuery] bool persist = false,
            CancellationToken cancellationToken = default)
        {
            var response = await _pinnacleUpcomingOddsScraper.ScrapeUpcomingFootballAsync(take, cancellationToken);

            if (persist && response.Matches.Count > 0)
            {
                await _pinnacleUpcomingOddsRepository.EnsureDatabaseObjectsAsync(cancellationToken);
                response.PersistedCount = await _pinnacleUpcomingOddsRepository.SincronizarAsync(
                    response.Matches,
                    response.ScrapedAtUtc,
                    cancellationToken);
                response.PersistedToDatabase = true;
            }

            return Ok(response);
        }
    }
}
