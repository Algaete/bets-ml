using CornersMLData.Data;
using CornersMLData.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Services
{
    public sealed class BetanoUpcomingOddsScraper
    {
        private static readonly decimal[] TargetTotalLines = [7.5m, 8.5m, 9.5m, 10.5m];
        private static readonly decimal[] TargetTeamCornerLines = [2.5m, 3.5m, 4.5m, 5.5m, 6.5m, 7.5m, 8.5m];
        private const int MaxCompetitionTraversalDepth = 2;
        private const int MaxCompetitionPagesToVisit = 40;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IConfiguration _configuration;
        private readonly TeamPositionResolver _teamPositionResolver;
        private readonly ILogger<BetanoUpcomingOddsScraper> _logger;

        public BetanoUpcomingOddsScraper(
            IConfiguration configuration,
            TeamPositionResolver teamPositionResolver,
            ILogger<BetanoUpcomingOddsScraper> logger)
        {
            _configuration = configuration;
            _teamPositionResolver = teamPositionResolver;
            _logger = logger;
        }

        public async Task<BetanoUpcomingFootballOddsResponse> ScrapeUpcomingFootballAsync(
            int take = 10,
            CancellationToken cancellationToken = default)
        {
            if (take <= 0)
                take = 10;

            if (take > 100)
                take = 100;

            await using var conn = await TryOpenConnectionAsync(cancellationToken);

            using var playwright = await Playwright.CreateAsync();

            var context = await CreateBrowserContextAsync(playwright);

            try
            {
                var targetDiscoveryCount = Math.Min(Math.Max(take * 3, 40), 120);
                var discoveredMatches = await DiscoverUpcomingMatchesAsync(context, targetDiscoveryCount, cancellationToken);

                var filtered = discoveredMatches
                    .Where(x => !IsWomenMatch(x.ListingText) && !IsWomenCompetition(x.CompetitionName, x.Url))
                    // Prioritize the competitions the bot currently models before the generic calendar order.
                    .OrderBy(x => CompetitionPriority(x.CompetitionName, x.Url))
                    .ThenBy(x => x.MatchDateLocal ?? DateTime.MaxValue)
                    .ThenBy(x => x.CompetitionName)
                    .ThenBy(x => x.ListingText)
                    .Take(take)
                    .ToList();

                var results = new List<BetanoUpcomingFootballOddsMatch>();

                foreach (var candidate in filtered)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var item = await ScrapeMatchAsync(context, candidate, conn, cancellationToken);
                        results.Add(item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "No se pudo scrapear partido Betano. Url={Url}, Text={Text}",
                            candidate.Url,
                            candidate.ListingText);

                        results.Add(new BetanoUpcomingFootballOddsMatch
                        {
                            SourceMatchId = candidate.SourceMatchId,
                            SourceUrl = candidate.Url,
                            HomeTeam = candidate.ParsedHomeTeam ?? string.Empty,
                            AwayTeam = candidate.ParsedAwayTeam ?? string.Empty,
                            Notes =
                            {
                                $"Error scraping match: {ex.Message}"
                            }
                        });
                    }
                }

                return new BetanoUpcomingFootballOddsResponse
                {
                    Message = "Scraping Betano de proximos partidos de futbol completado.",
                    ScrapedAtUtc = DateTime.UtcNow,
                    TotalDiscovered = discoveredMatches.Count,
                    TotalProcessed = results.Count,
                    TotalWithCornersTotal = results.Count(x => x.CornersTotal != null),
                    TotalWithCornersHomeTeam = results.Count(x => x.CornersHomeTeam != null),
                    TotalWithCornersAwayTeam = results.Count(x => x.CornersAwayTeam != null),
                    TotalWithShotsOnTargetTotal = results.Count(x => x.ShotsOnTargetTotal != null),
                    Matches = results
                };
            }
            finally
            {
                try { await context.CloseAsync(); } catch { }
            }
        }

        public async Task<BetanoUpcomingFootballOddsResponse> ScrapeMatchByUrlAsync(
            string sourceUrl,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var matchUri)
                || !matchUri.Host.Contains("betano", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("sourceUrl debe ser una URL absoluta de Betano.", nameof(sourceUrl));
            }

            await using var conn = await TryOpenConnectionAsync(cancellationToken);
            using var playwright = await Playwright.CreateAsync();
            var context = await CreateBrowserContextAsync(playwright);

            try
            {
                var candidate = new BetanoMatchCandidate
                {
                    Url = sourceUrl,
                    ListingText = sourceUrl,
                    SourceMatchId = ExtractSourceMatchId(sourceUrl)
                };

                var match = await ScrapeMatchAsync(context, candidate, conn, cancellationToken);
                return new BetanoUpcomingFootballOddsResponse
                {
                    Message = "Scraping Betano de partido especifico completado.",
                    ScrapedAtUtc = DateTime.UtcNow,
                    TotalDiscovered = 1,
                    TotalProcessed = 1,
                    TotalWithCornersTotal = match.CornersTotal == null ? 0 : 1,
                    TotalWithCornersHomeTeam = match.CornersHomeTeam == null ? 0 : 1,
                    TotalWithCornersAwayTeam = match.CornersAwayTeam == null ? 0 : 1,
                    TotalWithShotsOnTargetTotal = match.ShotsOnTargetTotal == null ? 0 : 1,
                    Matches = new List<BetanoUpcomingFootballOddsMatch> { match }
                };
            }
            finally
            {
                try { await context.CloseAsync(); } catch { }
            }
        }

        private static async Task<IBrowserContext> CreateBrowserContextAsync(IPlaywright playwright)
        {
            var sessionDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                ".runtime",
                "tmp",
                $"playwright-betano-upcoming-odds-{Environment.ProcessId}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(sessionDir);

            return await playwright.Chromium.LaunchPersistentContextAsync(
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
        }

        private async Task<List<BetanoMatchCandidate>> DiscoverUpcomingMatchesAsync(
            IBrowserContext context,
            int targetDiscoveryCount,
            CancellationToken cancellationToken)
        {
            var discoveredMatches = new List<BetanoMatchCandidate>();
            var seenMatchUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queuedCompetitionUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<BetanoCompetitionLink>();
            var visitedCompetitionPages = 0;

            foreach (var seed in GetSeedCompetitionLinks())
            {
                var normalizedSeedUrl = NormalizeBetanoUrl(seed.Url);
                if (string.IsNullOrWhiteSpace(normalizedSeedUrl))
                    continue;

                if (queuedCompetitionUrls.Add(normalizedSeedUrl))
                {
                    queue.Enqueue(new BetanoCompetitionLink
                    {
                        Url = normalizedSeedUrl,
                        Text = seed.Text,
                        Depth = 0
                    });
                }
            }

            while (queue.Count > 0
                && visitedCompetitionPages < MaxCompetitionPagesToVisit
                && seenMatchUrls.Count < targetDiscoveryCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var competition = queue.Dequeue();
                visitedCompetitionPages++;
                if (IsWomenCompetition(competition.Text, competition.Url))
                    continue;

                try
                {
                    var competitionPage = await context.NewPageAsync();
                    try
                    {
                        await NavigateToBetanoPageAsync(competitionPage, competition.Url);
                        await PrepareCompetitionPageAsync(competitionPage);

                        var competitionMatches = await ExtractUpcomingMatchesAsync(competitionPage, competition.Text);
                        foreach (var match in competitionMatches)
                        {
                            if (seenMatchUrls.Add(match.Url))
                                discoveredMatches.Add(match);
                        }

                        if (competition.Depth >= MaxCompetitionTraversalDepth)
                            continue;

                        var childCompetitionLinks = await ExtractCompetitionLinksAsync(competitionPage);
                        foreach (var child in childCompetitionLinks)
                        {
                            var normalizedChildUrl = NormalizeBetanoUrl(child.Url);
                            if (!ShouldFollowCompetitionLink(child.Text, normalizedChildUrl))
                                continue;

                            if (string.Equals(normalizedChildUrl, competition.Url, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (competitionMatches.Count > 0
                                && !ShouldTraverseChildCompetition(competition.Url, normalizedChildUrl))
                            {
                                continue;
                            }

                            if (!queuedCompetitionUrls.Add(normalizedChildUrl))
                                continue;

                            queue.Enqueue(new BetanoCompetitionLink
                            {
                                Url = normalizedChildUrl,
                                Text = child.Text,
                                Depth = competition.Depth + 1
                            });
                        }
                    }
                    finally
                    {
                        try { await competitionPage.CloseAsync(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "No se pudo leer competencia Betano. Competition={Competition}, Url={Url}, Depth={Depth}",
                        competition.Text,
                        competition.Url,
                        competition.Depth);
                }
            }

            return discoveredMatches;
        }

        private async Task<BetanoUpcomingFootballOddsMatch> ScrapeMatchAsync(
            IBrowserContext context,
            BetanoMatchCandidate candidate,
            SqlConnection? conn,
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

                await page.WaitForTimeoutAsync(1000);
                await DismissBetanoPopupsAsync(page);
                await PrepareMatchPageAsync(page);

                var defaultSnapshot = await ExtractMatchSnapshotAsync(page);
                var cornersSnapshot = await ExtractSnapshotForMarketTabAsync(page, "Córners");
                var allMarketsSnapshot = await ExtractSnapshotForMarketTabAsync(page, "Todo");
                var snapshot = allMarketsSnapshot.VisibleCards.Count > 0
                    ? allMarketsSnapshot
                    : defaultSnapshot;
                var titleMatch = ParseMatchTitle(snapshot.PageTitle);
                var homeTeam = titleMatch.HomeTeam ?? candidate.ParsedHomeTeam ?? string.Empty;
                var awayTeam = titleMatch.AwayTeam ?? candidate.ParsedAwayTeam ?? string.Empty;
                var sourceHomeTeam = homeTeam;
                var sourceAwayTeam = awayTeam;
                var league = snapshot.LeagueCandidate ?? ResolveLeague(snapshot.Breadcrumbs, homeTeam, awayTeam) ?? string.Empty;

                var cornersCards = cornersSnapshot.VisibleCards.Count > 0
                    ? cornersSnapshot.VisibleCards
                    : snapshot.VisibleCards;

                var shotsCards = allMarketsSnapshot.VisibleCards.Count > 0
                    ? allMarketsSnapshot.VisibleCards
                    : snapshot.VisibleCards;

                var cornersText = cornersCards
                    .Where(x => x.StartsWith("BBCórners Más/Menos", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Length)
                    .FirstOrDefault();

                var shotsText = shotsCards
                    .Where(x =>
                        x.StartsWith("BBTiros al Arco Más/Menos", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("BBRemates al Arco Más/Menos", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("BBTiros a puerta Más/Menos", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("BBRemates a puerta Más/Menos", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Length)
                    .FirstOrDefault();

                var homeCornersText = FindTeamCornersCard(cornersCards, sourceHomeTeam);
                var awayCornersText = FindTeamCornersCard(cornersCards, sourceAwayTeam);

                var standardizedLeague = league;
                var standardizedHomeTeam = homeTeam;
                var standardizedAwayTeam = awayTeam;
                var notes = new List<string>();

                if (conn != null
                    && !string.IsNullOrWhiteSpace(league)
                    && !string.IsNullOrWhiteSpace(homeTeam)
                    && !string.IsNullOrWhiteSpace(awayTeam))
                {
                    try
                    {
                        var resolvedIdentity = await _teamPositionResolver.ResolveIdentityAsync(
                            conn,
                            league,
                            homeTeam,
                            awayTeam,
                            "M",
                            "M",
                            cancellationToken: cancellationToken);

                        if (!string.IsNullOrWhiteSpace(resolvedIdentity.StandardizedLeague))
                            standardizedLeague = resolvedIdentity.StandardizedLeague;

                        if (!string.IsNullOrWhiteSpace(resolvedIdentity.PreferredHomeTeam))
                            homeTeam = resolvedIdentity.PreferredHomeTeam;

                        if (!string.IsNullOrWhiteSpace(resolvedIdentity.PreferredAwayTeam))
                            awayTeam = resolvedIdentity.PreferredAwayTeam;

                        if (!string.IsNullOrWhiteSpace(resolvedIdentity.StandardizedHomeTeam))
                            standardizedHomeTeam = resolvedIdentity.StandardizedHomeTeam;

                        if (!string.IsNullOrWhiteSpace(resolvedIdentity.StandardizedAwayTeam))
                            standardizedAwayTeam = resolvedIdentity.StandardizedAwayTeam;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "No se pudieron resolver nombres estandarizados Betano. League={League}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}",
                            league,
                            homeTeam,
                            awayTeam);

                        notes.Add($"No se pudo resolver estandarizacion desde BD: {ex.Message}");
                    }
                }

                var result = new BetanoUpcomingFootballOddsMatch
                {
                    SourceMatchId = candidate.SourceMatchId,
                    SourceUrl = candidate.Url,
                    MatchDateLocal = snapshot.MatchDateLocal ?? candidate.MatchDateLocal,
                    League = league,
                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam,
                    StandardizedLeague = standardizedLeague,
                    StandardizedHomeTeam = standardizedHomeTeam,
                    StandardizedAwayTeam = standardizedAwayTeam,
                    HomeTeamGender = "M",
                    AwayTeamGender = "M",
                    CornersTotal = BuildMarket(cornersText, "Córners Más/Menos"),
                    CornersHomeTeam = BuildMarket(homeCornersText, "Córners del equipo local", TargetTeamCornerLines),
                    CornersAwayTeam = BuildMarket(awayCornersText, "Córners del equipo visitante", TargetTeamCornerLines),
                    ShotsOnTargetTotal = BuildMarket(shotsText, "Tiros al Arco Más/Menos"),
                    Notes = notes
                };

                if (result.CornersTotal == null)
                    result.Notes.Add("Betano no mostro mercado total de corners para las lineas objetivo.");

                if (result.CornersHomeTeam == null)
                    result.Notes.Add($"Betano no mostro mercado de corners para {sourceHomeTeam}.");

                if (result.CornersAwayTeam == null)
                    result.Notes.Add($"Betano no mostro mercado de corners para {sourceAwayTeam}.");

                if (result.ShotsOnTargetTotal == null)
                    result.Notes.Add("Betano no mostro mercado total de tiros al arco para las lineas objetivo.");

                return result;
            }
            finally
            {
                try { await page.CloseAsync(); } catch { }
            }
        }

        private static BetanoMarketOddsDto? BuildMarket(
            string? rawText,
            string marketName,
            IReadOnlyCollection<decimal>? targetLines = null)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return null;

            var lines = new List<BetanoLineOddsDto>();

            foreach (var line in targetLines ?? TargetTotalLines)
            {
                var extracted = TryExtractLine(rawText, line);
                if (extracted == null)
                    continue;

                lines.Add(extracted);
            }

            return lines.Count == 0
                ? null
                : new BetanoMarketOddsDto
                {
                    MarketName = marketName,
                    Lines = lines.OrderBy(x => x.Line).ToList()
                };
        }

        private static string? FindTeamCornersCard(
            IReadOnlyCollection<string> cards,
            string teamName)
        {
            if (string.IsNullOrWhiteSpace(teamName))
                return null;

            var teamKey = NormalizeMarketText(teamName);
            return cards
                .Where(card => card.Contains("córner", StringComparison.OrdinalIgnoreCase))
                .Where(card => !card.StartsWith("BBCórners Más/Menos", StringComparison.OrdinalIgnoreCase))
                .Where(card => NormalizeMarketText(card).Contains(teamKey, StringComparison.Ordinal))
                .OrderByDescending(card => card.Contains("equipo", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(card => card.Length)
                .FirstOrDefault();
        }

        private static string NormalizeMarketText(string value)
        {
            var normalized = value
                .Normalize(NormalizationForm.FormD)
                .ToLowerInvariant();
            var builder = new System.Text.StringBuilder(normalized.Length);

            foreach (var character in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(character))
                    builder.Append(character);
            }

            return builder.ToString();
        }

        private static BetanoLineOddsDto? TryExtractLine(string rawText, decimal line)
        {
            var lineToken = line.ToString("0.0", CultureInfo.InvariantCulture);
            var pattern = $@"Más\s*de\s*{Regex.Escape(lineToken)}\s*(?<over>\d+\.\d+)\s*Menos\s*{Regex.Escape(lineToken)}\s*(?<under>\d+\.\d+)";
            var match = Regex.Match(rawText, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return new BetanoLineOddsDto
            {
                Line = line,
                OverOdds = ParseDecimal(match.Groups["over"].Value),
                UnderOdds = ParseDecimal(match.Groups["under"].Value)
            };
        }

        private async Task<List<BetanoCompetitionLink>> ExtractCompetitionLinksAsync(IPage page)
        {
            var rawJson = await page.EvaluateAsync<string>(
                @"() => {
                    const norm = value => (value || '').replace(/\s+/g, ' ').trim();
                    const noise = new Set([
                        'Programación de Apuestas',
                        'Fútbol',
                        'Competencias',
                        'VISITA LA PÁGINA DE LA LIGA'
                    ]);

                    const seen = new Set();
                    const rows = Array.from(document.querySelectorAll('a[href]'))
                        .map(anchor => {
                            const href = anchor.href || '';
                            const text = norm(anchor.textContent || '');
                            const y = Math.round(anchor.getBoundingClientRect().y);
                            return { url: href, text, y };
                        })
                        .filter(x => x.url && x.text)
                        .filter(x => !noise.has(x.text))
                        .filter(x => {
                            if (seen.has(x.url)) return false;
                            seen.add(x.url);
                            return true;
                        });
                    return JSON.stringify(rows);
                }");

            var raw = JsonSerializer.Deserialize<List<BetanoCompetitionLink>>(rawJson ?? "[]", JsonOptions)
                ?? new List<BetanoCompetitionLink>();

            return raw
                .Where(x => ShouldFollowCompetitionLink(x.Text, x.Url))
                .OrderBy(x => CompetitionPriority(x.Text, x.Url))
                .ThenBy(x => x.Text)
                .ToList();
        }

        private async Task<List<BetanoMatchCandidate>> ExtractUpcomingMatchesAsync(IPage page, string? competitionName)
        {
            var rawJson = await page.EvaluateAsync<string>(
                @"() => {
                    const norm = value => (value || '').replace(/\s+/g, ' ').trim();
                    const seen = new Set();
                    const rows = Array.from(document.querySelectorAll('a[href]'))
                        .map(anchor => {
                            const href = anchor.href || '';
                            const text = norm(anchor.textContent || '');
                            const pieces = Array.from(anchor.querySelectorAll('*'))
                                .map(el => norm(el.textContent || ''))
                                .filter(Boolean)
                                .filter((value, index, arr) => arr.indexOf(value) === index);
                            const card = anchor.closest('div.tw-relative.tw-overflow-clip.tw-rounded-n')
                                || anchor.closest('div.tw-flex.tw-w-full.tw-flex-row')
                                || anchor.parentElement
                                || anchor;
                            const cardText = norm(card.textContent || '');
                            const y = Math.round(anchor.getBoundingClientRect().y);
                            return {
                                href,
                                text,
                                cardText,
                                y,
                                homeTeam: pieces[0] || null,
                                awayTeam: pieces[1] || null
                            };
                        })
                        .filter(x => x.href && x.text)
                        .filter(x => !x.href.includes('playersTabPriority'))
                        .filter(x => /^https:\/\/lat\.betano\.com\/cuotas-de-partido\/[^\/]+\/\d+\/?$/.test(x.href))
                        .filter(x => {
                            if (seen.has(x.href)) return false;
                            seen.add(x.href);
                            return true;
                        });
                    return JSON.stringify(rows);
                }");

            var raw = JsonSerializer.Deserialize<List<BetanoRawMatchCandidate>>(rawJson ?? "[]", JsonOptions)
                ?? new List<BetanoRawMatchCandidate>();

            return raw
                .Select(x =>
                {
                    var parsed = ParseListingTeams(x.Text);
                    return new BetanoMatchCandidate
                    {
                        Url = x.Href,
                        ListingText = x.Text,
                        CardText = x.CardText,
                        CompetitionName = competitionName ?? string.Empty,
                        SourceMatchId = ExtractSourceMatchId(x.Href),
                        MatchDateLocal = ParseBetanoCandidateDate(x.CardText),
                        ParsedHomeTeam = NormalizeNullable(x.HomeTeam) ?? parsed.HomeTeam,
                        ParsedAwayTeam = NormalizeNullable(x.AwayTeam) ?? parsed.AwayTeam
                    };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .ToList();
        }

        private static DateTime? ParseBetanoCandidateDate(string? cardText)
        {
            var compact = NormalizeNullable(cardText);
            if (string.IsNullOrWhiteSpace(compact))
                return null;

            var match = Regex.Match(compact, @"(?<day>\d{1,2})/(?<month>\d{1,2})\s*(?<hour>\d{1,2}):(?<minute>\d{2})");
            if (!match.Success)
                return null;

            if (!int.TryParse(match.Groups["day"].Value, out var day)
                || !int.TryParse(match.Groups["month"].Value, out var month)
                || !int.TryParse(match.Groups["hour"].Value, out var hour)
                || !int.TryParse(match.Groups["minute"].Value, out var minute))
            {
                return null;
            }

            var nowLocal = DateTime.Now;
            var year = nowLocal.Year;

            try
            {
                var parsed = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);

                if (parsed < nowLocal.AddMonths(-6))
                    parsed = parsed.AddYears(1);
                else if (parsed > nowLocal.AddMonths(6))
                    parsed = parsed.AddYears(-1);

                return parsed;
            }
            catch
            {
                return null;
            }
        }

        private static List<BetanoCompetitionLink> GetSeedCompetitionLinks() =>
            [
                new BetanoCompetitionLink
                {
                    Url = "https://lat.betano.com/sport/futbol/ligas/",
                    Text = "Competencias",
                    Depth = 0
                },
                new BetanoCompetitionLink
                {
                    Url = "https://lat.betano.com/sport/futbol/proximos-partidos-hoy/",
                    Text = "Proximos",
                    Depth = 0
                },
                new BetanoCompetitionLink
                {
                    Url = "https://lat.betano.com/sport/futbol/",
                    Text = "Futbol",
                    Depth = 0
                }
            ];

        private async Task NavigateToBetanoPageAsync(IPage page, string url)
        {
            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 45000
                });
        }

        private async Task PrepareCompetitionPageAsync(IPage page)
        {
            await page.WaitForTimeoutAsync(1200);
            await DismissBetanoPopupsAsync(page);
            await ExpandAllShowAllButtonsAsync(page);
            await ScrollToRevealMoreAsync(page);
            await ExpandAllShowAllButtonsAsync(page);
        }

        private async Task PrepareMatchPageAsync(IPage page)
        {
            await ExpandAllShowAllButtonsAsync(page);
            await ScrollToRevealMoreAsync(page);
            await ExpandAllShowAllButtonsAsync(page);
        }

        private async Task<BetanoMatchSnapshot> ExtractSnapshotForMarketTabAsync(IPage page, string tabText)
        {
            await TryOpenMatchMarketTabAsync(page, tabText);
            await PrepareMatchPageAsync(page);
            return await ExtractMatchSnapshotAsync(page);
        }

        private async Task ScrollToRevealMoreAsync(IPage page)
        {
            var stablePasses = 0;
            var previousHeight = 0;

            for (var attempt = 0; attempt < 6; attempt++)
            {
                int currentHeight;
                try
                {
                    currentHeight = await page.EvaluateAsync<int>(
                        "() => Math.max(document.body?.scrollHeight || 0, document.documentElement?.scrollHeight || 0)");
                }
                catch
                {
                    break;
                }

                if (currentHeight <= previousHeight)
                {
                    stablePasses++;
                    if (stablePasses >= 2)
                        break;
                }
                else
                {
                    stablePasses = 0;
                    previousHeight = currentHeight;
                }

                try
                {
                    await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
                    await page.WaitForTimeoutAsync(700);
                }
                catch
                {
                    break;
                }
            }

            try
            {
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                await page.WaitForTimeoutAsync(150);
            }
            catch
            {
                // Ignore scroll reset failures.
            }
        }

        private static (string? HomeTeam, string? AwayTeam) ParseListingTeams(string listingText)
        {
            if (string.IsNullOrWhiteSpace(listingText))
                return (null, null);

            var compact = listingText.Trim();
            var tokens = new[]
            {
                "EE. UU.",
                "Corea del Sur",
                "Universidad de Chile",
                "Deporte Recife PE",
                "Atlético GO"
            };

            foreach (var token in tokens.OrderByDescending(x => x.Length))
            {
                compact = compact.Replace(token, $"|{token}|", StringComparison.Ordinal);
            }

            var parts = compact
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (parts.Count >= 2)
                return (parts[0], parts[1]);

            return (null, null);
        }

        private async Task<BetanoMatchSnapshot> ExtractMatchSnapshotAsync(IPage page)
        {
            var rawJson = await page.EvaluateAsync<string>(
                @"() => {
                    const norm = value => (value || '').replace(/\s+/g, ' ').trim();

                    const headerItems = Array.from(document.querySelectorAll('a,div,span'))
                        .map(el => ({
                            text: norm(el.textContent || ''),
                            y: Math.round(el.getBoundingClientRect().y)
                        }))
                        .filter(x => x.y >= 60 && x.y <= 90 && x.text && x.text.length <= 100)
                        .reduce((acc, item) => {
                            if (!acc.some(x => x === item.text)) acc.push(item.text);
                            return acc;
                        }, []);

                    const cards = Array.from(document.querySelectorAll('div.tw-bg-sem-color-bg-container-lowest-default'))
                        .map(el => norm(el.textContent || ''))
                        .filter(text => text && text.length > 0);

                    const title = document.title || '';
                    const bareTitle = title.replace(' Fútbol Cuotas | Betano', '').trim();
                    const titleIndex = headerItems.findIndex(x => x === bareTitle);
                    const leagueCandidate = titleIndex >= 0
                        ? headerItems
                            .slice(0, titleIndex)
                            .filter(x =>
                                x !== 'Inicio'
                                && x !== 'Fútbol'
                                && x !== 'Partidos'
                                && x !== 'Programación de Apuestas')
                            .join(' - ')
                        : null;

                    const topDateText = headerItems
                        .find(text => /(?:lunes|martes|miercoles|miércoles|jueves|viernes|sabado|sábado|domingo), \d{1,2} [a-záéíóú]+ \d{4} \d{2}:\d{2}/i.test(text))
                        || null;

                    const snapshot = {
                        pageTitle: title,
                        breadcrumbs: headerItems,
                        leagueCandidate,
                        visibleCards: cards,
                        topDateText
                    };
                    return JSON.stringify(snapshot);
                }");

            return JsonSerializer.Deserialize<BetanoMatchSnapshot>(rawJson ?? "{}", JsonOptions)
                ?? new BetanoMatchSnapshot();
        }

        private async Task DismissBetanoPopupsAsync(IPage page)
        {
            try
            {
                var acceptCookies = page.GetByText("SÍ, ACEPTO", new PageGetByTextOptions { Exact = true });
                if (await acceptCookies.CountAsync() > 0)
                {
                    await acceptCookies.Nth(0).ClickAsync(new LocatorClickOptions { Timeout = 1000 });
                    await page.WaitForTimeoutAsync(300);
                }
            }
            catch { }

            try
            {
                var closeModal = page.Locator("button[aria-label='Close modal']");
                if (await closeModal.CountAsync() > 0)
                {
                    await closeModal.Nth(0).ClickAsync(new LocatorClickOptions { Timeout = 1000 });
                    await page.WaitForTimeoutAsync(300);
                }
            }
            catch { }
        }

        private async Task ExpandAllShowAllButtonsAsync(IPage page)
        {
            try
            {
                var buttons = page.GetByText("MOSTRAR TODO", new PageGetByTextOptions { Exact = true });
                var count = await buttons.CountAsync();
                for (var i = count - 1; i >= 0; i--)
                {
                    try
                    {
                        await buttons.Nth(i).ClickAsync(new LocatorClickOptions { Timeout = 1500 });
                        await page.WaitForTimeoutAsync(250);
                    }
                    catch
                    {
                        // Ignore individual expansion failures.
                    }
                }
            }
            catch
            {
                // Ignore expansion failures.
            }
        }

        private async Task TryOpenMatchMarketTabAsync(IPage page, string tabText)
        {
            try
            {
                var tabs = page.GetByText(tabText, new PageGetByTextOptions { Exact = true });
                if (await tabs.CountAsync() != 1)
                    return;

                await tabs.Nth(0).ClickAsync(new LocatorClickOptions { Timeout = 1500 });
                await page.WaitForTimeoutAsync(400);
            }
            catch
            {
                // Ignore tab navigation failures.
            }
        }

        private async Task<SqlConnection?> TryOpenConnectionAsync(CancellationToken cancellationToken)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                return null;

            var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }

        private static string? ResolveLeague(IReadOnlyList<string>? breadcrumbs, string homeTeam, string awayTeam)
        {
            if (breadcrumbs == null || breadcrumbs.Count == 0)
                return null;

            var titleToken = $"{homeTeam} - {awayTeam}".Trim();
            var index = breadcrumbs
                .Select((text, idx) => new { text, idx })
                .FirstOrDefault(x => string.Equals(x.text, titleToken, StringComparison.OrdinalIgnoreCase))
                ?.idx;

            if (!index.HasValue || index.Value <= 0)
                return null;

            var leagueParts = breadcrumbs
                .Take(index.Value)
                .Where(x =>
                    !string.Equals(x, "Inicio", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x, "Fútbol", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x, "Partidos", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x, "Programación de Apuestas", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return leagueParts.Count == 0
                ? null
                : string.Join(" - ", leagueParts);
        }

        private static (string? HomeTeam, string? AwayTeam) ParseMatchTitle(string? pageTitle)
        {
            var title = NormalizeNullable(pageTitle);
            if (string.IsNullOrWhiteSpace(title))
                return (null, null);

            const string suffix = " Fútbol Cuotas | Betano";
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                title = title[..^suffix.Length];

            var separator = " - ";
            var idx = title.IndexOf(separator, StringComparison.Ordinal);
            if (idx <= 0 || idx >= title.Length - separator.Length)
                return (title, null);

            return (title[..idx].Trim(), title[(idx + separator.Length)..].Trim());
        }

        private static string ExtractSourceMatchId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var match = Regex.Match(url, @"/(\d+)/?$");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static bool IsWomenMatch(string? text)
        {
            var normalized = NormalizeNullable(text)?.ToLowerInvariant() ?? string.Empty;
            return normalized.Contains("(f)")
                || normalized.Contains("women")
                || normalized.Contains("femen");
        }

        private static bool IsWomenCompetition(string? text, string? url)
        {
            var normalizedText = NormalizeNullable(text)?.ToLowerInvariant() ?? string.Empty;
            var normalizedUrl = NormalizeNullable(url)?.ToLowerInvariant() ?? string.Empty;
            return normalizedText.Contains("(f)")
                || normalizedText.Contains("women")
                || normalizedText.Contains("femen")
                || normalizedUrl.Contains("(f)")
                || normalizedUrl.Contains("-f/")
                || normalizedUrl.Contains("women");
        }

        private static bool ShouldFollowCompetitionLink(string? text, string? url)
        {
            var normalizedUrl = NormalizeBetanoUrl(url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
                return false;

            if (!normalizedUrl.StartsWith("https://lat.betano.com/sport/futbol/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalizedUrl.Contains("/cuotas-de-partido/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalizedUrl.EndsWith("/sport/futbol/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalizedUrl.Contains("/sport/futbol/ligas/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalizedUrl.Contains("/sport/futbol/proximos-partidos-hoy/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalizedUrl.Contains("/live/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/ganadores-competencia/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/apuestas-especiales/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/favoritos/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/missions/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/master/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/virtuals/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/celular/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/yours/", StringComparison.OrdinalIgnoreCase)
                || normalizedUrl.Contains("/live-scores/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Regex.IsMatch(normalizedUrl, @"/\d+-t/?$", RegexOptions.IgnoreCase))
                return false;

            var betanoView = GetBetanoView(normalizedUrl);
            if (!string.IsNullOrWhiteSpace(betanoView)
                && !string.Equals(betanoView, "matchresult", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsWomenCompetition(text, normalizedUrl))
                return false;

            return true;
        }

        private static int CompetitionPriority(string? text, string? url)
        {
            var normalized = $"{text} {url}".ToLowerInvariant();
            if (normalized.Contains("mundial")
                || normalized.Contains("copa-mundial")
                || normalized.Contains("world-cup")
                || normalized.Contains("worldcup")
                || normalized.Contains("fifa"))
                return 0;
            if (normalized.Contains("liga-de-primera") || normalized.Contains("chile"))
                return 1;
            if (normalized.Contains("brasileirao"))
                return 2;
            if (normalized.Contains("copa-libertadores"))
                return 3;
            if (normalized.Contains("copa-sudamericana"))
                return 4;
            return 10;
        }

        private static string? NormalizeNullable(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        private static string NormalizeBetanoUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var trimmed = url.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
                return trimmed.Split('#')[0].Trim();

            var normalized = absoluteUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            var betanoView = GetBetanoView(absoluteUri.ToString());
            if (!string.IsNullOrWhiteSpace(betanoView))
                normalized = $"{normalized}/?bt={betanoView}";

            return normalized;
        }

        private static bool ShouldTraverseChildCompetition(string? parentUrl, string? childUrl)
        {
            var normalizedParentUrl = NormalizeBetanoUrl(parentUrl);
            var normalizedChildUrl = NormalizeBetanoUrl(childUrl);
            if (string.IsNullOrWhiteSpace(normalizedParentUrl) || string.IsNullOrWhiteSpace(normalizedChildUrl))
                return false;

            if (string.Equals(normalizedParentUrl, normalizedChildUrl, StringComparison.OrdinalIgnoreCase))
                return false;

            var parentBaseUrl = StripQuery(normalizedParentUrl);
            var childBaseUrl = StripQuery(normalizedChildUrl);
            var childView = GetBetanoView(normalizedChildUrl);

            return string.Equals(parentBaseUrl, childBaseUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(childView, "matchresult", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripQuery(string url)
        {
            var questionMarkIndex = url.IndexOf('?', StringComparison.Ordinal);
            return questionMarkIndex >= 0
                ? url[..questionMarkIndex]
                : url;
        }

        private static string? GetBetanoView(string? url)
        {
            var normalized = NormalizeNullable(url);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var match = Regex.Match(normalized, @"(?:\?|&)bt=(?<view>[^&#]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            var view = Uri.UnescapeDataString(match.Groups["view"].Value).Trim();
            return string.IsNullOrWhiteSpace(view)
                ? null
                : view;
        }

        private static decimal? ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Replace(",", ".", StringComparison.Ordinal).Trim();
            return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private sealed class BetanoRawMatchCandidate
        {
            public string Href { get; set; } = "";
            public string Text { get; set; } = "";
            public string? CardText { get; set; }
            public string? HomeTeam { get; set; }
            public string? AwayTeam { get; set; }
            public int Y { get; set; }
        }

        private sealed class BetanoMatchCandidate
        {
            public string Url { get; set; } = "";
            public string ListingText { get; set; } = "";
            public string? CardText { get; set; }
            public string CompetitionName { get; set; } = "";
            public string SourceMatchId { get; set; } = "";
            public DateTime? MatchDateLocal { get; set; }
            public string? ParsedHomeTeam { get; set; }
            public string? ParsedAwayTeam { get; set; }
        }

        private sealed class BetanoCompetitionLink
        {
            public string Url { get; set; } = "";
            public string Text { get; set; } = "";
            public int Y { get; set; }
            public int Depth { get; set; }
        }

        private sealed class BetanoMatchSnapshot
        {
            public string PageTitle { get; set; } = "";
            public List<string> Breadcrumbs { get; set; } = new();
            public string? LeagueCandidate { get; set; }
            public List<string> VisibleCards { get; set; } = new();
            public string? TopDateText { get; set; }

            public DateTime? MatchDateLocal
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(TopDateText))
                        return null;

                    return DateTime.TryParse(
                        TopDateText,
                        new CultureInfo("es-CL"),
                        DateTimeStyles.None,
                        out var parsed)
                        ? parsed
                        : null;
                }
            }
        }
    }
}
