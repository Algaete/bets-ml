using System.Data;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.MatchHistory;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerMatchHistoryRepository : IMatchHistoryRepository
{
    private readonly string _connectionString;

    public SqlServerMatchHistoryRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<MatchHistoryItem> AddAsync(MatchHistoryItem item, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("League", item.League, DbType.String, size: 100);
        parameters.Add("Season", item.Season, DbType.String, size: 20);
        parameters.Add("MatchDate", item.MatchDate.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("HomeTeam", item.HomeTeam, DbType.String, size: 150);
        parameters.Add("AwayTeam", item.AwayTeam, DbType.String, size: 150);
        parameters.Add("HomeFormation", item.HomeFormation, DbType.String, size: 20);
        parameters.Add("AwayFormation", item.AwayFormation, DbType.String, size: 20);
        parameters.Add("HomeGoals", item.HomeGoals, DbType.Int32);
        parameters.Add("AwayGoals", item.AwayGoals, DbType.Int32);
        parameters.Add("HomeCorners", item.HomeCorners, DbType.Int32);
        parameters.Add("AwayCorners", item.AwayCorners, DbType.Int32);
        parameters.Add("HomeShots", item.HomeShots, DbType.Int32);
        parameters.Add("AwayShots", item.AwayShots, DbType.Int32);
        parameters.Add("HomeShotsOnGoal", item.HomeShotsOnGoal, DbType.Int32);
        parameters.Add("AwayShotsOnGoal", item.AwayShotsOnGoal, DbType.Int32);
        parameters.Add("HomePossession", item.HomePossession, DbType.Decimal);
        parameters.Add("AwayPossession", item.AwayPossession, DbType.Decimal);
        parameters.Add("IsKnockout", item.IsKnockout, DbType.Boolean);
        parameters.Add("InsertedId", dbType: DbType.Int64, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_InsertMatchHistory",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);

        var insertedId = parameters.Get<long>("InsertedId");
        item.Id = checked((int)insertedId);
        item.CreatedAtUtc = DateTime.UtcNow;

        return item;
    }

    public async Task<int> UpdateAsync(
        int id,
        MatchHistoryItem item,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("League", item.League, DbType.String, size: 100);
        parameters.Add("Season", item.Season, DbType.String, size: 20);
        parameters.Add("MatchDate", item.MatchDate.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("HomeTeam", item.HomeTeam, DbType.String, size: 150);
        parameters.Add("AwayTeam", item.AwayTeam, DbType.String, size: 150);
        parameters.Add("HomeFormation", item.HomeFormation, DbType.String, size: 20);
        parameters.Add("AwayFormation", item.AwayFormation, DbType.String, size: 20);
        parameters.Add("HomeGoals", item.HomeGoals, DbType.Int32);
        parameters.Add("AwayGoals", item.AwayGoals, DbType.Int32);
        parameters.Add("HomeCorners", item.HomeCorners, DbType.Int32);
        parameters.Add("AwayCorners", item.AwayCorners, DbType.Int32);
        parameters.Add("HomeShots", item.HomeShots, DbType.Int32);
        parameters.Add("AwayShots", item.AwayShots, DbType.Int32);
        parameters.Add("HomeShotsOnGoal", item.HomeShotsOnGoal, DbType.Int32);
        parameters.Add("AwayShotsOnGoal", item.AwayShotsOnGoal, DbType.Int32);
        parameters.Add("HomePossession", item.HomePossession, DbType.Decimal);
        parameters.Add("AwayPossession", item.AwayPossession, DbType.Decimal);
        parameters.Add("IsKnockout", item.IsKnockout, DbType.Boolean);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_UpdateMatchHistory",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);

        return parameters.Get<int>("RowsAffected");
    }

    public async Task<int> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.Int64);
        parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

        var command = new CommandDefinition(
            "dbo.sp_DeleteMatchHistory",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);

        return parameters.Get<int>("RowsAffected");
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetRecentAsync(
        string homeTeam,
        string awayTeam,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("HomeTeam", homeTeam.Trim(), DbType.String, size: 150);
        parameters.Add("AwayTeam", awayTeam.Trim(), DbType.String, size: 150);

        var command = new CommandDefinition(
            "dbo.sp_GetMatchHistoryByTeams",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<TeamMatchHistoryRow>(command);
        return rows.Select(ToDomain).ToArray();
    }

    private static MatchHistoryItem ToDomain(TeamMatchHistoryRow row)
    {
        var isHomeTeamHistory = row.EquipoCondicion.Equals("HOME", StringComparison.OrdinalIgnoreCase);

        return new MatchHistoryItem
        {
            Id = row.Id,
            TeamCondition = row.EquipoCondicion,
            League = row.League,
            Season = row.Season,
            MatchDate = DateOnly.FromDateTime(row.MatchDate),
            IsKnockout = row.IsKnockout,
            HomeTeam = isHomeTeamHistory ? row.Equipo : row.Rival,
            AwayTeam = isHomeTeamHistory ? row.Rival : row.Equipo,
            HomeFormation = row.HomeFormation,
            AwayFormation = row.AwayFormation,
            HomeGoals = isHomeTeamHistory ? row.GolesEquipo : row.GolesRival,
            AwayGoals = isHomeTeamHistory ? row.GolesRival : row.GolesEquipo,
            HomeCorners = isHomeTeamHistory ? row.CornersEquipo : row.CornersRival,
            AwayCorners = isHomeTeamHistory ? row.CornersRival : row.CornersEquipo,
            HomeShots = isHomeTeamHistory ? row.TirosEquipo : row.TirosRival,
            AwayShots = isHomeTeamHistory ? row.TirosRival : row.TirosEquipo,
            HomeShotsOnGoal = isHomeTeamHistory ? row.TirosPuertaEquipo : row.TirosPuertaRival,
            AwayShotsOnGoal = isHomeTeamHistory ? row.TirosPuertaRival : row.TirosPuertaEquipo,
            HomePossession = (double)(isHomeTeamHistory ? row.PosesionEquipo : row.PosesionRival),
            AwayPossession = (double)(isHomeTeamHistory ? row.PosesionRival : row.PosesionEquipo),
            CreatedAtUtc = row.CreatedAtUtc
        };
    }

    private sealed class TeamMatchHistoryRow
    {
        public int Id { get; init; }
        public string EquipoCondicion { get; init; } = string.Empty;
        public string League { get; init; } = string.Empty;
        public string Season { get; init; } = string.Empty;
        public DateTime MatchDate { get; init; }
        public bool IsKnockout { get; init; }
        public string Equipo { get; init; } = string.Empty;
        public string Rival { get; init; } = string.Empty;
        public string? HomeFormation { get; init; }
        public string? AwayFormation { get; init; }
        public int GolesEquipo { get; init; }
        public int GolesRival { get; init; }
        public int CornersEquipo { get; init; }
        public int CornersRival { get; init; }
        public int TirosEquipo { get; init; }
        public int TirosRival { get; init; }
        public int TirosPuertaEquipo { get; init; }
        public int TirosPuertaRival { get; init; }
        public decimal PosesionEquipo { get; init; }
        public decimal PosesionRival { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }
}
