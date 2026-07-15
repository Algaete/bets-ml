using System.Data;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.MatchHistory;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerMatchHistoryRepository : IMatchHistoryRepository
{
    private const string MissingTeamPlaceholder = "__NO_TEAM_SELECTED__";

    private readonly string _connectionString;

    public SqlServerMatchHistoryRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<MatchHistoryItem> AddAsync(MatchHistoryItem item, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await NormalizeItemAsync(connection, item, cancellationToken);
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

    public async Task<MatchHistoryBulkImportResult> BulkImportAsync(
        MatchHistoryBulkImportRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        request = await NormalizeBulkRequestAsync(connection, request, cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("League", request.League, DbType.String, size: 200);
        parameters.Add("Season", request.Season, DbType.String, size: 50);
        parameters.Add("FocusTeam", request.FocusTeam, DbType.String, size: 150);
        parameters.Add("TeamGender", request.TeamGender, DbType.String, size: 1);
        parameters.Add("IsKnockout", request.IsKnockout, DbType.Boolean);
        parameters.Add("MatchesJson", request.MatchesJson, DbType.String);

        var command = new CommandDefinition(
            "dbo.sp_BulkInsertMatchHistoryJson",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync<BulkImportRowRecord>(command))
            .Select(row => new MatchHistoryBulkImportRow(
                row.RowNumber,
                row.MatchDate is null ? null : DateOnly.FromDateTime(row.MatchDate.Value),
                row.HomeTeam,
                row.AwayTeam,
                row.Status,
                row.Message,
                row.InsertedId))
            .ToArray();

        return new MatchHistoryBulkImportResult(
            rows.Length,
            rows.Count(row => row.Status.Equals("Inserted", StringComparison.OrdinalIgnoreCase)),
            rows.Count(row => row.Status.Equals("Duplicate", StringComparison.OrdinalIgnoreCase)),
            rows.Count(row => row.Status.Equals("Error", StringComparison.OrdinalIgnoreCase)),
            rows);
    }

    public async Task<int> UpdateAsync(
        int id,
        MatchHistoryItem item,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await NormalizeItemAsync(connection, item, cancellationToken);
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
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        return await QueryRecentByTeamsAsync(homeTeam, awayTeam, league, teamGender, cancellationToken);
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetManualEntriesAsync(
        string? league,
        string? team,
        int take,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("League", string.IsNullOrWhiteSpace(league) ? null : league.Trim(), DbType.String, size: 100);
        parameters.Add("Team", string.IsNullOrWhiteSpace(team) ? null : team.Trim(), DbType.String, size: 150);
        parameters.Add("Take", Math.Clamp(take, 1, 100), DbType.Int32);

        var command = new CommandDefinition(
            """
            DECLARE @CanonicalLeague NVARCHAR(200) = dbo.fn_CanonicalLeagueName(@League);
            DECLARE @CanonicalTeam NVARCHAR(150) = dbo.fn_CanonicalTeamName(@Team);

            SELECT TOP (@Take)
                mh.Id,
                CAST(NULL AS NVARCHAR(10)) AS TeamCondition,
                League = COALESCE(mh.StandardizedLeague, mh.League),
                mh.Season,
                mh.MatchDate,
                mh.IsKnockout,
                HomeTeam = COALESCE(mh.StandardizedHomeTeam, mh.HomeTeam),
                AwayTeam = COALESCE(mh.StandardizedAwayTeam, mh.AwayTeam),
                mh.HomeFormation,
                mh.AwayFormation,
                mh.HomeGoals,
                mh.AwayGoals,
                mh.HomeCorners,
                mh.AwayCorners,
                mh.HomeShots,
                mh.AwayShots,
                mh.HomeShotsOnGoal,
                mh.AwayShotsOnGoal,
                mh.HomePossession,
                mh.AwayPossession,
                mh.CreatedAtUtc
            FROM dbo.MatchHistory mh
            WHERE (
                    @CanonicalLeague IS NULL
                    OR mh.StandardizedLeague = @CanonicalLeague
                    OR (mh.StandardizedLeague IS NULL AND mh.League = @CanonicalLeague)
                  )
              AND (
                    @CanonicalTeam IS NULL
                    OR mh.StandardizedHomeTeam = @CanonicalTeam
                    OR mh.StandardizedAwayTeam = @CanonicalTeam
                    OR (mh.StandardizedHomeTeam IS NULL AND mh.HomeTeam = @CanonicalTeam)
                    OR (mh.StandardizedAwayTeam IS NULL AND mh.AwayTeam = @CanonicalTeam)
                  )
            ORDER BY mh.CreatedAtUtc DESC, mh.MatchDate DESC, mh.Id DESC;
            """,
            parameters,
            commandType: CommandType.Text,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<MatchHistoryRecordRow>(command);
        return rows.Select(ToManualEntryDomain).ToArray();
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetLast10GeneralMatchesAsync(
        string team,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var homeMatchesTask = QueryRecentByTeamsAsync(team, MissingTeamPlaceholder, league, teamGender, cancellationToken);
        var awayMatchesTask = QueryRecentByTeamsAsync(MissingTeamPlaceholder, team, league, teamGender, cancellationToken);

        await Task.WhenAll(homeMatchesTask, awayMatchesTask);
        var matches = (await homeMatchesTask).Concat(await awayMatchesTask);

        return FilterByLeague(matches, league)
            .OrderByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .GroupBy(match => match.Id)
            .Select(group => PreferTeamPerspective(group, team))
            .Take(10)
            .ToArray();
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetLast10HomeMatchesAsync(
        string homeTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var matches = await QueryRecentByTeamsAsync(homeTeam, MissingTeamPlaceholder, league, teamGender, cancellationToken);

        return FilterByLeague(matches, league)
            .Where(match => match.TeamCondition?.Equals("HOME", StringComparison.OrdinalIgnoreCase) == true)
            .Where(match => TeamNameEquals(match.HomeTeam, homeTeam))
            .OrderByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .Take(10)
            .ToArray();
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetLast10AwayMatchesAsync(
        string awayTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var matches = await QueryRecentByTeamsAsync(MissingTeamPlaceholder, awayTeam, league, teamGender, cancellationToken);

        return FilterByLeague(matches, league)
            .Where(match => match.TeamCondition?.Equals("AWAY", StringComparison.OrdinalIgnoreCase) == true)
            .Where(match => TeamNameEquals(match.AwayTeam, awayTeam))
            .OrderByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .Take(10)
            .ToArray();
    }

    private async Task<IReadOnlyList<MatchHistoryItem>> QueryRecentByTeamsAsync(
        string homeTeam,
        string awayTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var parameters = new DynamicParameters();
        parameters.Add("HomeTeam", homeTeam.Trim(), DbType.String, size: 150);
        parameters.Add("AwayTeam", awayTeam.Trim(), DbType.String, size: 150);
        parameters.Add("League", string.IsNullOrWhiteSpace(league) ? null : league.Trim(), DbType.String, size: 100);
        parameters.Add("TeamGender", teamGender, DbType.String, size: 1);

        var command = new CommandDefinition(
            "dbo.sp_GetMatchHistoryByTeams",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<TeamMatchHistoryRow>(command);
        return rows.Select(ToDomain).ToArray();
    }

    private static async Task NormalizeItemAsync(
        SqlConnection connection,
        MatchHistoryItem item,
        CancellationToken cancellationToken)
    {
        var identity = await NormalizeIdentityAsync(
            connection,
            item.League,
            item.HomeTeam,
            item.AwayTeam,
            cancellationToken);

        item.League = identity.League;
        item.HomeTeam = identity.HomeTeam;
        item.AwayTeam = identity.AwayTeam;
    }

    private static async Task<MatchHistoryBulkImportRequest> NormalizeBulkRequestAsync(
        SqlConnection connection,
        MatchHistoryBulkImportRequest request,
        CancellationToken cancellationToken)
    {
        var focusIdentity = await NormalizeIdentityAsync(
            connection,
            request.League,
            request.FocusTeam,
            request.FocusTeam,
            cancellationToken);

        var root = JsonNode.Parse(request.MatchesJson) as JsonArray
            ?? throw new ArgumentException("Matches JSON must be an array.");

        foreach (var item in root.OfType<JsonObject>())
        {
            var homeTeam = GetJsonString(item, "homeTeam");
            var awayTeam = GetJsonString(item, "awayTeam");
            if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
                continue;

            var identity = await NormalizeIdentityAsync(
                connection,
                focusIdentity.League,
                homeTeam,
                awayTeam,
                cancellationToken);

            SetJsonString(item, "homeTeam", identity.HomeTeam);
            SetJsonString(item, "awayTeam", identity.AwayTeam);
        }

        return request with
        {
            League = focusIdentity.League,
            FocusTeam = focusIdentity.HomeTeam,
            MatchesJson = root.ToJsonString(new JsonSerializerOptions { PropertyNamingPolicy = null })
        };
    }

    private static async Task<NormalizedIdentity> NormalizeIdentityAsync(
        SqlConnection connection,
        string league,
        string homeTeam,
        string awayTeam,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    League = dbo.fn_CanonicalLeagueName(@League),
    HomeTeam = dbo.fn_CanonicalTeamName(@HomeTeam),
    AwayTeam = dbo.fn_CanonicalTeamName(@AwayTeam);
""";

        return await connection.QuerySingleAsync<NormalizedIdentity>(new CommandDefinition(
            sql,
            new { League = league, HomeTeam = homeTeam, AwayTeam = awayTeam },
            commandType: CommandType.Text,
            cancellationToken: cancellationToken));
    }

    private static string? GetJsonString(JsonObject item, string propertyName)
    {
        var property = item.FirstOrDefault(pair => pair.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return property.Value?.GetValue<string>();
    }

    private static void SetJsonString(JsonObject item, string propertyName, string value)
    {
        var existingProperty = item.FirstOrDefault(pair => pair.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        item[existingProperty.Key ?? propertyName] = value;
    }

    private static IEnumerable<MatchHistoryItem> FilterByLeague(
        IEnumerable<MatchHistoryItem> matches,
        string? league)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return matches;
        }

        return matches.Where(match => match.League.Equals(league.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static MatchHistoryItem PreferTeamPerspective(
        IEnumerable<MatchHistoryItem> matches,
        string team)
    {
        var normalizedTeam = team.Trim();
        var matchArray = matches.ToArray();

        return matchArray.FirstOrDefault(match =>
                TeamNameEquals(match.HomeTeam, normalizedTeam)) ??
            matchArray.FirstOrDefault(match =>
                TeamNameEquals(match.AwayTeam, normalizedTeam)) ??
            matchArray[0];
    }

    private static bool TeamNameEquals(string left, string right)
    {
        return left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static MatchHistoryItem ToManualEntryDomain(MatchHistoryRecordRow row)
    {
        return new MatchHistoryItem
        {
            Id = row.Id,
            TeamCondition = row.TeamCondition,
            QueryTeamCondition = row.TeamCondition,
            League = row.League,
            Season = row.Season,
            MatchDate = DateOnly.FromDateTime(row.MatchDate),
            IsKnockout = row.IsKnockout,
            HomeTeam = row.HomeTeam,
            AwayTeam = row.AwayTeam,
            HomeFormation = row.HomeFormation,
            AwayFormation = row.AwayFormation,
            HomeGoals = row.HomeGoals,
            AwayGoals = row.AwayGoals,
            HomeCorners = row.HomeCorners,
            AwayCorners = row.AwayCorners,
            HomeShots = row.HomeShots,
            AwayShots = row.AwayShots,
            HomeShotsOnGoal = row.HomeShotsOnGoal,
            AwayShotsOnGoal = row.AwayShotsOnGoal,
            HomePossession = (double)row.HomePossession,
            AwayPossession = (double)row.AwayPossession,
            CreatedAtUtc = row.CreatedAtUtc
        };
    }

    private static MatchHistoryItem ToDomain(TeamMatchHistoryRow row)
    {
        var realCondition = string.IsNullOrWhiteSpace(row.CondicionReal)
            ? row.EquipoCondicion
            : row.CondicionReal;
        var isHomeTeamHistory = realCondition.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) ||
            realCondition.Equals("HOME", StringComparison.OrdinalIgnoreCase);

        return new MatchHistoryItem
        {
            Id = row.Id,
            TeamCondition = isHomeTeamHistory ? "HOME" : "AWAY",
            QueryTeamCondition = row.EquipoCondicion,
            HistoryType = row.TipoHistorial,
            HistoryRank = row.RnHistorial,
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
        public string? TipoHistorial { get; init; }
        public int? RnHistorial { get; init; }
        public int Id { get; init; }
        public string EquipoCondicion { get; init; } = string.Empty;
        public string CondicionReal { get; init; } = string.Empty;
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

    private sealed class NormalizedIdentity
    {
        public string League { get; init; } = string.Empty;
        public string HomeTeam { get; init; } = string.Empty;
        public string AwayTeam { get; init; } = string.Empty;
    }

    private sealed class MatchHistoryRecordRow
    {
        public int Id { get; init; }
        public string? TeamCondition { get; init; }
        public string League { get; init; } = string.Empty;
        public string Season { get; init; } = string.Empty;
        public DateTime MatchDate { get; init; }
        public bool IsKnockout { get; init; }
        public string HomeTeam { get; init; } = string.Empty;
        public string AwayTeam { get; init; } = string.Empty;
        public string? HomeFormation { get; init; }
        public string? AwayFormation { get; init; }
        public int HomeGoals { get; init; }
        public int AwayGoals { get; init; }
        public int HomeCorners { get; init; }
        public int AwayCorners { get; init; }
        public int HomeShots { get; init; }
        public int AwayShots { get; init; }
        public int HomeShotsOnGoal { get; init; }
        public int AwayShotsOnGoal { get; init; }
        public decimal HomePossession { get; init; }
        public decimal AwayPossession { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class BulkImportRowRecord
    {
        public int RowNumber { get; init; }
        public DateTime? MatchDate { get; init; }
        public string? HomeTeam { get; init; }
        public string? AwayTeam { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public long? InsertedId { get; init; }
    }
}
