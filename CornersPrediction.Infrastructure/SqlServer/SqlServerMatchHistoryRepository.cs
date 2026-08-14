using System.Data;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Application.Teams;
using CornersPrediction.Domain.MatchHistory;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerMatchHistoryRepository : IMatchHistoryRepository
{
    private const string MissingTeamPlaceholder = "__NO_TEAM_SELECTED__";

    private readonly string _connectionString;
    private readonly ILogger<SqlServerMatchHistoryRepository> _logger;
    private static readonly ConcurrentDictionary<string, TeamNameCandidateCacheEntry> TeamNameCandidateCache =
        new(StringComparer.OrdinalIgnoreCase);

    public SqlServerMatchHistoryRepository(
        IConfiguration configuration,
        ILogger<SqlServerMatchHistoryRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        _logger = logger;
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
        DateOnly? beforeDate,
        CancellationToken cancellationToken)
    {
        return await QueryRecentByTeamsAsync(homeTeam, awayTeam, league, teamGender, beforeDate, cancellationToken);
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
        var homeMatchesTask = QueryRecentByTeamsAsync(team, MissingTeamPlaceholder, league, teamGender, null, cancellationToken);
        var awayMatchesTask = QueryRecentByTeamsAsync(MissingTeamPlaceholder, team, league, teamGender, null, cancellationToken);

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
        var matches = await QueryRecentByTeamsAsync(homeTeam, MissingTeamPlaceholder, league, teamGender, null, cancellationToken);

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
        var matches = await QueryRecentByTeamsAsync(MissingTeamPlaceholder, awayTeam, league, teamGender, null, cancellationToken);

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
        DateOnly? beforeDate,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await QueryRecentRowsAsync(
            connection,
            homeTeam,
            awayTeam,
            league,
            teamGender,
            beforeDate,
            cancellationToken);

        var needsHomeResolution = !IsMissingTeam(homeTeam) && !HasHistory(rows, "HOME");
        var needsAwayResolution = !IsMissingTeam(awayTeam) && !HasHistory(rows, "AWAY");
        if (!needsHomeResolution && !needsAwayResolution)
        {
            return ToPredictionHistory(rows);
        }

        var candidates = await GetTeamNameCandidatesAsync(
            connection,
            league,
            teamGender,
            cancellationToken);

        TeamNameMatch? homeMatch = needsHomeResolution
            ? FindCandidate(homeTeam, candidates)
            : null;
        TeamNameMatch? awayMatch = needsAwayResolution
            ? FindCandidate(awayTeam, candidates)
            : null;

        if (!string.IsNullOrWhiteSpace(league) &&
            ((needsHomeResolution && homeMatch is null) ||
             (needsAwayResolution && awayMatch is null)))
        {
            var allLeagueCandidates = await QueryTeamNameCandidatesAsync(
                connection,
                null,
                teamGender,
                cancellationToken);

            if (needsHomeResolution && homeMatch is null)
            {
                homeMatch = FindCandidate(homeTeam, allLeagueCandidates);
            }

            if (needsAwayResolution && awayMatch is null)
            {
                awayMatch = FindCandidate(awayTeam, allLeagueCandidates);
            }
        }

        var resolvedHomeTeam = homeMatch?.Name ?? homeTeam;
        var resolvedAwayTeam = awayMatch?.Name ?? awayTeam;

        if (homeMatch is null && awayMatch is null)
        {
            return ToPredictionHistory(rows);
        }

        if (!IsMissingTeam(resolvedHomeTeam) &&
            !IsMissingTeam(resolvedAwayTeam) &&
            TeamNameMatcher.AreEquivalent(resolvedHomeTeam, resolvedAwayTeam))
        {
            _logger.LogWarning(
                "Skipped team-name fallback because both sides resolved to {TeamName}. Inputs: {HomeTeam} vs {AwayTeam}",
                resolvedHomeTeam,
                homeTeam,
                awayTeam);
            return ToPredictionHistory(rows);
        }

        var resolvedRows = await QueryRecentRowsAsync(
            connection,
            resolvedHomeTeam,
            resolvedAwayTeam,
            league,
            teamGender,
            beforeDate,
            cancellationToken);

        await CacheSuccessfulAliasAsync(
            connection,
            homeTeam,
            homeMatch,
            HasHistory(resolvedRows, "HOME"),
            league,
            cancellationToken);
        await CacheSuccessfulAliasAsync(
            connection,
            awayTeam,
            awayMatch,
            HasHistory(resolvedRows, "AWAY"),
            league,
            cancellationToken);

        return ToPredictionHistory(resolvedRows);
    }

    private static async Task<TeamMatchHistoryRow[]> QueryRecentRowsAsync(
        SqlConnection connection,
        string homeTeam,
        string awayTeam,
        string? league,
        string teamGender,
        DateOnly? beforeDate,
        CancellationToken cancellationToken)
    {
        if (beforeDate.HasValue)
        {
            return await QueryExactHistoricalRowsAsync(
                connection,
                homeTeam,
                awayTeam,
                teamGender,
                beforeDate.Value,
                cancellationToken);
        }

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

        return (await connection.QueryAsync<TeamMatchHistoryRow>(command)).ToArray();
    }

    private static async Task<TeamMatchHistoryRow[]> QueryExactHistoricalRowsAsync(
        SqlConnection connection,
        string homeTeam,
        string awayTeam,
        string teamGender,
        DateOnly beforeDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
;WITH CandidateIds AS
(
    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND mh.HomeCorners IS NOT NULL
      AND mh.AwayCorners IS NOT NULL
      AND mh.StandardizedHomeTeam IN (@HomeTeam, @AwayTeam)
    UNION
    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND mh.HomeCorners IS NOT NULL
      AND mh.AwayCorners IS NOT NULL
      AND mh.StandardizedAwayTeam IN (@HomeTeam, @AwayTeam)
    UNION
    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND mh.HomeCorners IS NOT NULL
      AND mh.AwayCorners IS NOT NULL
      AND NULLIF(mh.StandardizedHomeTeam, N'') IS NULL
      AND mh.HomeTeam IN (@HomeTeam, @AwayTeam)
    UNION
    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND mh.HomeCorners IS NOT NULL
      AND mh.AwayCorners IS NOT NULL
      AND NULLIF(mh.StandardizedAwayTeam, N'') IS NULL
      AND mh.AwayTeam IN (@HomeTeam, @AwayTeam)
),
BaseRows AS
(
    SELECT
        mh.Id,
        League = COALESCE(NULLIF(mh.StandardizedLeague, N''), mh.League),
        mh.Season,
        mh.MatchDate,
        HomeTeam = COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam),
        AwayTeam = COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam),
        HomeGoals = COALESCE(mh.HomeGoals, 0),
        AwayGoals = COALESCE(mh.AwayGoals, 0),
        HomeCorners = COALESCE(mh.HomeCorners, 0),
        AwayCorners = COALESCE(mh.AwayCorners, 0),
        HomeShots = COALESCE(mh.HomeShots, 0),
        AwayShots = COALESCE(mh.AwayShots, 0),
        HomeShotsOnGoal = COALESCE(mh.HomeShotsOnGoal, 0),
        AwayShotsOnGoal = COALESCE(mh.AwayShotsOnGoal, 0),
        HomePossession = COALESCE(mh.HomePossession, 0),
        AwayPossession = COALESCE(mh.AwayPossession, 0),
        mh.IsKnockout,
        mh.HomeFormation,
        mh.AwayFormation,
        mh.CreatedAtUtc,
        DuplicateRank = ROW_NUMBER() OVER
        (
            PARTITION BY mh.MatchDate,
                COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam),
                COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam)
            ORDER BY CASE WHEN mh.ApiFootballFixtureId IS NOT NULL THEN 0 ELSE 1 END,
                COALESCE(mh.UpdatedAtUtc, mh.CreatedAtUtc) DESC,
                mh.Id DESC
        )
    FROM dbo.MatchHistory mh
    INNER JOIN CandidateIds candidate ON candidate.Id = mh.Id
    WHERE mh.MatchDate < @BeforeDate
      AND ISNULL(NULLIF(mh.HomeTeamGender, N''), N'M') = @TeamGender
      AND ISNULL(NULLIF(mh.AwayTeamGender, N''), N'M') = @TeamGender
      AND COALESCE(mh.HomeCorners, 0) + COALESCE(mh.AwayCorners, 0) > 0
),
DistinctRows AS
(
    SELECT * FROM BaseRows WHERE DuplicateRank = 1
)
SELECT *
FROM
(
    SELECT TOP (60)
        TipoHistorial = CAST(NULL AS NVARCHAR(30)),
        RnHistorial = ROW_NUMBER() OVER (ORDER BY MatchDate DESC, Id DESC),
        EquipoCondicion = CAST(N'HOME' AS NVARCHAR(10)),
        CondicionReal = CASE WHEN HomeTeam = @HomeTeam THEN N'LOCAL' ELSE N'VISITA' END,
        Id, League, Season, MatchDate,
        Equipo = CASE WHEN HomeTeam = @HomeTeam THEN HomeTeam ELSE AwayTeam END,
        Rival = CASE WHEN HomeTeam = @HomeTeam THEN AwayTeam ELSE HomeTeam END,
        GolesEquipo = CASE WHEN HomeTeam = @HomeTeam THEN HomeGoals ELSE AwayGoals END,
        GolesRival = CASE WHEN HomeTeam = @HomeTeam THEN AwayGoals ELSE HomeGoals END,
        CornersEquipo = CASE WHEN HomeTeam = @HomeTeam THEN HomeCorners ELSE AwayCorners END,
        CornersRival = CASE WHEN HomeTeam = @HomeTeam THEN AwayCorners ELSE HomeCorners END,
        TirosEquipo = CASE WHEN HomeTeam = @HomeTeam THEN HomeShots ELSE AwayShots END,
        TirosRival = CASE WHEN HomeTeam = @HomeTeam THEN AwayShots ELSE HomeShots END,
        TirosPuertaEquipo = CASE WHEN HomeTeam = @HomeTeam THEN HomeShotsOnGoal ELSE AwayShotsOnGoal END,
        TirosPuertaRival = CASE WHEN HomeTeam = @HomeTeam THEN AwayShotsOnGoal ELSE HomeShotsOnGoal END,
        PosesionEquipo = CASE WHEN HomeTeam = @HomeTeam THEN HomePossession ELSE AwayPossession END,
        PosesionRival = CASE WHEN HomeTeam = @HomeTeam THEN AwayPossession ELSE HomePossession END,
        IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc
    FROM DistinctRows
    WHERE HomeTeam = @HomeTeam OR AwayTeam = @HomeTeam
    ORDER BY MatchDate DESC, Id DESC
) homeHistory
UNION ALL
SELECT *
FROM
(
    SELECT TOP (60)
        TipoHistorial = CAST(NULL AS NVARCHAR(30)),
        RnHistorial = ROW_NUMBER() OVER (ORDER BY MatchDate DESC, Id DESC),
        EquipoCondicion = CAST(N'AWAY' AS NVARCHAR(10)),
        CondicionReal = CASE WHEN HomeTeam = @AwayTeam THEN N'LOCAL' ELSE N'VISITA' END,
        Id, League, Season, MatchDate,
        Equipo = CASE WHEN HomeTeam = @AwayTeam THEN HomeTeam ELSE AwayTeam END,
        Rival = CASE WHEN HomeTeam = @AwayTeam THEN AwayTeam ELSE HomeTeam END,
        GolesEquipo = CASE WHEN HomeTeam = @AwayTeam THEN HomeGoals ELSE AwayGoals END,
        GolesRival = CASE WHEN HomeTeam = @AwayTeam THEN AwayGoals ELSE HomeGoals END,
        CornersEquipo = CASE WHEN HomeTeam = @AwayTeam THEN HomeCorners ELSE AwayCorners END,
        CornersRival = CASE WHEN HomeTeam = @AwayTeam THEN AwayCorners ELSE HomeCorners END,
        TirosEquipo = CASE WHEN HomeTeam = @AwayTeam THEN HomeShots ELSE AwayShots END,
        TirosRival = CASE WHEN HomeTeam = @AwayTeam THEN AwayShots ELSE HomeShots END,
        TirosPuertaEquipo = CASE WHEN HomeTeam = @AwayTeam THEN HomeShotsOnGoal ELSE AwayShotsOnGoal END,
        TirosPuertaRival = CASE WHEN HomeTeam = @AwayTeam THEN AwayShotsOnGoal ELSE HomeShotsOnGoal END,
        PosesionEquipo = CASE WHEN HomeTeam = @AwayTeam THEN HomePossession ELSE AwayPossession END,
        PosesionRival = CASE WHEN HomeTeam = @AwayTeam THEN AwayPossession ELSE HomePossession END,
        IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc
    FROM DistinctRows
    WHERE HomeTeam = @AwayTeam OR AwayTeam = @AwayTeam
    ORDER BY MatchDate DESC, Id DESC
) awayHistory
OPTION (RECOMPILE);
""";

        var command = new CommandDefinition(
            sql,
            new
            {
                HomeTeam = homeTeam.Trim(),
                AwayTeam = awayTeam.Trim(),
                TeamGender = teamGender,
                BeforeDate = beforeDate.ToDateTime(TimeOnly.MinValue)
            },
            commandType: CommandType.Text,
            commandTimeout: 120,
            cancellationToken: cancellationToken);

        return (await connection.QueryAsync<TeamMatchHistoryRow>(command)).ToArray();
    }

    private static async Task<TeamMatchHistoryRow[]> QueryHistoricalRowsAsync(
        SqlConnection connection,
        string homeTeam,
        string awayTeam,
        string teamGender,
        DateOnly beforeDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
DECLARE @HomeStandard NVARCHAR(150) = dbo.fn_CanonicalTeamName(@HomeTeam);
DECLARE @AwayStandard NVARCHAR(150) = dbo.fn_CanonicalTeamName(@AwayTeam);

;WITH CandidateIds AS
(
    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND ISNULL(NULLIF(mh.HomeTeamGender, N''), N'M') = @TeamGender
      AND ISNULL(NULLIF(mh.AwayTeamGender, N''), N'M') = @TeamGender
      AND mh.StandardizedHomeTeam IN (@HomeStandard, @AwayStandard)

    UNION

    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND ISNULL(NULLIF(mh.HomeTeamGender, N''), N'M') = @TeamGender
      AND ISNULL(NULLIF(mh.AwayTeamGender, N''), N'M') = @TeamGender
      AND mh.StandardizedAwayTeam IN (@HomeStandard, @AwayStandard)

    UNION

    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND ISNULL(NULLIF(mh.HomeTeamGender, N''), N'M') = @TeamGender
      AND ISNULL(NULLIF(mh.AwayTeamGender, N''), N'M') = @TeamGender
      AND NULLIF(mh.StandardizedHomeTeam, N'') IS NULL
      AND mh.HomeTeam IN (@HomeStandard, @AwayStandard)

    UNION

    SELECT mh.Id
    FROM dbo.MatchHistory mh
    WHERE mh.MatchDate < @BeforeDate
      AND ISNULL(NULLIF(mh.HomeTeamGender, N''), N'M') = @TeamGender
      AND ISNULL(NULLIF(mh.AwayTeamGender, N''), N'M') = @TeamGender
      AND NULLIF(mh.StandardizedAwayTeam, N'') IS NULL
      AND mh.AwayTeam IN (@HomeStandard, @AwayStandard)
),
NormalizedMatches AS
(
    SELECT
        mh.Id,
        League = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedLeague)), N''), mh.League),
        mh.Season,
        mh.MatchDate,
        HomeTeam = dbo.fn_CanonicalTeamName(COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedHomeTeam)), N''), mh.HomeTeam)),
        AwayTeam = dbo.fn_CanonicalTeamName(COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedAwayTeam)), N''), mh.AwayTeam)),
        mh.HomeGoals,
        mh.AwayGoals,
        mh.HomeCorners,
        mh.AwayCorners,
        HomeShots = COALESCE(mh.HomeShots, 0),
        AwayShots = COALESCE(mh.AwayShots, 0),
        HomeShotsOnGoal = COALESCE(mh.HomeShotsOnGoal, 0),
        AwayShotsOnGoal = COALESCE(mh.AwayShotsOnGoal, 0),
        HomePossession = COALESCE(mh.HomePossession, 0),
        AwayPossession = COALESCE(mh.AwayPossession, 0),
        mh.IsKnockout,
        mh.HomeFormation,
        mh.AwayFormation,
        mh.CreatedAtUtc,
        mh.UpdatedAtUtc,
        mh.ApiFootballFixtureId,
        DuplicateRank = ROW_NUMBER() OVER
        (
            PARTITION BY
                mh.MatchDate,
                dbo.fn_CanonicalTeamName(COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedHomeTeam)), N''), mh.HomeTeam)),
                dbo.fn_CanonicalTeamName(COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedAwayTeam)), N''), mh.AwayTeam))
            ORDER BY
                CASE WHEN mh.ApiFootballFixtureId IS NOT NULL THEN 0 ELSE 1 END,
                COALESCE(mh.UpdatedAtUtc, mh.CreatedAtUtc) DESC,
                mh.Id DESC
        )
    FROM dbo.MatchHistory mh
    INNER JOIN CandidateIds candidate ON candidate.Id = mh.Id
    WHERE mh.MatchDate < @BeforeDate
      AND ISNULL(NULLIF(mh.HomeTeamGender, N''), N'M') = @TeamGender
      AND ISNULL(NULLIF(mh.AwayTeamGender, N''), N'M') = @TeamGender
      AND mh.HomeCorners IS NOT NULL
      AND mh.AwayCorners IS NOT NULL
      AND mh.HomeCorners + mh.AwayCorners > 0
),
DistinctMatches AS
(
    SELECT *
    FROM NormalizedMatches
    WHERE DuplicateRank = 1
),
FocusedMatches AS
(
    SELECT
        TipoHistorial = CAST(NULL AS NVARCHAR(30)),
        EquipoCondicion = CAST(N'HOME' AS NVARCHAR(10)),
        CondicionReal = CASE WHEN HomeTeam = @HomeStandard THEN N'LOCAL' ELSE N'VISITA' END,
        Id, League, Season, MatchDate,
        Equipo = CASE WHEN HomeTeam = @HomeStandard THEN HomeTeam ELSE AwayTeam END,
        Rival = CASE WHEN HomeTeam = @HomeStandard THEN AwayTeam ELSE HomeTeam END,
        GolesEquipo = CASE WHEN HomeTeam = @HomeStandard THEN HomeGoals ELSE AwayGoals END,
        GolesRival = CASE WHEN HomeTeam = @HomeStandard THEN AwayGoals ELSE HomeGoals END,
        CornersEquipo = CASE WHEN HomeTeam = @HomeStandard THEN HomeCorners ELSE AwayCorners END,
        CornersRival = CASE WHEN HomeTeam = @HomeStandard THEN AwayCorners ELSE HomeCorners END,
        TirosEquipo = CASE WHEN HomeTeam = @HomeStandard THEN HomeShots ELSE AwayShots END,
        TirosRival = CASE WHEN HomeTeam = @HomeStandard THEN AwayShots ELSE HomeShots END,
        TirosPuertaEquipo = CASE WHEN HomeTeam = @HomeStandard THEN HomeShotsOnGoal ELSE AwayShotsOnGoal END,
        TirosPuertaRival = CASE WHEN HomeTeam = @HomeStandard THEN AwayShotsOnGoal ELSE HomeShotsOnGoal END,
        PosesionEquipo = CASE WHEN HomeTeam = @HomeStandard THEN HomePossession ELSE AwayPossession END,
        PosesionRival = CASE WHEN HomeTeam = @HomeStandard THEN AwayPossession ELSE HomePossession END,
        IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc
    FROM DistinctMatches
    WHERE HomeTeam = @HomeStandard OR AwayTeam = @HomeStandard

    UNION ALL

    SELECT
        TipoHistorial = CAST(NULL AS NVARCHAR(30)),
        EquipoCondicion = CAST(N'AWAY' AS NVARCHAR(10)),
        CondicionReal = CASE WHEN HomeTeam = @AwayStandard THEN N'LOCAL' ELSE N'VISITA' END,
        Id, League, Season, MatchDate,
        Equipo = CASE WHEN HomeTeam = @AwayStandard THEN HomeTeam ELSE AwayTeam END,
        Rival = CASE WHEN HomeTeam = @AwayStandard THEN AwayTeam ELSE HomeTeam END,
        GolesEquipo = CASE WHEN HomeTeam = @AwayStandard THEN HomeGoals ELSE AwayGoals END,
        GolesRival = CASE WHEN HomeTeam = @AwayStandard THEN AwayGoals ELSE HomeGoals END,
        CornersEquipo = CASE WHEN HomeTeam = @AwayStandard THEN HomeCorners ELSE AwayCorners END,
        CornersRival = CASE WHEN HomeTeam = @AwayStandard THEN AwayCorners ELSE HomeCorners END,
        TirosEquipo = CASE WHEN HomeTeam = @AwayStandard THEN HomeShots ELSE AwayShots END,
        TirosRival = CASE WHEN HomeTeam = @AwayStandard THEN AwayShots ELSE HomeShots END,
        TirosPuertaEquipo = CASE WHEN HomeTeam = @AwayStandard THEN HomeShotsOnGoal ELSE AwayShotsOnGoal END,
        TirosPuertaRival = CASE WHEN HomeTeam = @AwayStandard THEN AwayShotsOnGoal ELSE HomeShotsOnGoal END,
        PosesionEquipo = CASE WHEN HomeTeam = @AwayStandard THEN HomePossession ELSE AwayPossession END,
        PosesionRival = CASE WHEN HomeTeam = @AwayStandard THEN AwayPossession ELSE HomePossession END,
        IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc
    FROM DistinctMatches
    WHERE HomeTeam = @AwayStandard OR AwayTeam = @AwayStandard
),
Ranked AS
(
    SELECT
        RnHistorial = ROW_NUMBER() OVER (PARTITION BY EquipoCondicion ORDER BY MatchDate DESC, Id DESC),
        *
    FROM FocusedMatches
)
SELECT
    TipoHistorial, RnHistorial, EquipoCondicion, CondicionReal,
    Id, League, Season, MatchDate, Equipo, Rival,
    GolesEquipo, GolesRival, CornersEquipo, CornersRival,
    TirosEquipo, TirosRival, TirosPuertaEquipo, TirosPuertaRival,
    PosesionEquipo, PosesionRival, IsKnockout,
    HomeFormation, AwayFormation, CreatedAtUtc
FROM Ranked
WHERE RnHistorial <= 30
ORDER BY EquipoCondicion, RnHistorial;
""";

        var command = new CommandDefinition(
            sql,
            new
            {
                HomeTeam = homeTeam.Trim(),
                AwayTeam = awayTeam.Trim(),
                TeamGender = teamGender,
                BeforeDate = beforeDate.ToDateTime(TimeOnly.MinValue)
            },
            commandType: CommandType.Text,
            commandTimeout: 300,
            cancellationToken: cancellationToken);

        return (await connection.QueryAsync<TeamMatchHistoryRow>(command)).ToArray();
    }

    private async Task<TeamNameCandidate[]> GetTeamNameCandidatesAsync(
        SqlConnection connection,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var candidates = await GetCachedTeamNameCandidatesAsync(
            connection, league, teamGender, cancellationToken);

        if (candidates.Length > 0 || string.IsNullOrWhiteSpace(league))
        {
            return candidates;
        }

        return await GetCachedTeamNameCandidatesAsync(
            connection, null, teamGender, cancellationToken);
    }

    private async Task<TeamNameCandidate[]> GetCachedTeamNameCandidatesAsync(
        SqlConnection connection,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{teamGender}|{league?.Trim() ?? "*"}";
        if (TeamNameCandidateCache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return cached.Candidates;
        }

        var candidates = await QueryTeamNameCandidatesAsync(
            connection, league, teamGender, cancellationToken);
        TeamNameCandidateCache[cacheKey] = new TeamNameCandidateCacheEntry(
            candidates,
            DateTime.UtcNow.AddMinutes(15));
        return candidates;
    }

    private static async Task<TeamNameCandidate[]> QueryTeamNameCandidatesAsync(
        SqlConnection connection,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        const string sql = """
DECLARE @CanonicalLeague NVARCHAR(200) = dbo.fn_CanonicalLeagueName(@League);

;WITH TeamNames AS
(
    SELECT
        TeamName = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedHomeTeam)), N''), mh.HomeTeam)
    FROM dbo.MatchHistory mh
    WHERE ISNULL(NULLIF(mh.HomeTeamGender, N''), N'M') = @TeamGender
      AND mh.HomeCorners IS NOT NULL
      AND mh.AwayCorners IS NOT NULL
      AND mh.HomeCorners + mh.AwayCorners > 0
      AND
      (
          @CanonicalLeague IS NULL
          OR mh.StandardizedLeague COLLATE Latin1_General_100_CI_AI = @CanonicalLeague COLLATE Latin1_General_100_CI_AI
          OR (NULLIF(mh.StandardizedLeague, N'') IS NULL
              AND mh.League COLLATE Latin1_General_100_CI_AI = @CanonicalLeague COLLATE Latin1_General_100_CI_AI)
      )

    UNION ALL

    SELECT
        TeamName = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedAwayTeam)), N''), mh.AwayTeam)
    FROM dbo.MatchHistory mh
    WHERE ISNULL(NULLIF(mh.AwayTeamGender, N''), N'M') = @TeamGender
      AND mh.HomeCorners IS NOT NULL
      AND mh.AwayCorners IS NOT NULL
      AND mh.HomeCorners + mh.AwayCorners > 0
      AND
      (
          @CanonicalLeague IS NULL
          OR mh.StandardizedLeague COLLATE Latin1_General_100_CI_AI = @CanonicalLeague COLLATE Latin1_General_100_CI_AI
          OR (NULLIF(mh.StandardizedLeague, N'') IS NULL
              AND mh.League COLLATE Latin1_General_100_CI_AI = @CanonicalLeague COLLATE Latin1_General_100_CI_AI)
      )
)
SELECT TOP (5000)
    TeamName,
    Occurrences = COUNT_BIG(1)
FROM TeamNames
WHERE NULLIF(LTRIM(RTRIM(TeamName)), N'') IS NOT NULL
GROUP BY TeamName
ORDER BY Occurrences DESC, TeamName;
""";

        var command = new CommandDefinition(
            sql,
            new
            {
                League = string.IsNullOrWhiteSpace(league) ? null : league.Trim(),
                TeamGender = teamGender
            },
            commandType: CommandType.Text,
            commandTimeout: 300,
            cancellationToken: cancellationToken);

        return (await connection.QueryAsync<TeamNameCandidate>(command)).ToArray();
    }

    private static TeamNameMatch? FindCandidate(
        string team,
        IReadOnlyList<TeamNameCandidate> candidates) =>
        TeamNameMatcher.FindBestMatch(team, candidates.Select(candidate => candidate.TeamName));

    private async Task CacheSuccessfulAliasAsync(
        SqlConnection connection,
        string sourceTeam,
        TeamNameMatch? match,
        bool hasResolvedHistory,
        string? league,
        CancellationToken cancellationToken)
    {
        if (match is null || !hasResolvedHistory)
        {
            return;
        }

        _logger.LogInformation(
            "Resolved team name {SourceTeam} to {CanonicalTeam} using {MatchKind} ({Confidence:P1}) for league {League}",
            sourceTeam,
            match.Name,
            match.Kind,
            match.Confidence,
            league ?? "all leagues");

        if (!match.CanPersistAlias)
        {
            return;
        }

        const string sql = """
MERGE dbo.TeamNameAlias WITH (HOLDLOCK) AS Target
USING
(
    SELECT
        AliasKey = dbo.fn_NormalizeNameKey(@SourceTeam),
        CanonicalName = CONVERT(NVARCHAR(150), @CanonicalTeam)
) AS Source
    ON Target.AliasKey = Source.AliasKey
WHEN NOT MATCHED AND Source.AliasKey <> N'' THEN
    INSERT (AliasKey, CanonicalName, UpdatedAtUtc)
    VALUES (Source.AliasKey, Source.CanonicalName, SYSUTCDATETIME());
""";

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { SourceTeam = sourceTeam.Trim(), CanonicalTeam = match.Name },
            commandType: CommandType.Text,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    private static bool HasHistory(IEnumerable<TeamMatchHistoryRow> rows, string queryTeamCondition) =>
        rows.Any(row => row.EquipoCondicion.Equals(queryTeamCondition, StringComparison.OrdinalIgnoreCase));

    private static bool IsMissingTeam(string team) =>
        team.Equals(MissingTeamPlaceholder, StringComparison.Ordinal);

    private static MatchHistoryItem[] ToPredictionHistory(IEnumerable<TeamMatchHistoryRow> rows)
    {
        var result = new List<MatchHistoryItem>();
        var usableRows = rows
            .Where(row => row.CornersEquipo + row.CornersRival > 0)
            .Select(ToDomain);

        foreach (var match in usableRows)
        {
            var alreadyIncluded = result.Any(existing =>
                existing.MatchDate == match.MatchDate &&
                string.Equals(existing.QueryTeamCondition, match.QueryTeamCondition, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.HistoryType, match.HistoryType, StringComparison.OrdinalIgnoreCase) &&
                TeamNameEquals(existing.HomeTeam, match.HomeTeam) &&
                TeamNameEquals(existing.AwayTeam, match.AwayTeam));

            if (!alreadyIncluded)
            {
                result.Add(match);
            }
        }

        return result.ToArray();
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
        return TeamNameMatcher.AreEquivalent(left, right);
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

    private sealed class TeamNameCandidate
    {
        public string TeamName { get; init; } = string.Empty;
        public long Occurrences { get; init; }
    }

    private sealed record TeamNameCandidateCacheEntry(
        TeamNameCandidate[] Candidates,
        DateTime ExpiresAtUtc);

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
