using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Domain.FootballIntelligence;

namespace CornersPredictionApi.FootballIntelligence;

public sealed class FootballEntityResolver : IEntityResolver
{
    private readonly IStructuredFootballDataProvider _footballData;
    private readonly ITeamAliasRepository _teamAliases;
    private readonly IPlayerAliasRepository _playerAliases;

    public FootballEntityResolver(
        IStructuredFootballDataProvider footballData,
        ITeamAliasRepository teamAliases,
        IPlayerAliasRepository playerAliases)
    {
        _footballData = footballData;
        _teamAliases = teamAliases;
        _playerAliases = playerAliases;
    }

    public async Task<EntityResolutionResult> ResolveAsync(
        long fixtureId,
        string teamName,
        string? playerName,
        CancellationToken cancellationToken)
    {
        var fixture = await _footballData.GetFixtureAsync(fixtureId, cancellationToken);
        if (fixture is null)
            return NotFound();

        var normalizedTeam = Normalize(teamName);
        var fixtureTeams = new[] { fixture.Home, fixture.Away };
        var teamMatches = fixtureTeams.Where(team => Normalize(team.Name) == normalizedTeam).ToArray();
        EntityResolutionStatus teamStatus = EntityResolutionStatus.ResolvedExact;
        if (teamMatches.Length == 0)
        {
            var aliasIds = await _teamAliases.FindTeamIdsAsync(normalizedTeam, cancellationToken);
            teamMatches = fixtureTeams.Where(team => aliasIds.Contains(team.TeamId)).ToArray();
            teamStatus = EntityResolutionStatus.ResolvedAlias;
        }
        if (teamMatches.Length == 0)
        {
            var fuzzy = fixtureTeams
                .Select(team => new { Team = team, Distance = NormalizedDistance(normalizedTeam, Normalize(team.Name)) })
                .Where(value => value.Distance <= 0.20d)
                .OrderBy(value => value.Distance)
                .ToArray();
            if (fuzzy.Length > 1 && Math.Abs(fuzzy[0].Distance - fuzzy[1].Distance) < 0.03d)
                return Ambiguous();
            if (fuzzy.Length == 0)
                return NotFound();
            teamMatches = [fuzzy[0].Team];
            teamStatus = EntityResolutionStatus.ResolvedFuzzy;
        }
        if (teamMatches.Length != 1)
            return Ambiguous();

        var teamMatch = teamMatches[0];
        if (string.IsNullOrWhiteSpace(playerName))
            return new EntityResolutionResult(teamStatus, teamMatch.TeamId, null, teamStatus == EntityResolutionStatus.ResolvedFuzzy ? 0.80m : 1m, teamMatch.Name);

        var squad = await _footballData.GetSquadAsync(teamMatch.TeamId, cancellationToken);
        var normalizedPlayer = Normalize(playerName);
        var playerMatches = squad.Where(player => Normalize(player.Name) == normalizedPlayer).ToArray();
        var playerStatus = EntityResolutionStatus.ResolvedExact;
        if (playerMatches.Length == 0)
        {
            var aliasIds = await _playerAliases.FindPlayerIdsAsync(normalizedPlayer, teamMatch.TeamId, cancellationToken);
            playerMatches = squad.Where(player => aliasIds.Contains(player.PlayerId)).ToArray();
            playerStatus = EntityResolutionStatus.ResolvedAlias;
        }
        if (playerMatches.Length == 0)
        {
            var fuzzy = squad
                .Select(player => new { Player = player, Distance = NormalizedDistance(normalizedPlayer, Normalize(player.Name)) })
                .Where(value => value.Distance <= 0.18d)
                .OrderBy(value => value.Distance)
                .ThenBy(value => value.Player.PlayerId)
                .ToArray();
            if (fuzzy.Length > 1 && Math.Abs(fuzzy[0].Distance - fuzzy[1].Distance) < 0.02d)
                return Ambiguous(teamMatch.TeamId);
            if (fuzzy.Length == 0)
                return NotFound(teamMatch.TeamId);
            playerMatches = [fuzzy[0].Player];
            playerStatus = EntityResolutionStatus.ResolvedFuzzy;
        }
        if (playerMatches.Length != 1)
            return Ambiguous(teamMatch.TeamId);

        var playerMatch = playerMatches[0];
        return new EntityResolutionResult(
            playerStatus,
            teamMatch.TeamId,
            playerMatch.PlayerId,
            playerStatus == EntityResolutionStatus.ResolvedFuzzy ? 0.80m : 1m,
            playerMatch.Name);
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var withoutMarks = string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
        return Regex.Replace(withoutMarks.ToUpperInvariant(), @"[^A-Z0-9]+", " ").Trim();
    }

    private static double NormalizedDistance(string left, string right)
    {
        if (left == right)
            return 0d;
        var rows = left.Length + 1;
        var columns = right.Length + 1;
        var matrix = new int[rows, columns];
        for (var row = 0; row < rows; row++) matrix[row, 0] = row;
        for (var column = 0; column < columns; column++) matrix[0, column] = column;
        for (var row = 1; row < rows; row++)
        for (var column = 1; column < columns; column++)
        {
            var cost = left[row - 1] == right[column - 1] ? 0 : 1;
            matrix[row, column] = Math.Min(
                Math.Min(matrix[row - 1, column] + 1, matrix[row, column - 1] + 1),
                matrix[row - 1, column - 1] + cost);
        }
        return matrix[rows - 1, columns - 1] / (double)Math.Max(1, Math.Max(left.Length, right.Length));
    }

    private static EntityResolutionResult NotFound(int? teamId = null) =>
        new(EntityResolutionStatus.NotFound, teamId, null, 0m, null);

    private static EntityResolutionResult Ambiguous(int? teamId = null) =>
        new(EntityResolutionStatus.Ambiguous, teamId, null, 0m, null);
}
