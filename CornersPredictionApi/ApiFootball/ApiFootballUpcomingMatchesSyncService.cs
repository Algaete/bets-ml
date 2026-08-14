using CornersMLData.Data;
using CornersMLData.Models;
using CornersPredictionApi.CompetitionFiltering;

namespace CornersPredictionApi.ApiFootball;

/// <summary>
/// Imports scheduled senior-men fixtures from API-Football into the operational
/// table consumed by the odds collectors, bots and upcoming-matches page.
/// </summary>
public sealed class ApiFootballUpcomingMatchesSyncService
{
    private static readonly HashSet<string> ScheduledStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "NS",
        "TBD"
    };

    private readonly ApiFootballClient _client;
    private readonly PartidosProximosRepository _repository;
    private readonly CompetitionEligibilityPolicy _competitionPolicy;
    private readonly ILogger<ApiFootballUpcomingMatchesSyncService> _logger;

    public ApiFootballUpcomingMatchesSyncService(
        ApiFootballClient client,
        PartidosProximosRepository repository,
        CompetitionEligibilityPolicy competitionPolicy,
        ILogger<ApiFootballUpcomingMatchesSyncService> logger)
    {
        _client = client;
        _repository = repository;
        _competitionPolicy = competitionPolicy;
        _logger = logger;
    }

    public async Task<ApiFootballUpcomingSyncResult> SyncAsync(
        ApiFootballUpcomingSyncRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await _repository.EnsureDatabaseObjectsAsync(cancellationToken);

        var fixtureById = new Dictionary<long, ApiFootballFixture>();
        for (var date = request.DateFrom; date <= request.DateTo; date = date.AddDays(1))
        {
            var dailyFixtures = ApiFootballSyncService.ParseFixtures(
                await _client.GetUpcomingFixturesForDateAsync(date, cancellationToken));
            foreach (var fixture in dailyFixtures)
            {
                fixtureById[fixture.FixtureId] = fixture;
            }
        }

        var fixtures = fixtureById.Values
            .Where(fixture => ScheduledStatuses.Contains(fixture.Status))
            .OrderBy(fixture => fixture.Date)
            .ToArray();

        var upcoming = new List<PartidoProximoUpsertDto>(fixtures.Length);
        var excluded = 0;
        foreach (var fixture in fixtures)
        {
            var dbLeague = ApiFootballSyncService.ApiFootballLeagueNameMapper.Resolve(
                fixture.Country,
                fixture.LeagueName);
            var decision = _competitionPolicy.Evaluate(
                $"{fixture.Country} {fixture.LeagueName}",
                $"{dbLeague} {fixture.Round}",
                "M");
            if (!decision.IsEligible)
            {
                excluded++;
                _logger.LogDebug(
                    "Upcoming API-Football fixture excluded. FixtureId={FixtureId}, League={League}, Reason={Reason}",
                    fixture.FixtureId,
                    fixture.LeagueName,
                    decision.Reason);
                continue;
            }

            upcoming.Add(new PartidoProximoUpsertDto
            {
                FechaPartido = fixture.Date.DateTime,
                EquipoLocal = fixture.HomeTeam,
                EquipoVisita = fixture.AwayTeam,
                Liga = dbLeague,
                Genero = "M",
                EsKnockout = IsKnockout(fixture.LeagueName, fixture.Round),
                DataSource = "API-Football",
                ExternalFixtureId = fixture.FixtureId,
                FixtureStatus = fixture.Status
            });
        }

        // Ranking enrichment is intentionally deferred. Resolving it fixture by
        // fixture adds many Azure SQL round trips and blocks the scheduling feed.
        var persisted = await _repository.SincronizarAsync(
            upcoming,
            new PartidosProximosSyncOptions(
                EnrichPositions: false,
                NormalizeAliases: false),
            cancellationToken);
        var daily = upcoming
            .GroupBy(match => DateOnly.FromDateTime(match.FechaPartido))
            .OrderBy(group => group.Key)
            .Select(group => new ApiFootballUpcomingDailySummary(group.Key, group.Count()))
            .ToArray();

        return new ApiFootballUpcomingSyncResult(
            request.DateFrom,
            request.DateTo,
            fixtures.Length,
            upcoming.Count,
            excluded,
            persisted,
            _client.DailyRemaining,
            _client.MinuteRemaining,
            daily);
    }

    private static void Validate(ApiFootballUpcomingSyncRequest request)
    {
        if (request.DateFrom == default || request.DateTo == default)
            throw new ArgumentException("DateFrom and DateTo are required.");
        if (request.DateTo < request.DateFrom)
            throw new ArgumentException("DateTo must be on or after DateFrom.");
        if (request.DateTo.DayNumber - request.DateFrom.DayNumber > 30)
            throw new ArgumentException("The maximum upcoming-fixture range is 31 days.");
    }

    private static bool IsKnockout(string league, string round)
    {
        if (round.Contains("Group", StringComparison.OrdinalIgnoreCase)
            || round.Contains("League Phase", StringComparison.OrdinalIgnoreCase)
            || round.Contains("Regular", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return league.Contains("Cup", StringComparison.OrdinalIgnoreCase)
            || league.Contains("Copa", StringComparison.OrdinalIgnoreCase)
            || league.Contains("Champions League", StringComparison.OrdinalIgnoreCase)
            || league.Contains("Europa League", StringComparison.OrdinalIgnoreCase)
            || league.Contains("Conference League", StringComparison.OrdinalIgnoreCase)
            || league.Contains("Libertadores", StringComparison.OrdinalIgnoreCase)
            || league.Contains("Sudamericana", StringComparison.OrdinalIgnoreCase)
            || round.Contains("Final", StringComparison.OrdinalIgnoreCase)
            || round.Contains("Quarter", StringComparison.OrdinalIgnoreCase)
            || round.Contains("Semi", StringComparison.OrdinalIgnoreCase)
            || round.Contains("Round of", StringComparison.OrdinalIgnoreCase)
            || round.Contains("Qualif", StringComparison.OrdinalIgnoreCase)
            || round.Contains("Play-off", StringComparison.OrdinalIgnoreCase);
    }
}
