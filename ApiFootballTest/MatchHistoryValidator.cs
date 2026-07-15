namespace ApiFootballTest;

internal static class MatchHistoryValidator
{
    public static ValidationResult Validate(MatchHistoryCandidate candidate)
    {
        var reasons = new List<string>();

        Require(candidate.HomeGoals, "homeGoals", reasons);
        Require(candidate.AwayGoals, "awayGoals", reasons);
        Require(candidate.HomeCorners, "homeCorners", reasons);
        Require(candidate.AwayCorners, "awayCorners", reasons);
        Require(candidate.HomeShots, "homeShots", reasons);
        Require(candidate.AwayShots, "awayShots", reasons);
        Require(candidate.HomeShotsOnGoal, "homeShotsOnGoal", reasons);
        Require(candidate.AwayShotsOnGoal, "awayShotsOnGoal", reasons);
        Require(candidate.HomePossession, "homePossession", reasons);
        Require(candidate.AwayPossession, "awayPossession", reasons);

        if (candidate.HomeShotsOnGoal.HasValue && candidate.HomeShots.HasValue &&
            candidate.HomeShotsOnGoal > candidate.HomeShots)
        {
            reasons.Add("homeShotsOnGoal is greater than homeShots");
        }

        if (candidate.AwayShotsOnGoal.HasValue && candidate.AwayShots.HasValue &&
            candidate.AwayShotsOnGoal > candidate.AwayShots)
        {
            reasons.Add("awayShotsOnGoal is greater than awayShots");
        }

        ValidatePossession(candidate.HomePossession, "homePossession", reasons);
        ValidatePossession(candidate.AwayPossession, "awayPossession", reasons);

        if (candidate.HomePossession.HasValue && candidate.AwayPossession.HasValue)
        {
            var total = candidate.HomePossession.Value + candidate.AwayPossession.Value;
            if (total is < 98 or > 102)
            {
                reasons.Add($"possession sum is {total:0.##}, expected 98-102");
            }
        }

        return new ValidationResult(reasons.Count == 0, reasons);
    }

    private static void Require(int? value, string field, ICollection<string> reasons)
    {
        if (!value.HasValue)
        {
            reasons.Add($"{field} is missing");
        }
        else if (value < 0)
        {
            reasons.Add($"{field} is negative");
        }
    }

    private static void Require(double? value, string field, ICollection<string> reasons)
    {
        if (!value.HasValue)
        {
            reasons.Add($"{field} is missing");
        }
        else if (value < 0)
        {
            reasons.Add($"{field} is negative");
        }
    }

    private static void ValidatePossession(double? value, string field, ICollection<string> reasons)
    {
        if (value.HasValue && value is < 0 or > 100)
        {
            reasons.Add($"{field} is outside 0-100");
        }
    }
}
