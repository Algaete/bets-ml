namespace CornersPrediction.Application.UpcomingMatches;

public sealed class UpcomingMatchDto
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
}
