using System;
using System.Collections.Generic;

namespace CornersMLData.Models
{
    public sealed class PinnacleUpcomingFootballOddsResponse
    {
        public string Message { get; set; } = "";
        public DateTime ScrapedAtUtc { get; set; }
        public int TotalDiscovered { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalWithCornersTotal { get; set; }
        public int TotalWithCornersHomeTeam { get; set; }
        public int TotalWithCornersAwayTeam { get; set; }
        public bool PersistedToDatabase { get; set; }
        public int PersistedCount { get; set; }
        public List<PinnacleUpcomingFootballOddsMatch> Matches { get; set; } = new();
    }

    public sealed class PinnacleUpcomingFootballOddsMatch
    {
        public string Source { get; set; } = "Pinnacle";
        public string? SourceMatchId { get; set; }
        public string SourceUrl { get; set; } = "";
        public DateTime? MatchDateLocal { get; set; }
        public string League { get; set; } = "";
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public string StandardizedLeague { get; set; } = "";
        public string StandardizedHomeTeam { get; set; } = "";
        public string StandardizedAwayTeam { get; set; } = "";
        public string HomeTeamGender { get; set; } = "M";
        public string AwayTeamGender { get; set; } = "M";
        public BetanoMarketOddsDto? CornersTotal { get; set; }
        public BetanoMarketOddsDto? CornersHomeTeam { get; set; }
        public BetanoMarketOddsDto? CornersAwayTeam { get; set; }
        public List<string> Notes { get; set; } = new();
    }
}
