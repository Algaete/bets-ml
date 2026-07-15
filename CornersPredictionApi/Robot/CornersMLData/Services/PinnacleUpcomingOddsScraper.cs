using CornersMLData.Data;
using CornersMLData.Models;
using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Services
{
    public sealed class PinnacleUpcomingOddsScraper
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex MatchUrlRegex = new(
            @"^https://www\.pinnacle\.com/es/soccer/(?!matchups/|futures/).+/(?<id>\d+)/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CornersLineRegex = new(
            @"Más de\s+(?<line>\d+(?:[.,]\d+)?)\s+Córneres?\s+(?<over>\d+(?:[.,]\d+)?)\s+Menos de\s+(?<line2>\d+(?:[.,]\d+)?)\s+Córneres?\s+(?<under>\d+(?:[.,]\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TeamTotalLineRegex = new(
            @"Más de\s+(?<line>\d+(?:[.,]\d+)?)\s+(?<over>\d+(?:[.,]\d+)?)\s+Menos de\s+(?<line2>\d+(?:[.,]\d+)?)\s+(?<under>\d+(?:[.,]\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly ILogger<PinnacleUpcomingOddsScraper> _logger;

        public PinnacleUpcomingOddsScraper(ILogger<PinnacleUpcomingOddsScraper> logger)
        {
            _logger = logger;
        }

        public async Task<PinnacleUpcomingFootballOddsResponse> ScrapeUpcomingFootballAsync(
            int take = 10,
            CancellationToken cancellationToken = default)
        {
            if (take <= 0)
                take = 10;

            if (take > 50)
                take = 50;

            using var playwright = await Playwright.CreateAsync();

            var sessionDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                ".runtime",
                "tmp",
                $"playwright-pinnacle-upcoming-odds-{Environment.ProcessId}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(sessionDir);

            var context = await playwright.Chromium.LaunchPersistentContextAsync(
                sessionDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Channel = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSER_CHANNEL"),
                    Headless = true,
                    SlowMo = 0,
                    ChromiumSandbox = false,
                    ViewportSize = new ViewportSize { Width = 1600, Height = 1200 },
                    ScreenSize = new ScreenSize { Width = 1600, Height = 1200 },
                    Locale = "es-CL",
                    IgnoreHTTPSErrors = true,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-blink-features=AutomationControlled",
                        "--disable-dev-shm-usage",
                        "--disable-background-networking",
                        "--start-maximized"
                    }
                });

            try
            {
                var discoveredMatches = await DiscoverUpcomingMatchesAsync(context, take, cancellationToken);
                var results = new List<PinnacleUpcomingFootballOddsMatch>();

                foreach (var candidate in discoveredMatches.Take(take))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var match = await ScrapeMatchAsync(context, candidate, cancellationToken);
                        results.Add(match);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "No se pudo scrapear partido Pinnacle. Url={Url}, MatchId={MatchId}",
                            candidate.Url,
                            candidate.SourceMatchId);

                        results.Add(new PinnacleUpcomingFootballOddsMatch
                        {
                            SourceMatchId = candidate.SourceMatchId,
                            SourceUrl = candidate.Url,
                            Notes =
                            {
                                $"Error scraping match: {ex.Message}"
                            }
                        });
                    }
                }

                return new PinnacleUpcomingFootballOddsResponse
                {
                    Message = "Scraping Pinnacle de proximos partidos de futbol completado.",
                    ScrapedAtUtc = DateTime.UtcNow,
                    TotalDiscovered = discoveredMatches.Count,
                    TotalProcessed = results.Count,
                    TotalWithCornersTotal = results.Count(x => x.CornersTotal != null),
                    TotalWithCornersHomeTeam = results.Count(x => x.CornersHomeTeam != null),
                    TotalWithCornersAwayTeam = results.Count(x => x.CornersAwayTeam != null),
                    Matches = results
                };
            }
            finally
            {
                try { await context.CloseAsync(); } catch { }
            }
        }

        private async Task<List<PinnacleMatchCandidate>> DiscoverUpcomingMatchesAsync(
            IBrowserContext context,
            int take,
            CancellationToken cancellationToken)
        {
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync(
                "https://www.pinnacle.com/es/soccer/matchups/highlights/",
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 45000
                });

            await page.WaitForTimeoutAsync(8000);
            cancellationToken.ThrowIfCancellationRequested();

            var candidatesJson = await page.EvaluateAsync<string>(
                """
                (limit) => {
                  const items = [];
                  const seen = new Set();
                  const anchors = Array.from(document.querySelectorAll('a[href]'));

                  for (const anchor of anchors) {
                    const href = (anchor.href || '').split('#')[0];
                    if (!href) {
                      continue;
                    }

                    if (!/^https:\/\/www\.pinnacle\.com\/es\/soccer\/(?!matchups\/|futures\/).+\/\d+\/?$/i.test(href)) {
                      continue;
                    }

                    const idMatch = href.match(/\/(\d+)\/?$/);
                    if (!idMatch) {
                      continue;
                    }

                    const sourceMatchId = idMatch[1];
                    if (seen.has(sourceMatchId)) {
                      continue;
                    }

                    seen.add(sourceMatchId);
                    items.push({
                      sourceMatchId,
                      url: href,
                      listingText: (anchor.textContent || '').replace(/\s+/g, ' ').trim()
                    });

                    if (items.length >= limit * 3) {
                      break;
                    }
                  }

                  return JSON.stringify(items);
                }
                """,
                take);

            var candidates = JsonSerializer.Deserialize<List<PinnacleMatchCandidate>>(candidatesJson, JsonOptions);

            return candidates?
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url) && MatchUrlRegex.IsMatch(candidate.Url))
                .ToList()
                ?? new List<PinnacleMatchCandidate>();
        }

        private async Task<PinnacleUpcomingFootballOddsMatch> ScrapeMatchAsync(
            IBrowserContext context,
            PinnacleMatchCandidate candidate,
            CancellationToken cancellationToken)
        {
            var page = await context.NewPageAsync();

            try
            {
                await page.GotoAsync(
                    candidate.Url,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 45000
                    });

                await page.WaitForTimeoutAsync(8000);
                cancellationToken.ThrowIfCancellationRequested();

                var snapshotJson = await page.EvaluateAsync<string>(
                    """
                    () => {
                      const jsonLd = Array.from(document.querySelectorAll('script[type="application/ld+json"]'))
                        .map(script => (script.textContent || '').trim())
                        .filter(Boolean);

                      return JSON.stringify({
                        title: document.title || '',
                        url: location.href,
                        bodyText: (document.body.innerText || '').replace(/\s+/g, ' ').trim(),
                        jsonLd
                      });
                    }
                    """);

                var snapshot = JsonSerializer.Deserialize<PinnacleMatchPageSnapshot>(snapshotJson, JsonOptions);
                if (snapshot == null)
                    throw new InvalidOperationException("No se pudo leer el contenido del partido en Pinnacle.");

                var metadata = ParseMetadata(snapshot.JsonLd, candidate.Url, candidate.SourceMatchId);

                var match = new PinnacleUpcomingFootballOddsMatch
                {
                    SourceMatchId = candidate.SourceMatchId,
                    SourceUrl = candidate.Url,
                    MatchDateLocal = metadata.MatchDateLocal,
                    League = metadata.League,
                    HomeTeam = metadata.HomeTeam,
                    AwayTeam = metadata.AwayTeam,
                    StandardizedLeague = CanonicalizeLeague(metadata.League),
                    StandardizedHomeTeam = CanonicalizeTeam(metadata.HomeTeam),
                    StandardizedAwayTeam = CanonicalizeTeam(metadata.AwayTeam),
                    HomeTeamGender = "M",
                    AwayTeamGender = "M",
                    CornersTotal = ParseCornersTotal(snapshot.BodyText),
                };

                var teamTotals = ParseTeamCorners(snapshot.BodyText);
                match.CornersHomeTeam = teamTotals.homeMarket;
                match.CornersAwayTeam = teamTotals.awayMarket;

                if (match.CornersTotal == null)
                    match.Notes.Add("No se encontro el mercado Total (Córneres) Partido.");

                if (match.CornersHomeTeam == null || match.CornersAwayTeam == null)
                    match.Notes.Add("No se encontro completo el mercado Total del equipo (Córneres) Partido.");

                return match;
            }
            finally
            {
                try { await page.CloseAsync(); } catch { }
            }
        }

        private static PinnacleMatchMetadata ParseMetadata(
            IReadOnlyList<string> jsonLd,
            string fallbackUrl,
            string sourceMatchId)
        {
            string? homeTeam = null;
            string? awayTeam = null;
            string? league = null;
            DateTime? startUtc = null;

            foreach (var rawJson in jsonLd)
            {
                try
                {
                    var node = JsonNode.Parse(rawJson);
                    if (node is not JsonObject obj)
                        continue;

                    var type = obj["@type"]?.GetValue<string>();
                    if (string.Equals(type, "SportsEvent", StringComparison.OrdinalIgnoreCase))
                    {
                        homeTeam = obj["homeTeam"]?["name"]?.GetValue<string>() ?? homeTeam;
                        awayTeam = obj["awayTeam"]?["name"]?.GetValue<string>() ?? awayTeam;
                        league = obj["location"]?["name"]?.GetValue<string>() ?? league;

                        var startDateRaw = obj["startDate"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(startDateRaw)
                            && DateTimeOffset.TryParse(startDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedStart))
                        {
                            startUtc = parsedStart.UtcDateTime;
                        }
                    }
                    else if (string.Equals(type, "BreadcrumbList", StringComparison.OrdinalIgnoreCase))
                    {
                        var items = obj["itemListElement"]?.AsArray();
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                if (item?["position"]?.GetValue<int>() == 3)
                                {
                                    league ??= item["name"]?.GetValue<string>();
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore malformed json-ld fragments.
                }
            }

            if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
            {
                var urlTeams = ParseTeamsFromUrl(fallbackUrl);
                homeTeam ??= urlTeams.homeTeam;
                awayTeam ??= urlTeams.awayTeam;
            }

            var matchDateLocal = startUtc.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(startUtc.Value, ResolveChileTimeZone())
                : (DateTime?)null;

            return new PinnacleMatchMetadata(
                sourceMatchId: sourceMatchId,
                league: string.IsNullOrWhiteSpace(league) ? "Unknown" : league.Trim(),
                homeTeam: string.IsNullOrWhiteSpace(homeTeam) ? "Unknown" : homeTeam.Trim(),
                awayTeam: string.IsNullOrWhiteSpace(awayTeam) ? "Unknown" : awayTeam.Trim(),
                matchDateLocal: matchDateLocal);
        }

        private static BetanoMarketOddsDto? ParseCornersTotal(string bodyText)
        {
            var section = ExtractSection(
                bodyText,
                "Total \\(Córneres\\)Partido",
                "Total del equipo \\(Córneres\\)Partido",
                "Línea de dinero \\(Tarjetas\\)Partido",
                "Hándicap \\(Córneres\\)1\\.ª parte",
                "Total \\(Córneres\\)1\\.ª parte");

            if (string.IsNullOrWhiteSpace(section))
                return null;

            var lines = ParseLinePairs(section, requireCornersWord: true, preserveEncounterOrder: false);
            return lines.Count == 0
                ? null
                : new BetanoMarketOddsDto
                {
                    MarketName = "Total (Córneres) Partido",
                    Lines = lines
                };
        }

        private static (BetanoMarketOddsDto? homeMarket, BetanoMarketOddsDto? awayMarket) ParseTeamCorners(string bodyText)
        {
            var section = ExtractSection(
                bodyText,
                "Total del equipo \\(Córneres\\)Partido",
                "Línea de dinero \\(Tarjetas\\)Partido",
                "Hándicap \\(Córneres\\)1\\.ª parte",
                "Total \\(Córneres\\)1\\.ª parte");

            if (string.IsNullOrWhiteSpace(section))
                return (null, null);

            var lines = ParseLinePairs(section, requireCornersWord: false, preserveEncounterOrder: true);
            if (lines.Count < 2)
                return (null, null);

            return (
                new BetanoMarketOddsDto
                {
                    MarketName = "Total del equipo (Córneres) Partido - Home",
                    Lines = new List<BetanoLineOddsDto> { lines[0] }
                },
                new BetanoMarketOddsDto
                {
                    MarketName = "Total del equipo (Córneres) Partido - Away",
                    Lines = new List<BetanoLineOddsDto> { lines[1] }
                });
        }

        private static List<BetanoLineOddsDto> ParseLinePairs(
            string section,
            bool requireCornersWord,
            bool preserveEncounterOrder)
        {
            var regex = requireCornersWord ? CornersLineRegex : TeamTotalLineRegex;
            var lines = new List<BetanoLineOddsDto>();

            foreach (Match match in regex.Matches(section))
            {
                if (!match.Success)
                    continue;

                var lineRaw = match.Groups["line"].Value;
                var line2Raw = match.Groups["line2"].Value;
                if (!string.Equals(lineRaw, line2Raw, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryParseDecimal(lineRaw, out var line)
                    || !TryParseDecimal(match.Groups["over"].Value, out var overOdds)
                    || !TryParseDecimal(match.Groups["under"].Value, out var underOdds))
                {
                    continue;
                }

                lines.Add(new BetanoLineOddsDto
                {
                    Line = line,
                    OverOdds = overOdds,
                    UnderOdds = underOdds
                });
            }

            var deduped = lines
                .GroupBy(x => x.Line)
                .Select(g => g.First())
                .ToList();

            return preserveEncounterOrder
                ? deduped
                : deduped.OrderBy(x => x.Line).ToList();
        }

        private static string? ExtractSection(string text, string startPattern, params string[] endPatterns)
        {
            var startMatch = Regex.Match(text, startPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!startMatch.Success)
                return null;

            var startIndex = startMatch.Index + startMatch.Length;
            var endIndex = text.Length;

            foreach (var endPattern in endPatterns)
            {
                var endMatch = Regex.Match(
                    text[startIndex..],
                    endPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (endMatch.Success)
                    endIndex = Math.Min(endIndex, startIndex + endMatch.Index);
            }

            return startIndex >= endIndex
                ? null
                : text[startIndex..endIndex].Trim();
        }

        private static (string? homeTeam, string? awayTeam) ParseTeamsFromUrl(string url)
        {
            var match = Regex.Match(
                url,
                @"/soccer/[^/]+/(?<slug>[^/]+)/\d+/?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
                return (null, null);

            var slug = match.Groups["slug"].Value.Replace('-', ' ');
            var parts = Regex.Split(slug, @"\svs\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return parts.Length == 2 ? (Cultureize(parts[0]), Cultureize(parts[1])) : (null, null);
        }

        private static string Cultureize(string value)
        {
            var normalized = value.Replace("  ", " ").Trim();
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
        }

        private static string CanonicalizeLeague(string value)
            => CanonicalNameCatalog.CanonicalizeLeague(value);

        private static string CanonicalizeTeam(string value)
            => CanonicalNameCatalog.CanonicalizeTeam(value);

        private static bool TryParseDecimal(string raw, out decimal value)
            => decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

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

        private sealed class PinnacleMatchMetadata
        {
            public PinnacleMatchMetadata(
                string sourceMatchId,
                string league,
                string homeTeam,
                string awayTeam,
                DateTime? matchDateLocal)
            {
                SourceMatchId = sourceMatchId;
                League = league;
                HomeTeam = homeTeam;
                AwayTeam = awayTeam;
                MatchDateLocal = matchDateLocal;
            }

            public string SourceMatchId { get; }
            public string League { get; }
            public string HomeTeam { get; }
            public string AwayTeam { get; }
            public DateTime? MatchDateLocal { get; }
        }

        private sealed class PinnacleMatchPageSnapshot
        {
            public string Title { get; set; } = "";
            public string Url { get; set; } = "";
            public string BodyText { get; set; } = "";
            public List<string> JsonLd { get; set; } = new();
        }

        private sealed class PinnacleMatchCandidate
        {
            public string SourceMatchId { get; set; } = "";
            public string Url { get; set; } = "";
            public string ListingText { get; set; } = "";
        }
    }
}
