namespace CornersPrediction.Domain.Teams;

/// <summary>
/// Team metadata returned by SQL Server for form dropdowns and future feature enrichment.
/// </summary>
public sealed class TeamBi3Info
{
    public string League { get; init; } = string.Empty;

    public string Season { get; init; } = string.Empty;

    public string Team { get; init; } = string.Empty;

    public bool IsBig3 { get; init; }

    public DateTime CreatedAt { get; init; }
}
