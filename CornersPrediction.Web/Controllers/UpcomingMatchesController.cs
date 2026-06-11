using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.UpcomingMatches;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize]
public sealed class UpcomingMatchesController : Controller
{
    private readonly UpcomingMatchesApiClient _upcomingMatchesApiClient;
    private readonly ILogger<UpcomingMatchesController> _logger;

    public UpcomingMatchesController(
        UpcomingMatchesApiClient upcomingMatchesApiClient,
        ILogger<UpcomingMatchesController> logger)
    {
        _upcomingMatchesApiClient = upcomingMatchesApiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? genero,
        string? liga,
        CancellationToken cancellationToken)
    {
        var normalizedGenero = NormalizeFilter(genero);
        var normalizedLiga = NormalizeFilter(liga);
        IReadOnlyList<UpcomingMatchViewModel> matches = Array.Empty<UpcomingMatchViewModel>();

        try
        {
            // Load the weekly slate once; the page applies gender/league/team filters in the browser.
            var items = await _upcomingMatchesApiClient.GetNextWeekAsync(
                null,
                null,
                cancellationToken);

            matches = items.Select(Map).ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load upcoming matches");
            ModelState.AddModelError(string.Empty, "No se pudieron cargar los proximos partidos.");
        }

        return View(new UpcomingMatchesIndexViewModel
        {
            Genero = normalizedGenero,
            Liga = normalizedLiga,
            Matches = matches
        });
    }

    private static UpcomingMatchViewModel Map(UpcomingMatchDto item)
    {
        return new UpcomingMatchViewModel
        {
            PartidoID = item.PartidoID,
            FechaPartido = item.FechaPartido,
            EquipoLocal = item.EquipoLocal,
            EquipoVisita = item.EquipoVisita,
            Liga = item.Liga,
            Genero = item.Genero,
            EsKnockout = item.EsKnockout,
            FechaRegistro = item.FechaRegistro,
            FechaActualizacion = item.FechaActualizacion
        };
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
