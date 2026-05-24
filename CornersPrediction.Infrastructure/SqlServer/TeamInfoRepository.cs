using System.Data;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.Teams;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class TeamInfoRepository : ITeamInfoRepository
{
    private static readonly string[] TeamProcedureCandidates =
    [
        "sp_GetTeamBig3Info"
    ];

    private const string LeagueProcedureName = "sp_GetTeamBig3Leagues";
    private const string FormationProcedureName = "sp_GetFormationList";

    private readonly string _connectionString;

    public TeamInfoRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<string>> GetBig3LeaguesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            LeagueProcedureName,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var leagues = await connection.QueryAsync<string>(command);
        return leagues.ToArray();
    }

    public async Task<IReadOnlyList<TeamBi3Info>> GetBi3InfoAsync(
        string league,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var procedure = await ResolveTeamProcedureNameAsync(connection, cancellationToken);
        var command = new CommandDefinition(
            $"{QuoteName(procedure.SchemaName)}.{QuoteName(procedure.ProcedureName)}",
            new { League = league },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var teams = await connection.QueryAsync<TeamBi3Info>(command);
        return teams.ToArray();
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

    private static async Task<ResolvedProcedureName> ResolveTeamProcedureNameAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            SELECT TOP 1
                SCHEMA_NAME(schema_id) AS SchemaName,
                name AS ProcedureName
            FROM sys.procedures
            WHERE name IN @ProcedureNames
            ORDER BY CASE name
                WHEN 'sp_GetTeamBig3Info' THEN 0
                WHEN 'sp_GetTeamBi3Info' THEN 1
                WHEN 'sp_GetTeamBig4Info' THEN 2
                ELSE 3
            END
            """,
            new { ProcedureNames = TeamProcedureCandidates },
            cancellationToken: cancellationToken);

        var procedure = await connection.QueryFirstOrDefaultAsync<ResolvedProcedureName>(command);
        return procedure ?? throw new InvalidOperationException(
            "Could not find stored procedure sp_GetTeamBig3Info, sp_GetTeamBi3Info or sp_GetTeamBig4Info.");
    }

    private static string QuoteName(string value)
    {
        return $"[{value.Replace("]", "]]")}]";
    }

    private sealed record ResolvedProcedureName(string SchemaName, string ProcedureName);
}
