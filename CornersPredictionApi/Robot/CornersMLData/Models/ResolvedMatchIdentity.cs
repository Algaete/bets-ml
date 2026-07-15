namespace CornersMLData.Models
{
    public sealed class ResolvedMatchIdentity
    {
        public string StandardizedLeague { get; set; } = "";
        public string StandardizedHomeTeam { get; set; } = "";
        public string StandardizedAwayTeam { get; set; } = "";
        public string PreferredHomeTeam { get; set; } = "";
        public string PreferredAwayTeam { get; set; } = "";
    }
}
