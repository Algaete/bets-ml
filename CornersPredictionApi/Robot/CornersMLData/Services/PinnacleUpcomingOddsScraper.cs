using CornersMLData.Data;
using CornersMLData.Models;
using CornersPredictionApi.CompetitionFiltering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Services
{
    public sealed class PinnacleUpcomingOddsScraper
    {
        private const int SoccerSportId = 29;
        private const string DefaultApiBaseUrl = "https://guest.api.arcadia.pinnacle.com/";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly ILogger<PinnacleUpcomingOddsScraper> _logger;
        private readonly CompetitionEligibilityPolicy _competitionPolicy;
        private readonly string _apiBaseUrl;
        private readonly string _apiKey;
        private readonly int _parallelism;
        private readonly int _upcomingDays;

        public PinnacleUpcomingOddsScraper(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<PinnacleUpcomingOddsScraper> logger,
            CompetitionEligibilityPolicy competitionPolicy)
        {
            _httpClient = httpClient;
            _logger = logger;
            _competitionPolicy = competitionPolicy;
            _apiBaseUrl = NormalizeBaseUrl(
                configuration["PinnacleGuestApi:BaseUrl"] ?? DefaultApiBaseUrl);
            var configuredApiKey = configuration["PinnacleGuestApi:ApiKey"];
            if (string.IsNullOrWhiteSpace(configuredApiKey))
            {
                throw new InvalidOperationException(
                    "PinnacleGuestApi:ApiKey is required. Set PINNACLE_GUEST_API_KEY.");
            }

            _apiKey = configuredApiKey.Trim();
            _parallelism = Math.Clamp(
                configuration.GetValue("PinnacleGuestApi:Parallelism", 6),
                1,
                12);
            _upcomingDays = Math.Clamp(
                configuration.GetValue("PinnacleGuestApi:UpcomingDays", 7),
                1,
                14);
        }

        public async Task<PinnacleUpcomingFootballOddsResponse> ScrapeUpcomingFootballAsync(
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take <= 0 ? 100 : take, 1, 200);

            var matchups = await GetAsync<List<ArcadiaMatchup>>(
                $"0.1/sports/{SoccerSportId}/matchups?withSpecials=true",
                cancellationToken) ?? new List<ArcadiaMatchup>();

            var parents = matchups
                .Where(matchup => matchup.ParentId == null)
                .GroupBy(matchup => matchup.Id)
                .ToDictionary(group => group.Key, group => group.First());

            var minimumStart = DateTimeOffset.UtcNow.AddMinutes(-15);
            var maximumStart = DateTimeOffset.UtcNow.AddDays(_upcomingDays);
            var marketMatchupsByParent = matchups
                .Where(matchup => matchup.ParentId != null)
                .GroupBy(matchup => matchup.ParentId!.Value)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ArcadiaMatchup>)group.ToArray());
            var marketCandidates = parents.Values
                .Where(parent => !parent.IsLive && parent.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
                .Where(parent => parent.StartTime >= minimumStart && parent.StartTime <= maximumStart)
                .Select(parent => new PinnacleApiCandidate(
                    parent,
                    marketMatchupsByParent.GetValueOrDefault(parent.Id) ?? Array.Empty<ArcadiaMatchup>()))
                .Where(candidate => !IsWomenMatch(candidate.Parent))
                .ToArray();
            var candidates = marketCandidates
                .Where(candidate => _competitionPolicy.IsEligible(
                    candidate.Parent.League.Name,
                    candidate.Parent.League.Group))
                .OrderBy(candidate => candidate.Parent.StartTime)
                .ThenBy(candidate => candidate.Parent.League.Name)
                .ThenBy(candidate => candidate.Parent.Id)
                .ToArray();

            _logger.LogInformation(
                "Filtro de competiciones Pinnacle aplicado. MarketCandidates={MarketCandidates}, Included={Included}, Excluded={Excluded}",
                marketCandidates.Length,
                candidates.Length,
                marketCandidates.Length - candidates.Length);

            var selectedCandidates = candidates.Take(take).ToArray();
            using var semaphore = new SemaphoreSlim(_parallelism, _parallelism);
            var tasks = selectedCandidates.Select(async candidate =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ScrapeMatchAsync(candidate, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "No se pudo obtener mercados Pinnacle por API. MatchId={MatchId}",
                        candidate.Parent.Id);

                    var failedMatch = BuildMatchMetadata(candidate);
                    failedMatch.Notes.Add($"Error loading Pinnacle markets: {exception.Message}");
                    return failedMatch;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            return new PinnacleUpcomingFootballOddsResponse
            {
                Message = "Sincronizacion Pinnacle por API guest completada.",
                ScrapedAtUtc = DateTime.UtcNow,
                TotalDiscovered = candidates.Length,
                TotalProcessed = results.Length,
                TotalWithCornersTotal = results.Count(match => match.CornersTotal != null),
                TotalWithCornersHomeTeam = results.Count(match => match.CornersHomeTeam != null),
                TotalWithCornersAwayTeam = results.Count(match => match.CornersAwayTeam != null),
                TotalWithGoalsTotal = results.Count(match => match.GoalsTotal != null),
                TotalWithGoalsHomeTeam = results.Count(match => match.GoalsHomeTeam != null),
                TotalWithGoalsAwayTeam = results.Count(match => match.GoalsAwayTeam != null),
                TotalWithShotsOnTargetTotal = results.Count(match => match.ShotsOnTargetTotal != null),
                TotalWithShotsOnTargetHomeTeam = results.Count(match => match.ShotsOnTargetHomeTeam != null),
                TotalWithShotsOnTargetAwayTeam = results.Count(match => match.ShotsOnTargetAwayTeam != null),
                TotalWithShotsTotal = results.Count(match => match.ShotsTotal != null),
                TotalWithShotsHomeTeam = results.Count(match => match.ShotsHomeTeam != null),
                TotalWithShotsAwayTeam = results.Count(match => match.ShotsAwayTeam != null),
                TotalWithCardsTotal = results.Count(match => match.CardsTotal != null),
                Matches = results.ToList()
            };
        }

        private async Task<PinnacleUpcomingFootballOddsMatch> ScrapeMatchAsync(
            PinnacleApiCandidate candidate,
            CancellationToken cancellationToken)
        {
            var markets = await GetAsync<List<ArcadiaMarket>>(
                $"0.1/matchups/{candidate.Parent.Id}/markets/related/straight",
                cancellationToken) ?? new List<ArcadiaMarket>();

            var match = BuildMatchMetadata(candidate);
            var openMarkets = markets
                .Where(market => market.Period == 0)
                .Where(market => market.Status.Equals("open", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var cornerMarkets = GetUnitMarkets(candidate, openMarkets, "Corners");

            match.CornersTotal = BuildMarket(
                cornerMarkets,
                "total",
                side: null,
                "Total (Corners) Match");
            match.CornersHomeTeam = BuildMarket(
                cornerMarkets,
                "team_total",
                "home",
                "Team Total (Corners) Match - Home");
            match.CornersAwayTeam = BuildMarket(
                cornerMarkets,
                "team_total",
                "away",
                "Team Total (Corners) Match - Away");
            var goalMarkets = GetUnitMarkets(candidate, openMarkets, "Goals");
            if (goalMarkets.Length == 0)
                goalMarkets = openMarkets.Where(market => market.MatchupId == candidate.Parent.Id).ToArray();
            match.GoalsTotal = BuildMarket(goalMarkets, "total", null, "Total Goals Match");
            match.GoalsHomeTeam = BuildMarket(goalMarkets, "team_total", "home", "Team Total Goals Match - Home");
            match.GoalsAwayTeam = BuildMarket(goalMarkets, "team_total", "away", "Team Total Goals Match - Away");
            var shotsOnTargetMarkets = GetUnitMarkets(candidate, openMarkets, "Shots on Target", "Shots On Target", "ShotsOnTarget");
            match.ShotsOnTargetTotal = BuildMarket(shotsOnTargetMarkets, "total", null, "Total Shots on Target Match");
            match.ShotsOnTargetHomeTeam = BuildMarket(shotsOnTargetMarkets, "team_total", "home", "Team Total Shots on Target Match - Home");
            match.ShotsOnTargetAwayTeam = BuildMarket(shotsOnTargetMarkets, "team_total", "away", "Team Total Shots on Target Match - Away");
            var shotsMarkets = GetUnitMarkets(candidate, openMarkets, "Shots");
            match.ShotsTotal = BuildMarket(shotsMarkets, "total", null, "Total Shots Match");
            match.ShotsHomeTeam = BuildMarket(shotsMarkets, "team_total", "home", "Team Total Shots Match - Home");
            match.ShotsAwayTeam = BuildMarket(shotsMarkets, "team_total", "away", "Team Total Shots Match - Away");
            match.CardsTotal = BuildMarket(GetUnitMarkets(candidate, openMarkets, "Cards", "Bookings"), "total", null, "Total Cards Match");

            if (match.CornersTotal == null)
                match.Notes.Add("Pinnacle API no entrego total de corners abierto para el partido.");

            if (match.CornersHomeTeam == null)
                match.Notes.Add("Pinnacle API no entrego total de corners del local.");

            if (match.CornersAwayTeam == null)
                match.Notes.Add("Pinnacle API no entrego total de corners de la visita.");

            if (match.GoalsTotal == null)
                match.Notes.Add("Pinnacle API no entrego total de goles abierto para el partido.");

            if (match.GoalsHomeTeam == null || match.GoalsAwayTeam == null)
                match.Notes.Add("Pinnacle API no entrego ambos totales de goles por equipo.");

            if (match.ShotsOnTargetTotal == null)
                match.Notes.Add("Pinnacle API no entrego total de tiros al arco abierto para el partido.");

            if (match.ShotsOnTargetHomeTeam == null || match.ShotsOnTargetAwayTeam == null)
                match.Notes.Add("Pinnacle API no entrego ambos totales de tiros al arco por equipo.");

            if (match.ShotsTotal == null)
                match.Notes.Add("Pinnacle API no entrego total de tiros abierto para el partido.");

            if (match.ShotsHomeTeam == null || match.ShotsAwayTeam == null)
                match.Notes.Add("Pinnacle API no entrego ambos totales de tiros por equipo.");

            if (match.CardsTotal == null)
                match.Notes.Add("Pinnacle API no entrego total de tarjetas abierto para el partido.");

            return match;
        }

        private static PinnacleUpcomingFootballOddsMatch BuildMatchMetadata(PinnacleApiCandidate candidate)
        {
            var homeTeam = GetParticipantName(candidate.Parent, "home", 0);
            var awayTeam = GetParticipantName(candidate.Parent, "away", 1);
            var league = candidate.Parent.League.Name;

            return new PinnacleUpcomingFootballOddsMatch
            {
                SourceMatchId = candidate.Parent.Id.ToString(CultureInfo.InvariantCulture),
                SourceUrl = BuildSourceUrl(candidate.Parent.Id, league, homeTeam, awayTeam),
                MatchDateLocal = TimeZoneInfo.ConvertTime(
                    candidate.Parent.StartTime,
                    ResolveChileTimeZone()).DateTime,
                League = league,
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                StandardizedLeague = CanonicalNameCatalog.CanonicalizeLeague(league),
                StandardizedHomeTeam = CanonicalNameCatalog.CanonicalizeTeam(homeTeam),
                StandardizedAwayTeam = CanonicalNameCatalog.CanonicalizeTeam(awayTeam),
                HomeTeamGender = "M",
                AwayTeamGender = "M"
            };
        }

        private static BetanoMarketOddsDto? BuildMarket(
            IEnumerable<ArcadiaMarket> markets,
            string marketType,
            string? side,
            string marketName)
        {
            var lines = markets
                .Where(market => market.Type.Equals(marketType, StringComparison.OrdinalIgnoreCase))
                .Where(market => side == null || market.Side.Equals(side, StringComparison.OrdinalIgnoreCase))
                .OrderBy(market => market.IsAlternate)
                .Select(TryBuildLine)
                .Where(line => line is not null)
                .Select(line => line!)
                .GroupBy(line => line.Line)
                .Select(group => group.First())
                .OrderBy(line => line.Line)
                .ToList();

            return lines.Count == 0
                ? null
                : new BetanoMarketOddsDto
                {
                    MarketName = marketName,
                    Lines = lines
                };
        }

        private static BetanoLineOddsDto? TryBuildLine(ArcadiaMarket market)
        {
            var over = market.Prices.FirstOrDefault(price =>
                price.Designation.Equals("over", StringComparison.OrdinalIgnoreCase));
            var under = market.Prices.FirstOrDefault(price =>
                price.Designation.Equals("under", StringComparison.OrdinalIgnoreCase));
            var points = over?.Points ?? under?.Points;

            if (points == null || (over == null && under == null))
                return null;

            return new BetanoLineOddsDto
            {
                Line = points.Value,
                OverOdds = over == null ? null : ConvertAmericanToDecimal(over.Price),
                UnderOdds = under == null ? null : ConvertAmericanToDecimal(under.Price)
            };
        }

        private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_apiBaseUrl), relativeUrl));
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
            request.Headers.TryAddWithoutValidation("X-Language", "en-GB");
            request.Headers.TryAddWithoutValidation("X-Customer-Culture", "en-GB");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        private static decimal? ConvertAmericanToDecimal(decimal americanOdds)
        {
            if (americanOdds == 0)
                return null;

            var decimalOdds = americanOdds > 0
                ? 1m + americanOdds / 100m
                : 1m + 100m / Math.Abs(americanOdds);
            return Math.Round(decimalOdds, 2, MidpointRounding.AwayFromZero);
        }

        private static string GetParticipantName(ArcadiaMatchup matchup, string alignment, int fallbackIndex)
        {
            var aligned = matchup.Participants.FirstOrDefault(participant =>
                participant.Alignment.Equals(alignment, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(aligned?.Name))
                return aligned.Name.Trim();

            return matchup.Participants.Count > fallbackIndex
                ? matchup.Participants[fallbackIndex].Name.Trim()
                : string.Empty;
        }

        private static bool IsWomenMatch(ArcadiaMatchup matchup)
        {
            var text = string.Join(' ',
                matchup.League.Name,
                matchup.League.Group,
                string.Join(' ', matchup.Participants.Select(participant => participant.Name)))
                .ToLowerInvariant();

            return text.Contains("women")
                || text.Contains("female")
                || text.Contains("femen")
                || text.Contains("(w)");
        }

        private static string BuildSourceUrl(long id, string league, string homeTeam, string awayTeam) =>
            $"https://www.pinnacle.com/es/soccer/{Slugify(league)}/{Slugify(homeTeam)}-vs-{Slugify(awayTeam)}/{id}/";

        private static string Slugify(string value)
        {
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var slug = new StringBuilder(decomposed.Length);
            var pendingSeparator = false;

            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(character))
                {
                    if (pendingSeparator && slug.Length > 0)
                        slug.Append('-');

                    slug.Append(char.ToLowerInvariant(character));
                    pendingSeparator = false;
                }
                else
                {
                    pendingSeparator = true;
                }
            }

            return slug.ToString();
        }

        private static string NormalizeBaseUrl(string value) =>
            value.TrimEnd('/') + "/";

        private static TimeZoneInfo ResolveChileTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        private static ArcadiaMarket[] GetUnitMarkets(
            PinnacleApiCandidate candidate,
            IEnumerable<ArcadiaMarket> markets,
            params string[] units)
        {
            var matchupIds = candidate.MarketMatchups
                .Where(matchup => units.Any(unit => matchup.Units.Equals(unit, StringComparison.OrdinalIgnoreCase)))
                .Select(matchup => matchup.Id)
                .ToHashSet();
            return markets.Where(market => matchupIds.Contains(market.MatchupId)).ToArray();
        }

        private sealed record PinnacleApiCandidate(
            ArcadiaMatchup Parent,
            IReadOnlyList<ArcadiaMatchup> MarketMatchups);

        private sealed class ArcadiaMatchup
        {
            public long Id { get; init; }
            public long? ParentId { get; init; }
            public string Units { get; init; } = string.Empty;
            public bool IsLive { get; init; }
            public string Status { get; init; } = string.Empty;
            public DateTimeOffset StartTime { get; init; }
            public ArcadiaLeague League { get; init; } = new();
            public List<ArcadiaParticipant> Participants { get; init; } = new();
        }

        private sealed class ArcadiaLeague
        {
            public string Name { get; init; } = string.Empty;
            public string Group { get; init; } = string.Empty;
        }

        private sealed class ArcadiaParticipant
        {
            public string Alignment { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        private sealed class ArcadiaMarket
        {
            public long MatchupId { get; init; }
            public int Period { get; init; }
            public string Status { get; init; } = string.Empty;
            public string Type { get; init; } = string.Empty;
            public string Side { get; init; } = string.Empty;
            public bool IsAlternate { get; init; }
            public List<ArcadiaPrice> Prices { get; init; } = new();
        }

        private sealed class ArcadiaPrice
        {
            public string Designation { get; init; } = string.Empty;
            public decimal? Points { get; init; }
            public decimal Price { get; init; }
        }
    }
}
