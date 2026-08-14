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
        public int TotalWithGoalsTotal { get; set; }
        public int TotalWithGoalsHomeTeam { get; set; }
        public int TotalWithGoalsAwayTeam { get; set; }
        public int TotalWithShotsOnTargetTotal { get; set; }
        public int TotalWithShotsOnTargetHomeTeam { get; set; }
        public int TotalWithShotsOnTargetAwayTeam { get; set; }
        public int TotalWithShotsTotal { get; set; }
        public int TotalWithShotsHomeTeam { get; set; }
        public int TotalWithShotsAwayTeam { get; set; }
        public int TotalWithCardsTotal { get; set; }
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
        public BetanoMarketOddsDto? GoalsTotal { get; set; }
        public BetanoMarketOddsDto? GoalsHomeTeam { get; set; }
        public BetanoMarketOddsDto? GoalsAwayTeam { get; set; }
        public BetanoMarketOddsDto? ShotsOnTargetTotal { get; set; }
        public BetanoMarketOddsDto? ShotsOnTargetHomeTeam { get; set; }
        public BetanoMarketOddsDto? ShotsOnTargetAwayTeam { get; set; }
        public BetanoMarketOddsDto? ShotsTotal { get; set; }
        public BetanoMarketOddsDto? ShotsHomeTeam { get; set; }
        public BetanoMarketOddsDto? ShotsAwayTeam { get; set; }
        public BetanoMarketOddsDto? CardsTotal { get; set; }
        public List<string> Notes { get; set; } = new();
    }
}
