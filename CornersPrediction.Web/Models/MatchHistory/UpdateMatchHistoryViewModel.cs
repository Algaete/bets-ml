using System.ComponentModel.DataAnnotations;

namespace CornersPrediction.Web.Models.MatchHistory;

public sealed class UpdateMatchHistoryViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required]
    public string League { get; set; } = string.Empty;

    [Required]
    public string Season { get; set; } = string.Empty;

    [Required]
    public DateTime MatchDate { get; set; }

    public bool IsKnockout { get; set; }

    [Required]
    public string HomeTeam { get; set; } = string.Empty;

    [Required]
    public string AwayTeam { get; set; } = string.Empty;

    public string? HomeFormation { get; set; }

    public string? AwayFormation { get; set; }

    [Range(0, 50)]
    public int HomeCorners { get; set; }

    [Range(0, 50)]
    public int AwayCorners { get; set; }

    [Range(0, 30)]
    public int HomeGoals { get; set; }

    [Range(0, 30)]
    public int AwayGoals { get; set; }

    [Range(0, 80)]
    public int HomeShots { get; set; }

    [Range(0, 80)]
    public int AwayShots { get; set; }

    [Range(0, 80)]
    public int HomeShotsOnGoal { get; set; }

    [Range(0, 80)]
    public int AwayShotsOnGoal { get; set; }

    [Range(0, 100)]
    public double HomePossession { get; set; }

    [Range(0, 100)]
    public double AwayPossession { get; set; }
}
