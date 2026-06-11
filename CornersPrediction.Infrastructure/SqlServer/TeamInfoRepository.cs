using System.Data;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.Teams;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class TeamInfoRepository : ITeamInfoRepository
{
    private const string LeagueProcedureName = "sp_GetMatchHistoryLeagues";
    private const string TeamProcedureName = "sp_GetTeamsByLeague";
    private const string FormationProcedureName = "sp_GetFormationList";

    private readonly string _connectionString;

    public TeamInfoRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<string>> GetBig3LeaguesAsync(string teamGender, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = await BuildSupportedParametersAsync(
            connection,
            LeagueProcedureName,
            new Dictionary<string, object?> { ["TeamGender"] = teamGender },
            cancellationToken);
        var command = new CommandDefinition(
            LeagueProcedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var leagues = await connection.QueryAsync<string>(command);
        return leagues.ToArray();
    }

    public async Task<IReadOnlyList<TeamBi3Info>> GetBi3InfoAsync(
        string league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = await BuildSupportedParametersAsync(
            connection,
            TeamProcedureName,
            new Dictionary<string, object?>
            {
                ["League"] = league,
                ["TeamGender"] = teamGender
            },
            cancellationToken);
        var command = new CommandDefinition(
            TeamProcedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var teams = await connection.QueryAsync<TeamInfoRow>(command);
        return teams
            .Select(team => new TeamBi3Info
            {
                League = string.IsNullOrWhiteSpace(team.League) ? league : team.League,
                Season = team.Season ?? string.Empty,
                Team = string.IsNullOrWhiteSpace(team.Team) ? team.StandardizedTeam ?? string.Empty : team.Team,
                IsBig3 = team.IsBig3,
                CreatedAt = team.CreatedAt
            })
            .Where(team => !string.IsNullOrWhiteSpace(team.Team))
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> GetFormationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            FormationProcedureName,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var formations = await connection.QueryAsync<string?>(command);
        return formations
            .Where(formation => !string.IsNullOrWhiteSpace(formation))
            .Select(formation => formation!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(formation => formation)
            .ToArray();
    }

    private static async Task<DynamicParameters> BuildSupportedParametersAsync(
        SqlConnection connection,
        string procedureName,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            SELECT ParameterName = REPLACE(p.name, '@', '')
            FROM sys.parameters p
            WHERE p.object_id = OBJECT_ID(@ProcedureName);
            """,
            new { ProcedureName = procedureName },
            cancellationToken: cancellationToken);

        var supported = (await connection.QueryAsync<string>(command))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parameters = new DynamicParameters();
        foreach (var value in values)
        {
            if (supported.Contains(value.Key))
            {
                parameters.Add(value.Key, value.Value);
            }
        }

        return parameters;
    }

    private sealed class TeamInfoRow
    {
        public string? League { get; init; }
        public string? Season { get; init; }
        public string? Team { get; init; }
        public string? StandardizedTeam { get; init; }
        public bool IsBig3 { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
