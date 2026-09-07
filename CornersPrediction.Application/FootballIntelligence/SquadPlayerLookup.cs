namespace CornersPrediction.Application.FootballIntelligence;

public static class SquadPlayerLookup
{
    public static IReadOnlyDictionary<int, SquadPlayerDto> ForTeam(
        IEnumerable<SquadPlayerDto> squad,
        int teamId) => squad
        .Where(player => player.TeamId == teamId && player.PlayerId > 0)
        .GroupBy(player => player.PlayerId)
        .ToDictionary(group => group.Key, group =>
        {
            // Providers can repeat a player in one squad response. Keep one identity,
            // retaining useful fields from a complete row when another row is partial.
            var first = group.First();
            return first with
            {
                Name = group.Select(player => player.Name)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? first.Name,
                Position = group.Select(player => player.Position)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                Age = group.Select(player => player.Age).FirstOrDefault(value => value.HasValue),
                PhotoUrl = group.Select(player => player.PhotoUrl)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            };
        });
}
