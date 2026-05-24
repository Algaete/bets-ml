using System.ComponentModel.DataAnnotations;

namespace CornersPrediction.Web.Models.MatchHistory;

public sealed class CreateMatchHistoryViewModel
{
    [Required]
    [Display(Name = "League")]
    public string League { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Season")]
    public string Season { get; set; } = "2025-2026";

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Match date")]
    public DateTime MatchDate { get; set; } = DateTime.Today;

    [Display(Name = "Knockout match")]
    public bool IsKnockout { get; set; }

    [Required]
    [Display(Name = "Home team")]
    public string HomeTeam { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Away team")]
    public string AwayTeam { get; set; } = string.Empty;

    [Display(Name = "Home formation")]
    public string? HomeFormation { get; set; }

    [Display(Name = "Away formation")]
    public string? AwayFormation { get; set; }

    [Range(0, 50)]
    [Display(Name = "Home corners")]
    public int HomeCorners { get; set; }

    [Range(0, 50)]
    [Display(Name = "Away corners")]
    public int AwayCorners { get; set; }

    [Range(0, 30)]
    [Display(Name = "Home goals")]
    public int HomeGoals { get; set; }

    [Range(0, 30)]
    [Display(Name = "Away goals")]
    public int AwayGoals { get; set; }

    [Range(0, 80)]
    [Display(Name = "Home shots")]
    public int HomeShots { get; set; }

    [Range(0, 80)]
    [Display(Name = "Away shots")]
    public int AwayShots { get; set; }

    [Range(0, 80)]
    [Display(Name = "Home shots on goal")]
    public int HomeShotsOnGoal { get; set; }

    [Range(0, 80)]
    [Display(Name = "Away shots on goal")]
    public int AwayShotsOnGoal { get; set; }

    [Range(0, 100)]
    [Display(Name = "Home possession")]
    public double HomePossession { get; set; } = 50;

    [Range(0, 100)]
    [Display(Name = "Away possession")]
    public double AwayPossession { get; set; } = 50;
}
