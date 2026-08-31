using System.Globalization;
using System.Text.Json;
using CornersPrediction.Application.FootballIntelligence;
using CornersPredictionApi.ApiFootball;

namespace CornersPredictionApi.FootballIntelligence;

public sealed class ApiFootballStructuredDataProvider : IStructuredFootballDataProvider
{
    private readonly ApiFootballClient _client;

    public ApiFootballStructuredDataProvider(ApiFootballClient client)
    {
        _client = client;
    }

    public async Task<IntelligenceFixtureDto?> GetFixtureAsync(
        long fixtureId,
        CancellationToken cancellationToken)
    {
        var root = await _client.GetFixtureAsync(fixtureId, cancellationToken);
        var item = Responses(root).FirstOrDefault();
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var fixture = item.GetProperty("fixture");
        var league = item.GetProperty("league");
        var teams = item.GetProperty("teams");
        var home = teams.GetProperty("home");
        var away = teams.GetProperty("away");
        var dateText = ReadString(fixture, "date");
        if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var kickoff))
            return null;

        return new IntelligenceFixtureDto(
            ReadLong(fixture, "id") ?? fixtureId,
            kickoff.UtcDateTime,
            ReadNestedString(fixture, "status", "short") ?? "NS",
            ReadString(league, "name") ?? string.Empty,
            ReadInt(league, "id") ?? 0,
            ReadInt(league, "season") ?? 0,
            new IntelligenceTeamDto(
                ReadInt(home, "id") ?? 0,
                ReadString(home, "name") ?? string.Empty,
                ReadString(league, "country")),
            new IntelligenceTeamDto(
                ReadInt(away, "id") ?? 0,
                ReadString(away, "name") ?? string.Empty,
                ReadString(league, "country")));
    }

    public async Task<IReadOnlyCollection<InjuryDto>> GetFixtureInjuriesAsync(
        long fixtureId,
        CancellationToken cancellationToken)
    {
        var root = await _client.GetFixtureInjuriesAsync(fixtureId, cancellationToken);
        var rows = new List<InjuryDto>();
        foreach (var item in Responses(root))
        {
            if (!item.TryGetProperty("player", out var player)
                || !item.TryGetProperty("team", out var team))
                continue;
            var playerId = ReadInt(player, "id");
            var teamId = ReadInt(team, "id");
            if (!playerId.HasValue || !teamId.HasValue)
                continue;
            rows.Add(new InjuryDto(
                fixtureId,
                teamId.Value,
                playerId.Value,
                ReadString(player, "name") ?? string.Empty,
                ReadString(player, "type"),
                ReadString(player, "reason")));
        }
        return rows;
    }

    public async Task<IReadOnlyCollection<SquadPlayerDto>> GetSquadAsync(
        int teamId,
        CancellationToken cancellationToken)
    {
        var root = await _client.GetSquadAsync(teamId, cancellationToken);
        var rows = new List<SquadPlayerDto>();
        foreach (var response in Responses(root))
        {
            if (!response.TryGetProperty("players", out var players)
                || players.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var player in players.EnumerateArray())
            {
                var playerId = ReadInt(player, "id");
                if (!playerId.HasValue)
                    continue;
                rows.Add(new SquadPlayerDto(
                    teamId,
                    playerId.Value,
                    ReadString(player, "name") ?? string.Empty,
                    ReadString(player, "position"),
                    ReadInt(player, "age"),
                    ReadString(player, "photo")));
            }
        }
        return rows;
    }

    public async Task<IReadOnlyCollection<FixtureLineupDto>> GetFixtureLineupsAsync(
        long fixtureId,
        CancellationToken cancellationToken)
    {
        var root = await _client.GetFixtureLineupsAsync(fixtureId, cancellationToken);
        var rows = new List<FixtureLineupDto>();
        foreach (var response in Responses(root))
        {
            if (!response.TryGetProperty("team", out var team))
                continue;
            var teamId = ReadInt(team, "id");
            if (!teamId.HasValue)
                continue;
            var players = new List<LineupPlayerDto>();
            AddPlayers(response, "startXI", true, players);
            AddPlayers(response, "substitutes", false, players);
            rows.Add(new FixtureLineupDto(
                fixtureId,
                teamId.Value,
                ReadString(response, "formation"),
                players));
        }
        return rows;
    }

    private static void AddPlayers(
        JsonElement response,
        string property,
        bool starter,
        ICollection<LineupPlayerDto> target)
    {
        if (!response.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
            return;
        foreach (var entry in values.EnumerateArray())
        {
            var player = entry.TryGetProperty("player", out var nested) ? nested : entry;
            var id = ReadInt(player, "id");
            if (!id.HasValue)
                continue;
            target.Add(new LineupPlayerDto(
                id.Value,
                ReadString(player, "name") ?? string.Empty,
                ReadString(player, "pos"),
                starter,
                ReadGridPosition(ReadString(player, "grid"))));
        }
    }

    private static int? ReadGridPosition(string? grid)
    {
        if (string.IsNullOrWhiteSpace(grid))
            return null;
        var first = grid.Split(':', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static IEnumerable<JsonElement> Responses(JsonElement root) =>
        root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Array
            ? response.EnumerateArray()
            : [];

    private static string? ReadNestedString(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out var nested) ? ReadString(nested, property) : null;

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static long? ReadLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
