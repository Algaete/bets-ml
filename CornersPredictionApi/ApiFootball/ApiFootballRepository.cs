using System.Data;
using CornersMLData.Data;
using CornersMLData.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CornersPredictionApi.ApiFootball;

public sealed class ApiFootballRepository
{
    private readonly string _connectionString;
    private readonly IWebHostEnvironment _environment;
    private readonly MatchHistoryRepository _matchHistoryRepository;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static volatile bool _schemaReady;

    public ApiFootballRepository(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        MatchHistoryRepository matchHistoryRepository)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        _environment = environment;
        _matchHistoryRepository = matchHistoryRepository;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            var scriptPath = Path.Combine(_environment.ContentRootPath, "SqlScripts", "ApiFootballIntegration.sql");
            var script = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            foreach (var batch in SplitSqlBatches(script))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    batch,
                    commandTimeout: 180,
                    cancellationToken: cancellationToken));
            }
            _schemaReady = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<ApiFootballDatabaseAudit> GetAuditAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);

        const string sql = """
SELECT COUNT_BIG(*) FROM dbo.MatchHistory;
SELECT COUNT_BIG(*) FROM dbo.MatchHistory WHERE ApiFootballFixtureId IS NOT NULL;
SELECT COUNT_BIG(*) FROM dbo.MatchHistory
WHERE ApiFootballFixtureId IS NOT NULL
  AND HomeTeamGender = 'F' AND AwayTeamGender = 'F';
SELECT COUNT_BIG(*) FROM dbo.ApiFootballTeams;
SELECT COUNT_BIG(*) FROM dbo.ApiFootballLeagueSeasons;
SELECT COUNT_BIG(*) FROM dbo.LeagueStandingSnapshots;
SELECT COUNT_BIG(*) FROM dbo.ApiFootballSyncRuns;
SELECT c.name
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.MatchHistory')
  AND (c.name LIKE 'ApiFootball%' OR c.name IN
      ('DataSource','FixtureRound','FixtureStatus','VenueName','VenueCity','Referee',
       'HomeHalfTimeGoals','AwayHalfTimeGoals','HomeFouls','AwayFouls','HomeOffsides','AwayOffsides',
       'HomeYellowCards','AwayYellowCards','HomeRedCards','AwayRedCards','HomeTotalPasses','AwayTotalPasses',
       'HomePassAccuracy','AwayPassAccuracy'))
ORDER BY c.column_id;
SELECT LeagueName
FROM
(
    SELECT NULLIF(LTRIM(RTRIM(StandardizedLeague)), '') AS LeagueName FROM dbo.LeagueMapping
    UNION
    SELECT NULLIF(LTRIM(RTRIM(SourceLeague)), '') FROM dbo.LeagueMapping
    UNION
    SELECT NULLIF(LTRIM(RTRIM(League)), '') FROM dbo.MatchHistory
) q
WHERE LeagueName IS NOT NULL
ORDER BY LeagueName;
IF OBJECT_ID('dbo.PartidosProximos', 'U') IS NOT NULL
    SELECT DISTINCT NULLIF(LTRIM(RTRIM(Liga)), '') AS LeagueName
    FROM dbo.PartidosProximos
    WHERE FechaPartido >= CAST(GETDATE() AS DATE)
    ORDER BY LeagueName;
ELSE
    SELECT CAST(NULL AS NVARCHAR(200)) AS LeagueName WHERE 1 = 0;
""";

        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        var total = checked((int)await grid.ReadSingleAsync<long>());
        var apiRows = checked((int)await grid.ReadSingleAsync<long>());
        var womenRows = checked((int)await grid.ReadSingleAsync<long>());
        var teams = checked((int)await grid.ReadSingleAsync<long>());
        var leagueSeasons = checked((int)await grid.ReadSingleAsync<long>());
        var standings = checked((int)await grid.ReadSingleAsync<long>());
        var syncRuns = checked((int)await grid.ReadSingleAsync<long>());
        var columns = (await grid.ReadAsync<string>()).ToArray();
        var leagues = (await grid.ReadAsync<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        var upcoming = (await grid.ReadAsync<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return new ApiFootballDatabaseAudit(
            total,
            apiRows,
            womenRows,
            teams,
            leagueSeasons,
            standings,
            syncRuns,
            columns,
            leagues,
            upcoming);
    }

    internal async Task UpsertLeagueSeasonAsync(
        ApiFootballLeagueSeason league,
        string dbLeague,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        const string sql = """
MERGE dbo.ApiFootballLeagueSeasons WITH (HOLDLOCK) AS target
USING (SELECT @LeagueId AS ApiFootballLeagueId, @Season AS Season) AS source
ON target.ApiFootballLeagueId = source.ApiFootballLeagueId AND target.Season = source.Season
WHEN MATCHED THEN UPDATE SET
    LeagueName=@LeagueName, DbLeagueName=@DbLeague, Country=@Country, CompetitionType=@CompetitionType,
    IsCurrent=@IsCurrent, CoverageEvents=@Events, CoverageLineups=@Lineups,
    CoverageFixtureStatistics=@FixtureStatistics, CoveragePlayerStatistics=@PlayerStatistics,
    CoverageStandings=@Standings, CoveragePredictions=@Predictions, CoverageOdds=@Odds,
    LastSyncedAtUtc=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ApiFootballLeagueId,Season,LeagueName,DbLeagueName,Country,CompetitionType,IsCurrent,
     CoverageEvents,CoverageLineups,CoverageFixtureStatistics,CoveragePlayerStatistics,
     CoverageStandings,CoveragePredictions,CoverageOdds,LastSyncedAtUtc)
VALUES
    (@LeagueId,@Season,@LeagueName,@DbLeague,@Country,@CompetitionType,@IsCurrent,
     @Events,@Lineups,@FixtureStatistics,@PlayerStatistics,@Standings,@Predictions,@Odds,SYSUTCDATETIME());
""";
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            LeagueId = league.LeagueId,
            league.Season,
            league.LeagueName,
            DbLeague = dbLeague,
            league.Country,
            league.CompetitionType,
            league.IsCurrent,
            league.Events,
            league.Lineups,
            league.FixtureStatistics,
            league.PlayerStatistics,
            league.Standings,
            league.Predictions,
            league.Odds
        }, cancellationToken: cancellationToken));
    }

    internal async Task<ApiFootballPersistResult> UpsertMatchAsync(
        ApiFootballMatchData data,
        string dbLeague,
        bool isKnockout,
        bool updateExisting,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var existingId = await FindExistingIdAsync(connection, data.Fixture, dbLeague, cancellationToken);

        long id;
        string action;
        if (existingId.HasValue)
        {
            id = existingId.Value;
            action = updateExisting ? "Updated" : "Existing";
            if (updateExisting)
            {
                await UpdateCoreFieldsAsync(connection, id, data, cancellationToken);
            }
        }
        else
        {
            var dto = new MatchHistoryUpsertDto
            {
                League = dbLeague,
                Season = data.Fixture.Season.ToString(),
                MatchDate = data.Fixture.Date.UtcDateTime.Date,
                HomeTeam = data.Fixture.HomeTeam,
                AwayTeam = data.Fixture.AwayTeam,
                HomeFormation = data.HomeFormation,
                AwayFormation = data.AwayFormation,
                HomeGoals = data.Fixture.HomeGoals,
                AwayGoals = data.Fixture.AwayGoals,
                HomeCorners = data.HomeCorners,
                AwayCorners = data.AwayCorners,
                HomeShots = data.HomeShots,
                AwayShots = data.AwayShots,
                HomeShotsOnGoal = data.HomeShotsOnGoal,
                AwayShotsOnGoal = data.AwayShotsOnGoal,
                HomePossession = data.HomePossession,
                AwayPossession = data.AwayPossession,
                IsKnockout = isKnockout,
                SourceMatchId = data.Fixture.FixtureId.ToString(),
                HomeTeamGender = "M",
                AwayTeamGender = "M"
            };
            var persisted = await _matchHistoryRepository.UpsertMatchHistoryAsync(dto, cancellationToken);
            id = persisted.MatchId;
            action = persisted.Status == MatchHistoryPersistStatus.Inserted ? "Inserted" : "Updated";
        }

        await UpdateExtendedFieldsAsync(connection, id, data, cancellationToken);
        await UpsertTeamAsync(connection, data.Fixture.HomeTeamId, data.Fixture.HomeTeam, data.Fixture.Country, data.Fixture.HomeLogo, cancellationToken);
        await UpsertTeamAsync(connection, data.Fixture.AwayTeamId, data.Fixture.AwayTeam, data.Fixture.Country, data.Fixture.AwayLogo, cancellationToken);
        return new ApiFootballPersistResult(action, id);
    }

    internal async Task UpsertStandingsAsync(
        int leagueId,
        int season,
        DateOnly snapshotDate,
        IReadOnlyCollection<ApiFootballStanding> standings,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        const string sql = """
MERGE dbo.LeagueStandingSnapshots WITH (HOLDLOCK) AS target
USING (SELECT @LeagueId ApiFootballLeagueId, @Season Season, @SnapshotDate SnapshotDate,
              @GroupName GroupName, @TeamId ApiFootballTeamId) source
ON target.ApiFootballLeagueId=source.ApiFootballLeagueId AND target.Season=source.Season
AND target.SnapshotDate=source.SnapshotDate AND target.GroupName=source.GroupName
AND target.ApiFootballTeamId=source.ApiFootballTeamId
WHEN MATCHED THEN UPDATE SET TeamName=@TeamName, RankPosition=@Rank, Points=@Points,
    GoalsDifference=@GoalsDifference, Played=@Played, Won=@Won, Drawn=@Drawn, Lost=@Lost,
    GoalsFor=@GoalsFor, GoalsAgainst=@GoalsAgainst, RecentForm=@Form,
    Description=@Description, UpdatedAtUtc=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ApiFootballLeagueId,Season,SnapshotDate,GroupName,ApiFootballTeamId,TeamName,RankPosition,
     Points,GoalsDifference,Played,Won,Drawn,Lost,GoalsFor,GoalsAgainst,RecentForm,Description,UpdatedAtUtc)
VALUES
    (@LeagueId,@Season,@SnapshotDate,@GroupName,@TeamId,@TeamName,@Rank,@Points,@GoalsDifference,
     @Played,@Won,@Drawn,@Lost,@GoalsFor,@GoalsAgainst,@Form,@Description,SYSUTCDATETIME());
""";

        foreach (var standing in standings)
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                LeagueId = leagueId,
                Season = season,
                SnapshotDate = snapshotDate.ToDateTime(TimeOnly.MinValue),
                standing.GroupName,
                TeamId = standing.TeamId,
                standing.TeamName,
                standing.Rank,
                standing.Points,
                standing.GoalsDifference,
                standing.Played,
                standing.Won,
                standing.Drawn,
                standing.Lost,
                standing.GoalsFor,
                standing.GoalsAgainst,
                standing.Form,
                standing.Description
            }, cancellationToken: cancellationToken));
        }
    }

    public async Task SaveRunAsync(ApiFootballSyncResult result, DateTime startedAtUtc, CancellationToken cancellationToken)
    {
        if (result.DryRun)
        {
            return;
        }
        await using var connection = await OpenConnectionAsync(cancellationToken);
        const string sql = """
INSERT dbo.ApiFootballSyncRuns
(SyncRunId,ApiFootballLeagueId,Season,DateFrom,DateTo,DryRun,Discovered,Processed,Inserted,Updated,Skipped,Errors,StartedAtUtc,CompletedAtUtc)
VALUES
(@SyncRunId,@LeagueId,@Season,NULL,NULL,@DryRun,@Discovered,@Processed,@Inserted,@Updated,@Skipped,@Errors,@StartedAtUtc,SYSUTCDATETIME());
""";
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            result.SyncRunId,
            result.LeagueId,
            result.Season,
            result.DryRun,
            result.Discovered,
            result.Processed,
            result.Inserted,
            result.Updated,
            result.Skipped,
            result.Errors,
            StartedAtUtc = startedAtUtc
        }, cancellationToken: cancellationToken));
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<long?> FindExistingIdAsync(
        SqlConnection connection,
        ApiFootballFixture fixture,
        string dbLeague,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT TOP (1) Id
FROM dbo.MatchHistory WITH (NOLOCK)
WHERE ApiFootballFixtureId=@FixtureId
   OR SourceMatchId=@FixtureId
   OR
   (
       MatchDate=CAST(@MatchDate AS DATE)
       AND dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam,''),HomeTeam)) COLLATE Latin1_General_100_CI_AI=
           dbo.fn_CanonicalTeamName(@HomeTeam) COLLATE Latin1_General_100_CI_AI
       AND dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam,''),AwayTeam)) COLLATE Latin1_General_100_CI_AI=
           dbo.fn_CanonicalTeamName(@AwayTeam) COLLATE Latin1_General_100_CI_AI
   )
ORDER BY CASE WHEN ApiFootballFixtureId=@FixtureId THEN 0 WHEN SourceMatchId=@FixtureId THEN 1 ELSE 2 END,
         CASE WHEN dbo.fn_CanonicalLeagueName(COALESCE(NULLIF(StandardizedLeague,''),League)) COLLATE Latin1_General_100_CI_AI=
                        dbo.fn_CanonicalLeagueName(@League) COLLATE Latin1_General_100_CI_AI THEN 0 ELSE 1 END,
         Id DESC;
""";
        return await connection.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(sql, new
        {
            FixtureId = fixture.FixtureId,
            MatchDate = fixture.Date.UtcDateTime,
            League = dbLeague,
            fixture.HomeTeam,
            fixture.AwayTeam
        }, cancellationToken: cancellationToken));
    }

    private static Task UpdateCoreFieldsAsync(
        SqlConnection connection,
        long id,
        ApiFootballMatchData data,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.MatchHistory SET
    HomeFormation=COALESCE(@HomeFormation,HomeFormation), AwayFormation=COALESCE(@AwayFormation,AwayFormation),
    HomeGoals=COALESCE(@HomeGoals,HomeGoals), AwayGoals=COALESCE(@AwayGoals,AwayGoals),
    HomeCorners=COALESCE(@HomeCorners,HomeCorners), AwayCorners=COALESCE(@AwayCorners,AwayCorners),
    HomeShots=COALESCE(@HomeShots,HomeShots), AwayShots=COALESCE(@AwayShots,AwayShots),
    HomeShotsOnGoal=COALESCE(@HomeShotsOnGoal,HomeShotsOnGoal), AwayShotsOnGoal=COALESCE(@AwayShotsOnGoal,AwayShotsOnGoal),
    HomePossession=COALESCE(@HomePossession,HomePossession), AwayPossession=COALESCE(@AwayPossession,AwayPossession),
    SourceMatchId=COALESCE(SourceMatchId,@FixtureId)
WHERE Id=@Id;
""";
        return connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            data.HomeFormation,
            data.AwayFormation,
            data.Fixture.HomeGoals,
            data.Fixture.AwayGoals,
            data.HomeCorners,
            data.AwayCorners,
            data.HomeShots,
            data.AwayShots,
            data.HomeShotsOnGoal,
            data.AwayShotsOnGoal,
            data.HomePossession,
            data.AwayPossession,
            FixtureId = data.Fixture.FixtureId
        }, cancellationToken: cancellationToken));
    }

    private static Task UpdateExtendedFieldsAsync(
        SqlConnection connection,
        long id,
        ApiFootballMatchData data,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.MatchHistory SET DataSource='API-Football', ApiFootballFixtureId=@FixtureId,
 ApiFootballLeagueId=@LeagueId, ApiFootballHomeTeamId=@HomeTeamId, ApiFootballAwayTeamId=@AwayTeamId,
 FixtureRound=@Round, FixtureStatus=@Status, VenueName=@VenueName, VenueCity=@VenueCity, Referee=@Referee,
 HomeHalfTimeGoals=@HomeHalfTimeGoals, AwayHalfTimeGoals=@AwayHalfTimeGoals,
 HomeFouls=@HomeFouls, AwayFouls=@AwayFouls, HomeOffsides=@HomeOffsides, AwayOffsides=@AwayOffsides,
 HomeYellowCards=@HomeYellowCards, AwayYellowCards=@AwayYellowCards, HomeRedCards=@HomeRedCards, AwayRedCards=@AwayRedCards,
 HomeTotalPasses=@HomeTotalPasses, AwayTotalPasses=@AwayTotalPasses,
 HomePassAccuracy=@HomePassAccuracy, AwayPassAccuracy=@AwayPassAccuracy,
 ApiFootballUpdatedAtUtc=SYSUTCDATETIME()
WHERE Id=@Id;
""";
        return connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            FixtureId = data.Fixture.FixtureId,
            LeagueId = data.Fixture.LeagueId,
            HomeTeamId = data.Fixture.HomeTeamId,
            AwayTeamId = data.Fixture.AwayTeamId,
            data.Fixture.Round,
            data.Fixture.Status,
            data.Fixture.VenueName,
            data.Fixture.VenueCity,
            data.Fixture.Referee,
            data.Fixture.HomeHalfTimeGoals,
            data.Fixture.AwayHalfTimeGoals,
            data.HomeFouls,
            data.AwayFouls,
            data.HomeOffsides,
            data.AwayOffsides,
            data.HomeYellowCards,
            data.AwayYellowCards,
            data.HomeRedCards,
            data.AwayRedCards,
            data.HomeTotalPasses,
            data.AwayTotalPasses,
            data.HomePassAccuracy,
            data.AwayPassAccuracy
        }, cancellationToken: cancellationToken));
    }

    private static Task UpsertTeamAsync(
        SqlConnection connection,
        int teamId,
        string teamName,
        string country,
        string? logo,
        CancellationToken cancellationToken)
    {
        const string sql = """
MERGE dbo.ApiFootballTeams WITH (HOLDLOCK) target
USING (SELECT @TeamId ApiFootballTeamId) source ON target.ApiFootballTeamId=source.ApiFootballTeamId
WHEN MATCHED THEN UPDATE SET SourceTeamName=@TeamName, Country=@Country, LogoUrl=@Logo, LastSyncedAtUtc=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (ApiFootballTeamId,SourceTeamName,Country,LogoUrl,LastSyncedAtUtc)
VALUES (@TeamId,@TeamName,@Country,@Logo,SYSUTCDATETIME());
""";
        return connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            TeamId = teamId,
            TeamName = teamName,
            Country = country,
            Logo = logo
        }, cancellationToken: cancellationToken));
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(
                sql,
                @"^\s*GO\s*(?:--.*)?$",
                System.Text.RegularExpressions.RegexOptions.Multiline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();
}
