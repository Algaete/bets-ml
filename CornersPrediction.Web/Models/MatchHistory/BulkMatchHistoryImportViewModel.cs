using System.ComponentModel.DataAnnotations;

namespace CornersPrediction.Web.Models.MatchHistory;

public sealed class BulkMatchHistoryImportViewModel
{
    [Required]
    [Display(Name = "League")]
    public string League { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Season")]
    public string Season { get; set; } = "2025-2026";

    [Required]
    [Display(Name = "Team")]
    public string FocusTeam { get; set; } = string.Empty;

    [Display(Name = "Team gender")]
    public string TeamGender { get; set; } = "M";

    [Display(Name = "Knockout matches")]
    public bool IsKnockout { get; set; }

    [Required]
    [Display(Name = "Matches JSON")]
    public string MatchesJson { get; set; } = string.Empty;
}

