using System;

namespace CornersMLData.Models
{
    public sealed class MatchHistoryUpsertDto
    {
        public string League { get; set; } = "";
        public string Season { get; set; } = "";
        public DateTime MatchDate { get; set; }
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public string? HomeFormation { get; set; }
        public string? AwayFormation { get; set; }
        public int? HomeGoals { get; set; }
        public int? AwayGoals { get; set; }
        public int? HomeCorners { get; set; }
        public int? AwayCorners { get; set; }
        public int? HomeShots { get; set; }
        public int? AwayShots { get; set; }
        public int? HomeShotsOnGoal { get; set; }
        public int? AwayShotsOnGoal { get; set; }
        public decimal? HomePossession { get; set; }
        public decimal? AwayPossession { get; set; }
        public bool IsKnockout { get; set; }
        public string? SourceMatchId { get; set; }
        public string HomeTeamGender { get; set; } = "M";
        public string AwayTeamGender { get; set; } = "M";
        public int? TotalTeams { get; set; }
        public int? HomeTeamPosition { get; set; }
        public int? AwayTeamPosition { get; set; }
    }
}
