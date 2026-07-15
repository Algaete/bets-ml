using System.Globalization;
using System.Text.Json;

namespace ApiFootballTest;

internal static class Program
{
    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "AET", "PEN"
    };

    public static async Task<int> Main()
    {
        var apiKey = Environment.GetEnvironmentVariable("API_FOOTBALL_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("API_FOOTBALL_KEY is not configured.");
            return 2;
        }

        var delayMs = ReadIntEnvironment("API_FOOTBALL_DELAY_MS", 6500, 0, 60000);
        var sampleSize = ReadIntEnvironment("API_FOOTBALL_SAMPLE_SIZE", 10, 1, 10);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        using var client = new ApiFootballClient(
            apiKey,
            TimeSpan.FromMilliseconds(delayMs),
            TimeSpan.FromSeconds(30));

        try
        {
            Console.WriteLine("=== CONFIGURATION ===");
            Console.WriteLine("Base URL: https://v3.football.api-sports.io");
            Console.WriteLine("API key: configured (value hidden)");
            Console.WriteLine($"Sample size: {sampleSize}");
            Console.WriteLine($"Delay between requests: {delayMs} ms");

            var allLeaguesRoot = await client.GetAsync("/leagues?country=Chile", cancellationSource.Token);
            var leagues = ParseLeagues(allLeaguesRoot);
            IReadOnlyList<LeagueInfo> currentLeagues = Array.Empty<LeagueInfo>();
            int? freePlanMaximumSeason = null;
            try
            {
                var currentLeaguesRoot = await client.GetAsync(
                    "/leagues?country=Chile&season=2026",
                    cancellationSource.Token);
                currentLeagues = ParseLeagues(currentLeaguesRoot);
            }
            catch (ApiFootballException exception) when (
                exception.Message.Contains("2022 to 2024", StringComparison.OrdinalIgnoreCase))
            {
                freePlanMaximumSeason = 2024;
                Console.WriteLine($"Season 2026 access: unavailable ({exception.Message})");
            }

            Console.WriteLine("\n=== CHILEAN LEAGUES ===");
            foreach (var league in leagues.OrderBy(item => item.Name))
            {
                var years = string.Join(",", league.Seasons.Select(season => season.Year));
                var current = league.Seasons.FirstOrDefault(season => season.Current)?.Year.ToString() ?? "none";
                Console.WriteLine($"{league.Id} | {league.Name} | {league.Type} | seasons={years} | current={current}");
            }
            Console.WriteLine($"Chile leagues returned for season 2026: {currentLeagues.Count}");

            var copaChile = leagues.FirstOrDefault(league =>
                league.Name.Contains("Copa Chile", StringComparison.OrdinalIgnoreCase));
            if (copaChile is null)
            {
                Console.WriteLine("Copa Chile was not returned by the API.");
                return 3;
            }

            var accessibleSeasons = freePlanMaximumSeason.HasValue
                ? copaChile.Seasons.Where(season => season.Year <= freePlanMaximumSeason.Value)
                : copaChile.Seasons;
            var selectedSeason = accessibleSeasons
                .OrderByDescending(season => season.Current)
                .ThenByDescending(season => season.Year)
                .First();

            PrintCoverage(copaChile, selectedSeason);

            var teamsRoot = await client.GetAsync(
                $"/teams?league={copaChile.Id}&season={selectedSeason.Year}",
                cancellationSource.Token);
            var teams = ParseTeams(teamsRoot);
            Console.WriteLine("\n=== COPA CHILE TEAMS ===");
            Console.WriteLine($"Total teams: {teams.Count}");
            foreach (var team in teams.OrderBy(team => team.Name))
            {
                Console.WriteLine($"{team.Id} | {team.Name} | {team.Country} | founded={team.Founded?.ToString() ?? "n/a"} | venue={team.Venue ?? "n/a"}");
            }

            var fixturesRoot = await client.GetAsync(
                $"/fixtures?league={copaChile.Id}&season={selectedSeason.Year}",
                cancellationSource.Token);
            var allFixtures = ParseFixtures(fixturesRoot);
            var recentFinished = allFixtures
                .Where(fixture => FinishedStatuses.Contains(fixture.Status))
                .OrderByDescending(fixture => fixture.Date)
                .Take(sampleSize)
                .ToArray();

            Console.WriteLine("\n=== RECENT FINISHED FIXTURES ===");
            foreach (var fixture in recentFinished)
            {
                Console.WriteLine($"{fixture.Id} | {fixture.Date:yyyy-MM-dd} | {fixture.Status} | {fixture.League} {fixture.Season} | {fixture.Round} | {fixture.HomeTeam} {fixture.HomeGoals}-{fixture.AwayGoals} {fixture.AwayTeam}");
            }

            var probes = new List<FixtureProbe>();
            var allStatisticTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("\n=== FIXTURE STATISTICS ===");
            foreach (var fixture in recentFinished)
            {
                try
                {
                    var statisticsRoot = await client.GetAsync(
                        $"/fixtures/statistics?fixture={fixture.Id}",
                        cancellationSource.Token);
                    var statistics = ParseStatistics(statisticsRoot);
                    foreach (var type in statistics.AllTypes)
                    {
                        allStatisticTypes.Add(type);
                    }

                    var lineupsRoot = await client.GetAsync(
                        $"/fixtures/lineups?fixture={fixture.Id}",
                        cancellationSource.Token);
                    var lineups = ParseLineups(lineupsRoot);

                    var candidate = BuildCandidate(fixture, statistics, lineups);
                    var validation = MatchHistoryValidator.Validate(candidate);
                    probes.Add(new FixtureProbe(
                        fixture,
                        candidate,
                        statistics.HasRows,
                        lineups.HasRows,
                        statistics.AllTypes,
                        validation));

                    Console.WriteLine(
                        $"{fixture.Id} | corners={FormatPair(candidate.HomeCorners, candidate.AwayCorners)} | " +
                        $"shots={FormatPair(candidate.HomeShots, candidate.AwayShots)} | " +
                        $"SOG={FormatPair(candidate.HomeShotsOnGoal, candidate.AwayShotsOnGoal)} | " +
                        $"possession={FormatPair(candidate.HomePossession, candidate.AwayPossession)}");
                }
                catch (Exception exception) when (exception is ApiFootballException or HttpRequestException or TaskCanceledException)
                {
                    var candidate = BuildEmptyCandidate(fixture);
                    var validation = MatchHistoryValidator.Validate(candidate);
                    probes.Add(new FixtureProbe(
                        fixture,
                        candidate,
                        false,
                        false,
                        new HashSet<string>(),
                        new ValidationResult(false, validation.Reasons.Append(exception.Message).ToArray())));
                    Console.WriteLine($"{fixture.Id} | ERROR: {exception.Message}");
                }
            }

            Console.WriteLine("\n=== AVAILABLE STATISTIC TYPES ===");
            Console.WriteLine(allStatisticTypes.Count == 0
                ? "No statistic types were returned."
                : string.Join(" | ", allStatisticTypes));

            Console.WriteLine("\n=== LINEUPS AND FORMATIONS ===");
            foreach (var probe in probes)
            {
                Console.WriteLine($"{probe.Fixture.Id} | {probe.Fixture.HomeTeam}={probe.Candidate.HomeFormation ?? "null"} | {probe.Fixture.AwayTeam}={probe.Candidate.AwayFormation ?? "null"}");
            }

            await PrintEventsSampleAsync(client, recentFinished.Take(2), cancellationSource.Token);
            await PrintPlayerStatisticsSampleAsync(client, recentFinished.FirstOrDefault(), cancellationSource.Token);

            var futureFixture = allFixtures
                .Where(fixture => !FinishedStatuses.Contains(fixture.Status) && fixture.Date >= DateTimeOffset.UtcNow)
                .OrderBy(fixture => fixture.Date)
                .FirstOrDefault();
            await PrintOddsAndPredictionsAsync(
                client,
                futureFixture,
                recentFinished.FirstOrDefault(),
                cancellationSource.Token);

            PrintValidation(probes);
            PrintSummary(client, probes);
            return 0;
        }
        catch (Exception exception) when (exception is ApiFootballException or HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"Test stopped: {exception.Message}");
            Console.Error.WriteLine($"Requests made: {client.NetworkRequestCount}; daily remaining: {client.RequestsRemaining ?? "unknown"}");
            return 1;
        }
    }

    private static void PrintCoverage(LeagueInfo league, LeagueSeason season)
    {
        Console.WriteLine("\n=== COPA CHILE COVERAGE ===");
        Console.WriteLine($"League ID: {league.Id}");
        Console.WriteLine($"League name: {league.Name}");
        Console.WriteLine($"Season: {season.Year}");
        Console.WriteLine($"Current season: {season.Current}");
        Console.WriteLine($"Fixtures statistics available: {season.Coverage.FixtureStatistics}");
        Console.WriteLine($"Player statistics available: {season.Coverage.PlayerStatistics}");
        Console.WriteLine($"Lineups available: {season.Coverage.Lineups}");
        Console.WriteLine($"Events available: {season.Coverage.Events}");
        Console.WriteLine($"Odds available: {season.Coverage.Odds}");
        Console.WriteLine($"Predictions available: {season.Coverage.Predictions}");
    }

    private static async Task PrintEventsSampleAsync(
        ApiFootballClient client,
        IEnumerable<FixtureInfo> fixtures,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== EVENTS SAMPLE ===");
        foreach (var fixture in fixtures)
        {
            try
            {
                var root = await client.GetAsync($"/fixtures/events?fixture={fixture.Id}", cancellationToken);
                var response = root.GetProperty("response");
                var types = response.EnumerateArray()
                    .Select(item => ReadString(item, "type"))
                    .Where(value => value is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Console.WriteLine($"{fixture.Id} | events={response.GetArrayLength()} | types={string.Join(", ", types!)} | fields=time, team, player, assist, type, detail, comments");
            }
            catch (Exception exception) when (exception is ApiFootballException or HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"{fixture.Id} | ERROR: {exception.Message}");
            }
        }
    }

    private static async Task PrintPlayerStatisticsSampleAsync(
        ApiFootballClient client,
        FixtureInfo? fixture,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== PLAYER STATISTICS SAMPLE ===");
        if (fixture is null)
        {
            Console.WriteLine("No finished fixture available.");
            return;
        }

        try
        {
            var root = await client.GetAsync($"/fixtures/players?fixture={fixture.Id}", cancellationToken);
            Console.WriteLine($"{fixture.Id} | team rows={root.GetProperty("response").GetArrayLength()}");
        }
        catch (Exception exception) when (exception is ApiFootballException or HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"{fixture.Id} | ERROR: {exception.Message}");
        }
    }

    private static async Task PrintOddsAndPredictionsAsync(
        ApiFootballClient client,
        FixtureInfo? futureFixture,
        FixtureInfo? historicalFixture,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== ODDS SAMPLE ===");
        var oddsFixture = futureFixture ?? historicalFixture;
        if (oddsFixture is null)
        {
            Console.WriteLine("No Copa Chile fixture was available.");
        }
        else
        {
            try
            {
                var root = await client.GetAsync($"/odds?fixture={oddsFixture.Id}", cancellationToken);
                var bookmakers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var markets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var cornerValues = new List<string>();
                foreach (var row in root.GetProperty("response").EnumerateArray())
                {
                    if (!row.TryGetProperty("bookmakers", out var bookmakerRows))
                    {
                        continue;
                    }

                    foreach (var bookmaker in bookmakerRows.EnumerateArray())
                    {
                        var bookmakerName = ReadString(bookmaker, "name") ?? "unknown";
                        bookmakers.Add(bookmakerName);
                        if (!bookmaker.TryGetProperty("bets", out var bets))
                        {
                            continue;
                        }

                        foreach (var bet in bets.EnumerateArray())
                        {
                            var marketName = ReadString(bet, "name") ?? "unknown";
                            markets.Add(marketName);
                            if (!marketName.Contains("corner", StringComparison.OrdinalIgnoreCase) ||
                                !bet.TryGetProperty("values", out var values))
                            {
                                continue;
                            }

                            foreach (var value in values.EnumerateArray())
                            {
                                cornerValues.Add($"{bookmakerName} | {marketName} | {ReadString(value, "value")} | {ReadString(value, "odd")}");
                            }
                        }
                    }
                }

                Console.WriteLine($"Fixture: {oddsFixture.Id} | {oddsFixture.HomeTeam} vs {oddsFixture.AwayTeam} | {oddsFixture.Date:O}");
                Console.WriteLine($"Fixture kind: {(futureFixture is null ? "historical fallback" : "future")}");
                Console.WriteLine($"Bookmakers: {(bookmakers.Count == 0 ? "none" : string.Join(", ", bookmakers))}");
                Console.WriteLine($"Markets: {(markets.Count == 0 ? "none" : string.Join(", ", markets))}");
                Console.WriteLine($"Corner markets: {(cornerValues.Count == 0 ? "none" : string.Join(" || ", cornerValues))}");
            }
            catch (Exception exception) when (exception is ApiFootballException or HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"{oddsFixture.Id} | ERROR: {exception.Message}");
            }
        }

        Console.WriteLine("\n=== PREDICTIONS SAMPLE ===");
        var predictionFixture = futureFixture ?? historicalFixture;
        if (predictionFixture is null)
        {
            Console.WriteLine("No Copa Chile fixture was available.");
            return;
        }

        try
        {
            var root = await client.GetAsync($"/predictions?fixture={predictionFixture.Id}", cancellationToken);
            Console.WriteLine($"{predictionFixture.Id} | prediction rows={root.GetProperty("response").GetArrayLength()}");
        }
        catch (Exception exception) when (exception is ApiFootballException or HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"{predictionFixture.Id} | ERROR: {exception.Message}");
        }
    }

    private static void PrintValidation(IReadOnlyCollection<FixtureProbe> probes)
    {
        Console.WriteLine("\n=== MATCHHISTORY VALIDATION ===");
        Console.WriteLine("FixtureId | Match | Corners | Shots | ShotsOnGoal | Possession | Formations | Valid");
        foreach (var probe in probes)
        {
            var candidate = probe.Candidate;
            Console.WriteLine(
                $"{probe.Fixture.Id} | {probe.Fixture.HomeTeam} vs {probe.Fixture.AwayTeam} | " +
                $"{FormatPair(candidate.HomeCorners, candidate.AwayCorners)} | " +
                $"{FormatPair(candidate.HomeShots, candidate.AwayShots)} | " +
                $"{FormatPair(candidate.HomeShotsOnGoal, candidate.AwayShotsOnGoal)} | " +
                $"{FormatPair(candidate.HomePossession, candidate.AwayPossession)} | " +
                $"{(candidate.HomeFormation is not null && candidate.AwayFormation is not null ? "both" : "incomplete")} | " +
                $"{probe.Validation.IsValid}");
        }

        Console.WriteLine("\nDiscarded fixtures:");
        foreach (var probe in probes.Where(probe => !probe.Validation.IsValid))
        {
            Console.WriteLine($"{probe.Fixture.Id} | {probe.Fixture.HomeTeam} vs {probe.Fixture.AwayTeam} | {string.Join("; ", probe.Validation.Reasons)}");
        }
    }

    private static void PrintSummary(ApiFootballClient client, IReadOnlyCollection<FixtureProbe> probes)
    {
        var withStatistics = probes.Count(probe => probe.HasStatistics);
        var withLineups = probes.Count(probe => probe.HasLineups);
        var withBothFormations = probes.Count(probe =>
            probe.Candidate.HomeFormation is not null && probe.Candidate.AwayFormation is not null);
        var valid = probes.Count(probe => probe.Validation.IsValid);
        var percentage = probes.Count == 0 ? 0 : valid * 100d / probes.Count;

        Console.WriteLine("\n=== FINAL SUMMARY ===");
        Console.WriteLine($"Total fixtures reviewed: {probes.Count}");
        Console.WriteLine($"Fixtures with statistics: {withStatistics}");
        Console.WriteLine($"Fixtures with lineups: {withLineups}");
        Console.WriteLine($"Fixtures with both formations: {withBothFormations}");
        Console.WriteLine($"Valid for MatchHistory: {valid}");
        Console.WriteLine($"Discarded: {probes.Count - valid}");
        Console.WriteLine($"Valid percentage: {percentage:0.##}%");
        Console.WriteLine($"Network requests made: {client.NetworkRequestCount}");
        Console.WriteLine($"Daily requests remaining: {client.RequestsRemaining ?? "unknown"}");
        Console.WriteLine($"Minute requests remaining: {client.MinuteRequestsRemaining ?? "unknown"}");
    }

    private static IReadOnlyList<LeagueInfo> ParseLeagues(JsonElement root)
    {
        return root.GetProperty("response").EnumerateArray().Select(row =>
        {
            var league = row.GetProperty("league");
            var country = row.GetProperty("country");
            var seasons = row.GetProperty("seasons").EnumerateArray().Select(season =>
            {
                var coverage = season.GetProperty("coverage");
                var fixtures = coverage.GetProperty("fixtures");
                return new LeagueSeason(
                    season.GetProperty("year").GetInt32(),
                    ReadDateOnly(season, "start"),
                    ReadDateOnly(season, "end"),
                    ReadBoolean(season, "current"),
                    new Coverage(
                        ReadBoolean(fixtures, "events"),
                        ReadBoolean(fixtures, "lineups"),
                        ReadBoolean(fixtures, "statistics_fixtures"),
                        ReadBoolean(fixtures, "statistics_players"),
                        ReadBoolean(coverage, "predictions"),
                        ReadBoolean(coverage, "odds")));
            }).ToArray();

            return new LeagueInfo(
                league.GetProperty("id").GetInt32(),
                league.GetProperty("name").GetString() ?? "unknown",
                league.GetProperty("type").GetString() ?? "unknown",
                country.GetProperty("name").GetString() ?? "unknown",
                seasons);
        }).ToArray();
    }

    private static IReadOnlyList<TeamInfo> ParseTeams(JsonElement root)
    {
        return root.GetProperty("response").EnumerateArray().Select(row =>
        {
            var team = row.GetProperty("team");
            var venue = row.GetProperty("venue");
            return new TeamInfo(
                team.GetProperty("id").GetInt32(),
                team.GetProperty("name").GetString() ?? "unknown",
                ReadString(team, "country") ?? "unknown",
                ReadNullableInt(team, "founded"),
                ReadString(venue, "name"));
        }).ToArray();
    }

    private static IReadOnlyList<FixtureInfo> ParseFixtures(JsonElement root)
    {
        return root.GetProperty("response").EnumerateArray().Select(row =>
        {
            var fixture = row.GetProperty("fixture");
            var league = row.GetProperty("league");
            var teams = row.GetProperty("teams");
            var home = teams.GetProperty("home");
            var away = teams.GetProperty("away");
            var goals = row.GetProperty("goals");
            return new FixtureInfo(
                fixture.GetProperty("id").GetInt64(),
                DateTimeOffset.Parse(fixture.GetProperty("date").GetString()!, CultureInfo.InvariantCulture),
                fixture.GetProperty("status").GetProperty("short").GetString() ?? "unknown",
                league.GetProperty("name").GetString() ?? "unknown",
                league.GetProperty("season").GetInt32(),
                ReadString(league, "round") ?? "unknown",
                home.GetProperty("id").GetInt32(),
                home.GetProperty("name").GetString() ?? "unknown",
                away.GetProperty("id").GetInt32(),
                away.GetProperty("name").GetString() ?? "unknown",
                ReadNullableInt(goals, "home"),
                ReadNullableInt(goals, "away"));
        }).ToArray();
    }

    private static FixtureStatistics ParseStatistics(JsonElement root)
    {
        var byTeam = new Dictionary<int, Dictionary<string, JsonElement>>();
        var allTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in root.GetProperty("response").EnumerateArray())
        {
            var teamId = row.GetProperty("team").GetProperty("id").GetInt32();
            var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var statistic in row.GetProperty("statistics").EnumerateArray())
            {
                var type = ReadString(statistic, "type");
                if (type is null)
                {
                    continue;
                }

                allTypes.Add(type);
                values[type] = statistic.GetProperty("value").Clone();
            }

            byTeam[teamId] = values;
        }

        return new FixtureStatistics(byTeam, allTypes);
    }

    private static FixtureLineups ParseLineups(JsonElement root)
    {
        var formations = new Dictionary<int, string?>();
        foreach (var row in root.GetProperty("response").EnumerateArray())
        {
            var teamId = row.GetProperty("team").GetProperty("id").GetInt32();
            formations[teamId] = ReadString(row, "formation");
        }

        return new FixtureLineups(formations);
    }

    private static MatchHistoryCandidate BuildCandidate(
        FixtureInfo fixture,
        FixtureStatistics statistics,
        FixtureLineups lineups)
    {
        return new MatchHistoryCandidate(
            DateOnly.FromDateTime(fixture.Date.UtcDateTime),
            fixture.HomeTeam,
            fixture.AwayTeam,
            lineups.Get(fixture.HomeTeamId),
            lineups.Get(fixture.AwayTeamId),
            fixture.HomeGoals,
            fixture.AwayGoals,
            statistics.GetInt(fixture.HomeTeamId, "Corner Kicks"),
            statistics.GetInt(fixture.AwayTeamId, "Corner Kicks"),
            statistics.GetInt(fixture.HomeTeamId, "Total Shots"),
            statistics.GetInt(fixture.AwayTeamId, "Total Shots"),
            statistics.GetInt(fixture.HomeTeamId, "Shots on Goal"),
            statistics.GetInt(fixture.AwayTeamId, "Shots on Goal"),
            statistics.GetDouble(fixture.HomeTeamId, "Ball Possession"),
            statistics.GetDouble(fixture.AwayTeamId, "Ball Possession"),
            $"api-football-{fixture.Id}");
    }

    private static MatchHistoryCandidate BuildEmptyCandidate(FixtureInfo fixture) =>
        new(
            DateOnly.FromDateTime(fixture.Date.UtcDateTime),
            fixture.HomeTeam,
            fixture.AwayTeam,
            null,
            null,
            fixture.HomeGoals,
            fixture.AwayGoals,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            $"api-football-{fixture.Id}");

    private static int ReadIntEnvironment(string name, int fallback, int minimum, int maximum)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.TryGetInt32(out var value) ? value : null;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        property.GetBoolean();

    private static DateOnly? ReadDateOnly(JsonElement element, string propertyName) =>
        DateOnly.TryParse(ReadString(element, propertyName), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;

    private static string FormatPair<T>(T? home, T? away) where T : struct =>
        $"{home?.ToString() ?? "null"}/{away?.ToString() ?? "null"}";

    private sealed record FixtureStatistics(
        IReadOnlyDictionary<int, Dictionary<string, JsonElement>> ByTeam,
        IReadOnlySet<string> AllTypes)
    {
        public bool HasRows => ByTeam.Count > 0;

        public int? GetInt(int teamId, string type)
        {
            var value = GetValue(teamId, type);
            if (value is null || value.Value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.Value.TryGetInt32(out var number))
            {
                return number;
            }

            return int.TryParse(value.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
        }

        public double? GetDouble(int teamId, string type)
        {
            var value = GetValue(teamId, type);
            if (value is null || value.Value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var text = value.Value.ToString().Trim().TrimEnd('%');
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        private JsonElement? GetValue(int teamId, string type) =>
            ByTeam.TryGetValue(teamId, out var statistics) && statistics.TryGetValue(type, out var value)
                ? value
                : null;
    }

    private sealed record FixtureLineups(IReadOnlyDictionary<int, string?> Formations)
    {
        public bool HasRows => Formations.Count > 0;
        public string? Get(int teamId) => Formations.TryGetValue(teamId, out var formation) ? formation : null;
    }
}
