namespace CornersPrediction.Web.Models.UpcomingMatches;

public sealed class UpcomingMatchesIndexViewModel
{
    public string? Genero { get; init; }

    public string? Liga { get; init; }

    public IReadOnlyList<UpcomingMatchViewModel> Matches { get; init; } = Array.Empty<UpcomingMatchViewModel>();
}

public sealed class UpcomingMatchViewModel
{
    public int PartidoID { get; init; }

    public DateTime FechaPartido { get; init; }

    public string EquipoLocal { get; init; } = string.Empty;

    public string EquipoVisita { get; init; } = string.Empty;

    public string Liga { get; init; } = string.Empty;

    public string Genero { get; init; } = string.Empty;

    public bool EsKnockout { get; init; }

    public DateTime FechaRegistro { get; init; }

    public DateTime? FechaActualizacion { get; init; }

    public string TeamGenderForPrediction
    {
        get
        {
            var normalized = (Genero ?? string.Empty).Trim();
            return normalized.Equals("F", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Femenino", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Female", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Women", StringComparison.OrdinalIgnoreCase)
                    ? "F"
                    : "M";
        }
    }
}
