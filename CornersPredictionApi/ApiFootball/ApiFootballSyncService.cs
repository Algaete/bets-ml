using System.Globalization;
using System.Text.Json;

namespace CornersPredictionApi.ApiFootball;

public sealed class ApiFootballSyncService
{
    private static readonly HashSet<int> WomensCompetitionIds = new()
    {
        44, 64, 82, 254, 549, 640, 641, 649, 660, 666, 673, 725, 736,
        915, 918, 1013, 1103, 1117, 1136, 1182, 1189, 1229
    };

    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "AET", "PEN"
    };

    private readonly ApiFootballClient _client;
    private readonly ApiFootballRepository _repository;
    private readonly ILogger<ApiFootballSyncService> _logger;

    public ApiFootballSyncService(
        ApiFootballClient client,
        ApiFootballRepository repository,
        ILogger<ApiFootballSyncService> logger)
    {
        _client = client;
        _repository = repository;
        _logger = logger;
    }

    public Task<ApiFootballStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        _client.GetStatusAsync(cancellationToken);

    public Task<ApiFootballDatabaseAudit> GetDatabaseAuditAsync(CancellationToken cancellationToken) =>
        _repository.GetAuditAsync(cancellationToken);

    public async Task<ApiFootballDiscoveryResult> DiscoverAsync(
        ApiFootballDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ValidateDateRange(request.DateFrom, request.DateTo);
        var fixtures = new Dictionary<long, ApiFootballFixture>();

        for (var date = request.DateFrom; date <= request.DateTo; date = date.AddDays(1))
        {
            var dailyFixtures = ParseFixtures(await _client.GetFixturesForDateAsync(date, cancellationToken));
            foreach (var fixture in dailyFixtures.Where(fixture => FinishedStatuses.Contains(fixture.Status)))
            {
                fixtures[fixture.FixtureId] = fixture;
            }
        }

        var rows = fixtures.Values
            .GroupBy(fixture => new
            {
                fixture.LeagueId,
                fixture.LeagueName,
                fixture.Country,
                fixture.Season
            })
            .Select(group => new ApiFootballCompetitionSummary(
                group.Key.LeagueId,
                group.Key.LeagueName,
                group.Key.Country,
                group.Key.Season,
                group.Count(),
                DateOnly.FromDateTime(group.Min(fixture => fixture.Date.UtcDateTime)),
                DateOnly.FromDateTime(group.Max(fixture => fixture.Date.UtcDateTime))))
            .OrderByDescending(row => row.FinishedFixtures)
            .ThenBy(row => row.Country)
            .ThenBy(row => row.League)
            .ToArray();

        return new ApiFootballDiscoveryResult(
            request.DateFrom,
            request.DateTo,
            fixtures.Count,
            rows.Length,
            _client.DailyRemaining,
            _client.MinuteRemaining,
            rows);
    }

    public async Task<ApiFootballBulkSyncResult> BulkSyncAsync(
        ApiFootballBulkSyncRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBulkRequest(request);
        var discovery = await DiscoverAsync(
            new ApiFootballDiscoveryRequest(request.DateFrom, request.DateTo),
            cancellationToken);
        var eligible = discovery.Rows
            .Where(row => !request.SeniorMenOnly || IsSeniorMensCompetition(row))
            .Skip(request.CompetitionOffset)
            .Take(request.MaxCompetitions)
            .ToArray();
        var rows = new List<ApiFootballBulkSyncRow>();
        var consideredFixtures = 0;
        var processedFixtures = 0;
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var errors = 0;
        var stoppedByQuota = false;

        foreach (var competition in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fixtureCapacity = request.MaxTotalFixtures - consideredFixtures;
            if (fixtureCapacity <= 0)
            {
                break;
            }

            var requestedFixtures = Math.Min(
                competition.FinishedFixtures,
                Math.Min(request.MaxFixturesPerCompetition, fixtureCapacity));
            var dailyRemaining = ParseRemaining(_client.DailyRemaining);
            if (dailyRemaining.HasValue)
            {
                var requestsPerFixture = request.SyncLineups ? 2 : 1;
                var affordableFixtures = Math.Max(
                    0,
                    (dailyRemaining.Value - request.MinimumDailyRemaining - 3) / requestsPerFixture);
                requestedFixtures = Math.Min(requestedFixtures, affordableFixtures);
                if (requestedFixtures == 0)
                {
                    stoppedByQuota = true;
                    break;
                }
            }

            try
            {
                consideredFixtures += requestedFixtures;
                var probeFixtures = Math.Min(requestedFixtures, 2);
                var probe = await SyncAsync(new ApiFootballSyncRequest(
                    competition.LeagueId,
                    competition.Season,
                    DateFrom: request.DateFrom,
                    DateTo: request.DateTo,
                    MaxFixtures: probeFixtures,
                    DryRun: true,
                    UpdateExisting: request.UpdateExisting,
                    SyncStandings: false,
                    SyncLineups: false), cancellationToken);

                if (!probe.FixtureStatisticsCovered || probe.Processed <= probe.Skipped)
                {
                    skipped += requestedFixtures;
                    errors += probe.Errors;
                    rows.Add(new ApiFootballBulkSyncRow(
                        competition.LeagueId,
                        probe.DbLeague,
                        competition.Country,
                        competition.Season,
                        competition.FinishedFixtures,
                        requestedFixtures,
                        probe.Processed,
                        0,
                        0,
                        requestedFixtures,
                        probe.Errors,
                        !probe.FixtureStatisticsCovered ? "NoStatisticsCoverage" : "NoCompleteFixtures"));
                    continue;
                }

                var result = request.DryRun
                    ? probe
                    : await SyncAsync(new ApiFootballSyncRequest(
                        competition.LeagueId,
                        competition.Season,
                        DateFrom: request.DateFrom,
                        DateTo: request.DateTo,
                        MaxFixtures: requestedFixtures,
                        DryRun: false,
                        UpdateExisting: request.UpdateExisting,
                        SyncStandings: request.SyncStandings,
                        SyncLineups: request.SyncLineups), cancellationToken);
                processedFixtures += result.Processed;
                inserted += result.Inserted;
                updated += result.Updated;
                skipped += result.Skipped;
                errors += result.Errors;
                rows.Add(new ApiFootballBulkSyncRow(
                    competition.LeagueId,
                    result.DbLeague,
                    competition.Country,
                    competition.Season,
                    competition.FinishedFixtures,
                    requestedFixtures,
                    result.Processed,
                    result.Inserted,
                    result.Updated,
                    result.Skipped,
                    result.Errors,
                    result.Errors > 0
                        ? "Partial"
                        : result.Processed <= result.Skipped ? "NoCompleteFixtures" : "Completed"));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                _logger.LogError(
                    exception,
                    "API-Football bulk sync failed for league {LeagueId} season {Season}",
                    competition.LeagueId,
                    competition.Season);
                rows.Add(new ApiFootballBulkSyncRow(
                    competition.LeagueId,
                    competition.League,
                    competition.Country,
                    competition.Season,
                    competition.FinishedFixtures,
                    requestedFixtures,
                    0,
                    0,
                    0,
                    0,
                    1,
                    "Error",
                    exception.Message));
            }
        }

        return new ApiFootballBulkSyncResult(
            request.DateFrom,
            request.DateTo,
            request.DryRun,
            discovery.FinishedFixtures,
            discovery.Competitions,
            eligible.Length,
            rows.Count,
            processedFixtures,
            inserted,
            updated,
            skipped,
            errors,
            stoppedByQuota,
            _client.DailyRemaining,
            _client.MinuteRemaining,
            rows);
    }

    public async Task<ApiFootballSyncResult> SyncAsync(
        ApiFootballSyncRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await _repository.EnsureSchemaAsync(cancellationToken);
        var startedAtUtc = DateTime.UtcNow;
        var runId = Guid.NewGuid();
        var league = ParseLeagueSeason(
            await _client.GetLeagueAsync(request.LeagueId, request.Season, cancellationToken),
            request.LeagueId,
            request.Season);
        var dbLeague = string.IsNullOrWhiteSpace(request.DbLeague)
            ? ResolveDefaultDbLeague(league)
            : request.DbLeague.Trim();

        if (!request.DryRun)
        {
            await _repository.UpsertLeagueSeasonAsync(league, dbLeague, cancellationToken);
        }

        var fixtures = ParseFixtures(await _client.GetFixturesAsync(
                request.LeagueId,
                request.Season,
                request.DateFrom,
                request.DateTo,
                cancellationToken))
            .Where(fixture => FinishedStatuses.Contains(fixture.Status))
            .OrderByDescending(fixture => fixture.Date)
            .Take(request.MaxFixtures)
            .ToArray();

        var rows = new List<ApiFootballSyncRow>();
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var errors = 0;
        var processed = 0;

        foreach (var fixture in fixtures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!league.FixtureStatistics)
                {
                    skipped++;
                    rows.Add(ToRow(fixture, "Skipped", "The league season does not cover fixture statistics."));
                    continue;
                }

                var matchData = new ApiFootballMatchData { Fixture = fixture };
                ParseStatistics(
                    await _client.GetFixtureStatisticsAsync(fixture.FixtureId, cancellationToken),
                    matchData);
                if (request.SyncLineups && league.Lineups)
                {
                    ParseLineups(
                        await _client.GetFixtureLineupsAsync(fixture.FixtureId, cancellationToken),
                        matchData);
                }
                processed++;

                if (!matchData.HasRequiredStatistics)
                {
                    skipped++;
                    rows.Add(ToRow(fixture, "Skipped", "Required corners, shots, shots on goal or possession are missing."));
                    continue;
                }

                if (request.DryRun)
                {
                    rows.Add(ToRow(fixture, "Ready", "Complete fixture; no database changes because DryRun=true."));
                    continue;
                }

                var persisted = await _repository.UpsertMatchAsync(
                    matchData,
                    dbLeague,
                    IsKnockout(league, fixture.Round),
                    request.UpdateExisting,
                    cancellationToken);
                if (persisted.Action == "Inserted")
                {
                    inserted++;
                }
                else if (persisted.Action == "Updated")
                {
                    updated++;
                }
                rows.Add(ToRow(fixture, persisted.Action, $"MatchHistory {persisted.Action.ToLowerInvariant()}.", persisted.MatchHistoryId));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                _logger.LogError(exception, "API-Football fixture sync failed for {FixtureId}", fixture.FixtureId);
                rows.Add(ToRow(fixture, "Error", exception.Message));
            }
        }

        if (!request.DryRun && request.SyncStandings && league.Standings)
        {
            try
            {
                var standings = ParseStandings(await _client.GetStandingsAsync(
                    request.LeagueId,
                    request.Season,
                    cancellationToken));
                await _repository.UpsertStandingsAsync(
                    request.LeagueId,
                    request.Season,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    standings,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Could not synchronize standings for league {LeagueId} season {Season}", request.LeagueId, request.Season);
            }
        }

        var result = new ApiFootballSyncResult(
            runId,
            request.LeagueId,
            league.LeagueName,
            dbLeague,
            request.Season,
            request.DryRun,
            league.FixtureStatistics,
            fixtures.Length,
            processed,
            inserted,
            updated,
            skipped,
            errors,
            _client.DailyRemaining,
            _client.MinuteRemaining,
            rows);
        await _repository.SaveRunAsync(result, startedAtUtc, cancellationToken);
        return result;
    }

    private static void Validate(ApiFootballSyncRequest request)
    {
        if (request.LeagueId <= 0)
        {
            throw new ArgumentException("LeagueId must be greater than zero.");
        }
        if (request.Season is < 2000 or > 2100)
        {
            throw new ArgumentException("Season is outside the supported range.");
        }
        if (request.MaxFixtures is < 1 or > 2000)
        {
            throw new ArgumentException("MaxFixtures must be between 1 and 2000.");
        }
        if (request.DateFrom.HasValue && request.DateTo.HasValue && request.DateFrom > request.DateTo)
        {
            throw new ArgumentException("DateFrom cannot be greater than DateTo.");
        }
    }

    private static void ValidateDateRange(DateOnly dateFrom, DateOnly dateTo)
    {
        if (dateFrom > dateTo)
        {
            throw new ArgumentException("DateFrom cannot be greater than DateTo.");
        }
        if (dateTo.DayNumber - dateFrom.DayNumber > 31)
        {
            throw new ArgumentException("The discovery range cannot exceed 32 days.");
        }
    }

    private static void ValidateBulkRequest(ApiFootballBulkSyncRequest request)
    {
        ValidateDateRange(request.DateFrom, request.DateTo);
        if (request.MaxCompetitions is < 1 or > 500)
        {
            throw new ArgumentException("MaxCompetitions must be between 1 and 500.");
        }
        if (request.CompetitionOffset is < 0 or > 2000)
        {
            throw new ArgumentException("CompetitionOffset must be between 0 and 2000.");
        }
        if (request.MaxFixturesPerCompetition is < 1 or > 2000)
        {
            throw new ArgumentException("MaxFixturesPerCompetition must be between 1 and 2000.");
        }
        if (request.MaxTotalFixtures is < 1 or > 7000)
        {
            throw new ArgumentException("MaxTotalFixtures must be between 1 and 7000.");
        }
        if (request.MinimumDailyRemaining is < 100 or > 2000)
        {
            throw new ArgumentException("MinimumDailyRemaining must be between 100 and 2000.");
        }
    }

    private static bool IsSeniorMensCompetition(ApiFootballCompetitionSummary competition)
    {
        if (WomensCompetitionIds.Contains(competition.LeagueId))
        {
            return false;
        }

        var value = $"{competition.League} {competition.Country}";
        string[] excludedTokens =
        {
            "Friendly", "Friendlies", "Women", " W League", "WSL", "Frauen", "Feminine",
            "Femenil", "Youth", "Reserve", "Junior", "Primavera", "U17", "U18", "U19",
            "U20", "U21", "U23", "U-17", "U-18", "U-19", "U-20", "U-21", "U-23",
            "Sub 17", "Sub 20", "Sub-17", "Sub-20"
        };
        return !excludedTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static int? ParseRemaining(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static ApiFootballLeagueSeason ParseLeagueSeason(JsonElement root, int leagueId, int season)
    {
        var rows = root.GetProperty("response");
        if (rows.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"League {leagueId} season {season} was not returned by API-Football.");
        }

        var row = rows[0];
        var league = row.GetProperty("league");
        var country = row.GetProperty("country");
        var seasonNode = row.GetProperty("seasons").EnumerateArray()
            .FirstOrDefault(item => ApiFootballClient.ReadInt(item, "year") == season);
        if (seasonNode.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Coverage for league {leagueId} season {season} is unavailable.");
        }

        var coverage = seasonNode.GetProperty("coverage");
        var fixtureCoverage = coverage.GetProperty("fixtures");
        return new ApiFootballLeagueSeason(
            leagueId,
            ApiFootballClient.ReadString(league, "name") ?? $"League {leagueId}",
            ApiFootballClient.ReadString(country, "name") ?? string.Empty,
            ApiFootballClient.ReadString(league, "type") ?? string.Empty,
            season,
            ReadBool(seasonNode, "current"),
            ReadBool(fixtureCoverage, "events"),
            ReadBool(fixtureCoverage, "lineups"),
            ReadBool(fixtureCoverage, "statistics_fixtures"),
            ReadBool(fixtureCoverage, "statistics_players"),
            ReadBool(coverage, "standings"),
            ReadBool(coverage, "predictions"),
            ReadBool(coverage, "odds"));
    }

    private static IReadOnlyList<ApiFootballFixture> ParseFixtures(JsonElement root)
    {
        var result = new List<ApiFootballFixture>();
        foreach (var row in root.GetProperty("response").EnumerateArray())
        {
            var fixture = row.GetProperty("fixture");
            var status = fixture.GetProperty("status");
            var league = row.GetProperty("league");
            var teams = row.GetProperty("teams");
            var home = teams.GetProperty("home");
            var away = teams.GetProperty("away");
            var goals = row.GetProperty("goals");
            var score = row.GetProperty("score");
            var halfTime = score.GetProperty("halftime");
            var venue = fixture.GetProperty("venue");
            var dateText = ApiFootballClient.ReadString(fixture, "date") ??
                throw new InvalidOperationException("Fixture date is missing.");
            result.Add(new ApiFootballFixture(
                fixture.GetProperty("id").GetInt64(),
                DateTimeOffset.Parse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ApiFootballClient.ReadString(status, "short") ?? string.Empty,
                ApiFootballClient.ReadString(league, "round") ?? string.Empty,
                ApiFootballClient.ReadString(fixture, "referee"),
                ApiFootballClient.ReadString(venue, "name"),
                ApiFootballClient.ReadString(venue, "city"),
                ApiFootballClient.ReadInt(league, "id") ?? 0,
                ApiFootballClient.ReadString(league, "name") ?? string.Empty,
                ApiFootballClient.ReadString(league, "country") ?? string.Empty,
                ApiFootballClient.ReadInt(league, "season") ?? 0,
                ApiFootballClient.ReadInt(home, "id") ?? 0,
                ApiFootballClient.ReadString(home, "name") ?? string.Empty,
                ApiFootballClient.ReadString(home, "logo"),
                ApiFootballClient.ReadInt(away, "id") ?? 0,
                ApiFootballClient.ReadString(away, "name") ?? string.Empty,
                ApiFootballClient.ReadString(away, "logo"),
                ApiFootballClient.ReadInt(goals, "home"),
                ApiFootballClient.ReadInt(goals, "away"),
                ApiFootballClient.ReadInt(halfTime, "home"),
                ApiFootballClient.ReadInt(halfTime, "away")));
        }
        return result;
    }

    private static void ParseStatistics(JsonElement root, ApiFootballMatchData data)
    {
        foreach (var row in root.GetProperty("response").EnumerateArray())
        {
            var teamId = ApiFootballClient.ReadInt(row.GetProperty("team"), "id");
            var isHome = teamId == data.Fixture.HomeTeamId;
            var isAway = teamId == data.Fixture.AwayTeamId;
            if (!isHome && !isAway)
            {
                continue;
            }

            foreach (var statistic in row.GetProperty("statistics").EnumerateArray())
            {
                var type = ApiFootballClient.ReadString(statistic, "type") ?? string.Empty;
                var value = statistic.GetProperty("value");
                switch (type)
                {
                    case "Corner Kicks": SetInt(isHome, ReadInt(value), v => data.HomeCorners = v, v => data.AwayCorners = v); break;
                    case "Total Shots": SetInt(isHome, ReadInt(value), v => data.HomeShots = v, v => data.AwayShots = v); break;
                    case "Shots on Goal": SetInt(isHome, ReadInt(value), v => data.HomeShotsOnGoal = v, v => data.AwayShotsOnGoal = v); break;
                    case "Ball Possession": SetDecimal(isHome, ReadDecimal(value), v => data.HomePossession = v, v => data.AwayPossession = v); break;
                    case "Fouls": SetInt(isHome, ReadInt(value), v => data.HomeFouls = v, v => data.AwayFouls = v); break;
                    case "Offsides": SetInt(isHome, ReadInt(value), v => data.HomeOffsides = v, v => data.AwayOffsides = v); break;
                    case "Yellow Cards": SetInt(isHome, ReadInt(value), v => data.HomeYellowCards = v, v => data.AwayYellowCards = v); break;
                    case "Red Cards": SetInt(isHome, ReadInt(value), v => data.HomeRedCards = v, v => data.AwayRedCards = v); break;
                    case "Total passes": SetInt(isHome, ReadInt(value), v => data.HomeTotalPasses = v, v => data.AwayTotalPasses = v); break;
                    case "Passes %": SetDecimal(isHome, ReadDecimal(value), v => data.HomePassAccuracy = v, v => data.AwayPassAccuracy = v); break;
                }
            }
        }
    }

    private static void ParseLineups(JsonElement root, ApiFootballMatchData data)
    {
        foreach (var row in root.GetProperty("response").EnumerateArray())
        {
            var teamId = ApiFootballClient.ReadInt(row.GetProperty("team"), "id");
            var formation = ApiFootballClient.ReadString(row, "formation");
            if (teamId == data.Fixture.HomeTeamId)
            {
                data.HomeFormation = formation;
            }
            else if (teamId == data.Fixture.AwayTeamId)
            {
                data.AwayFormation = formation;
            }
        }
    }

    private static IReadOnlyList<ApiFootballStanding> ParseStandings(JsonElement root)
    {
        var result = new List<ApiFootballStanding>();
        foreach (var responseRow in root.GetProperty("response").EnumerateArray())
        {
            var league = responseRow.GetProperty("league");
            foreach (var group in league.GetProperty("standings").EnumerateArray())
            {
                foreach (var row in group.EnumerateArray())
                {
                    var team = row.GetProperty("team");
                    var all = row.GetProperty("all");
                    var goals = all.GetProperty("goals");
                    result.Add(new ApiFootballStanding(
                        ApiFootballClient.ReadString(row, "group") ?? string.Empty,
                        ApiFootballClient.ReadInt(team, "id") ?? 0,
                        ApiFootballClient.ReadString(team, "name") ?? string.Empty,
                        ApiFootballClient.ReadInt(row, "rank") ?? 0,
                        ApiFootballClient.ReadInt(row, "points"),
                        ApiFootballClient.ReadInt(row, "goalsDiff"),
                        ApiFootballClient.ReadInt(all, "played"),
                        ApiFootballClient.ReadInt(all, "win"),
                        ApiFootballClient.ReadInt(all, "draw"),
                        ApiFootballClient.ReadInt(all, "lose"),
                        ApiFootballClient.ReadInt(goals, "for"),
                        ApiFootballClient.ReadInt(goals, "against"),
                        ApiFootballClient.ReadString(row, "form"),
                        ApiFootballClient.ReadString(row, "description")));
                }
            }
        }
        return result;
    }

    private static string ResolveDefaultDbLeague(ApiFootballLeagueSeason league) =>
        (league.Country, league.LeagueName) switch
        {
            ("Brazil", "Serie A") => "Brasileirão",
            ("Brazil", "Serie B") => "Brasileirão Serie B",
            ("Brazil", "Serie C") => "Brasileirão Serie C",
            ("Brazil", "Serie D") => "Brasileirão Serie D",
            ("Chile", "Primera División") => "Liga de Primera",
            ("Argentina", "Liga Profesional Argentina") => "Liga Profesional Argentina",
            ("Argentina", "Primera Nacional") => "Argentine Nacional B",
            ("Argentina", "Copa Argentina") => "Copa Argentina",
            ("Bolivia", "Primera División") => "Bolivian Liga Profesional",
            ("Belgium", "Jupiler Pro League") => "Belgian Pro League",
            ("China", "Super League") => "Chinese Super League",
            ("Denmark", "Superliga") => "Danish Superliga",
            ("Ecuador", "Liga Pro") => "LigaPro Ecuador",
            ("England", "Premier League") => "Premier League",
            ("England", "Championship") => "English League Championship",
            ("England", "League One") => "English League One",
            ("England", "League Two") => "English League Two",
            ("France", "Ligue 1") => "Ligue 1",
            ("France", "Ligue 2") => "Ligue 2",
            ("Germany", "Bundesliga") => "Bundesliga",
            ("Italy", "Serie A") => "Serie A",
            ("Italy", "Serie B") => "Italian Serie B",
            ("Japan", "J1 League") => "J1 League",
            ("Mexico", "Liga MX") => "Liga MX",
            ("Netherlands", "Eredivisie") => "Eredivisie",
            ("Netherlands", "Eerste Divisie") => "Eerste Divisie",
            ("Norway", "Eliteserien") => "Eliteserien",
            ("Paraguay", "Division Profesional - Apertura") => "Paraguayan Primera División",
            ("Peru", "Primera División") => "Liga 1 Peru",
            ("Portugal", "Primeira Liga") => "Primeira Liga",
            ("Scotland", "Premiership") => "Scottish Premiership",
            ("Spain", "Segunda División") => "Spanish LALIGA 2",
            ("Spain", "La Liga") => "La Liga",
            ("Sweden", "Allsvenskan") => "Allsvenskan",
            ("Switzerland", "Super League") => "Super League (Switzerland)",
            ("Turkey", "Süper Lig") => "Turkish Super Lig",
            ("Austria", "Bundesliga") => "Austrian Bundesliga",
            ("USA", "Major League Soccer") => "MLS",
            ("USA", "USL Championship") => "USL Championship",
            ("USA", "USL League One") => "USL League One",
            ("World", "World Cup") => "Copa del Mundo",
            ("World", "UEFA Champions League") => "UEFA Champions League",
            ("World", "UEFA Europa League") => "UEFA Europa League",
            ("World", "UEFA Europa Conference League") => "UEFA Conference League",
            ("World", "CONMEBOL Libertadores") => "Copa Libertadores",
            ("World", "CONMEBOL Sudamericana") => "Copa Sudamericana",
            _ => $"{league.LeagueName} ({league.Country})"
        };

    private static bool IsKnockout(ApiFootballLeagueSeason league, string round) =>
        league.CompetitionType.Equals("Cup", StringComparison.OrdinalIgnoreCase) &&
        !round.Contains("Group", StringComparison.OrdinalIgnoreCase) &&
        !round.Contains("Regular", StringComparison.OrdinalIgnoreCase);

    private static ApiFootballSyncRow ToRow(
        ApiFootballFixture fixture,
        string status,
        string message,
        long? matchHistoryId = null) =>
        new(
            fixture.FixtureId,
            DateOnly.FromDateTime(fixture.Date.UtcDateTime),
            fixture.HomeTeam,
            fixture.AwayTeam,
            status,
            message,
            matchHistoryId);

    private static bool ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? ReadInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
        {
            return result;
        }
        return int.TryParse(NormalizeNumeric(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            ? result
            : null;
    }

    private static decimal? ReadDecimal(JsonElement value) =>
        decimal.TryParse(NormalizeNumeric(value), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static string NormalizeNumeric(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
            ? string.Empty
            : value.ToString().Replace("%", string.Empty, StringComparison.Ordinal).Trim();

    private static void SetInt(bool isHome, int? value, Action<int?> home, Action<int?> away)
    {
        if (isHome) home(value); else away(value);
    }

    private static void SetDecimal(bool isHome, decimal? value, Action<decimal?> home, Action<decimal?> away)
    {
        if (isHome) home(value); else away(value);
    }
}
