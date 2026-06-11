using System.Data;
using CornersPrediction.Application.UpcomingMatches;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerUpcomingMatchesRepository : IUpcomingMatchesRepository
{
    private readonly string _connectionString;

    public SqlServerUpcomingMatchesRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<UpcomingMatchDto>> GetNextWeekMatchesAsync(
        string? genero,
        string? liga,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Genero", string.IsNullOrWhiteSpace(genero) ? null : genero.Trim(), DbType.String, size: 20);
        parameters.Add("Liga", string.IsNullOrWhiteSpace(liga) ? null : liga.Trim(), DbType.String, size: 200);

        var command = new CommandDefinition(
            "dbo.sp_GetMatchNextWeek",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var matches = await connection.QueryAsync<UpcomingMatchDto>(command);
        return matches.ToArray();
    }
}
