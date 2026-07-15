using CornersMLData.Data;
using CornersMLData.Models;
using Dapper;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;

namespace CornersMLData.Controllers
{
    /// <summary>
    /// Endpoints para consultar y poblar historial de partidos del futbol chileno usando scraping de Google.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ChileMatchHistoryScrappingController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly MatchHistoryRepository _matchHistoryRepository;
        private readonly ILogger<ChileMatchHistoryScrappingController> _logger;

        public ChileMatchHistoryScrappingController(
            IConfiguration configuration,
            MatchHistoryRepository matchHistoryRepository,
            ILogger<ChileMatchHistoryScrappingController> logger)
        {
            _configuration = configuration;
            _matchHistoryRepository = matchHistoryRepository;
            _logger = logger;
        }

        /// <summary>
        /// Retorna la cantidad de registros existentes en <c>MatchHistory</c> para Liga de Primera temporada 2026.
        /// </summary>
        /// <remarks>
        /// Endpoint de consulta sin parametros. Devuelve un conteo rapido para validar si la temporada 2026 ya fue cargada en historial.
        /// </remarks>
        [HttpGet("chile-2026-count")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChile2026Count()
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                return Problem("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var total = await conn.QuerySingleAsync<int>(
                @"SELECT COUNT(1)
                  FROM dbo.MatchHistory WITH (NOLOCK)
                  WHERE League = @League AND Season = @Season",
                new { League = "Liga de Primera", Season = "2026" });

            return Ok(new
            {
                league = "Liga de Primera",
                season = "2026",
                totalRecords = total
            });
        }

        /// <summary>
        /// Retorna el conteo de registros de <c>MatchHistory</c> para la temporada 2026 agrupados por liga.
        /// </summary>
        /// <remarks>
        /// Endpoint de consulta sin parametros. Agrupa el historial 2026 por nombre de liga para revisar cobertura de carga.
        /// </remarks>
        [HttpGet("season-2026-count-by-league")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSeason2026CountByLeague()
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
                return Problem("Connection string 'DefaultConnection' is not configured.");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var rows = await conn.QueryAsync(
                @"SELECT ISNULL(NULLIF(LTRIM(RTRIM(League)), ''), '(NULL)') AS League,
                         COUNT(1) AS Total
                  FROM dbo.MatchHistory WITH (NOLOCK)
                  WHERE Season = @Season
                  GROUP BY ISNULL(NULLIF(LTRIM(RTRIM(League)), ''), '(NULL)')
                  ORDER BY COUNT(1) DESC",
                new { Season = "2026" });

            return Ok(rows);
        }

        /// <summary>
        /// Scrapea resultados y estadisticas de jornadas del futbol chileno 2026 desde Google e inserta en <c>MatchHistory</c>.
        /// </summary>
        /// <remarks>
        /// Recibe parametros por <c>query string</c>. Permite limitar la corrida por cantidad de partidos, rango de jornadas y modo
        /// visual de Playwright antes de guardar los resultados en <c>MatchHistory</c>.
        /// </remarks>
        /// <param name="take">Cantidad maxima de partidos a procesar en la corrida.</param>
        /// <param name="headless">Indica si Playwright debe ejecutarse sin UI visible.</param>
        /// <param name="fromRound">Jornada inicial a consultar.</param>
        /// <param name="toRound">Jornada final opcional. Si no se indica, el scraper puede seguir avanzando automaticamente.</param>
        [ProducesResponseType(typeof(ChileHistoryBatchResponse), StatusCodes.Status200OK)]
        [HttpPost("scrape-google-chile-2026")]
        public async Task<IActionResult> ScrapeGoogleChile2026(
            [FromQuery] int take = 10,
            [FromQuery] bool headless = false,
            [FromQuery] int fromRound = 1,
            [FromQuery] int? toRound = null)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connStr))
                return Problem("Connection string 'DefaultConnection' is not configured.");

            if (take <= 0) take = 10;
            if (take > 1000) take = 1000;
            if (fromRound < 1) fromRound = 1;
            if (fromRound > 60) fromRound = 60;
            if (toRound.HasValue && toRound.Value < fromRound) toRound = fromRound;

            using var playwright = await Playwright.CreateAsync();

            var sessionDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                ".runtime",
                "tmp",
                $"playwright-google-chile-history-{Environment.ProcessId}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(sessionDir);

            var context = await playwright.Chromium.LaunchPersistentContextAsync(
                sessionDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Channel = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSER_CHANNEL"),
                    Headless = headless,
                    SlowMo = headless ? 0 : 50,
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

            var results = new List<ChileHistoryProcessResult>();

            try
            {
                var page = await context.NewPageAsync();
                await NavigateToGoogleRoundSearchAsync(page, fromRound);

                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allDiscoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allDiscoveredRounds = new HashSet<int>();
                var currentIndex = 0;
                var totalDiscovered = 0;
                var totalRounds = toRound.HasValue
                    ? Math.Min(60, Math.Max(fromRound, toRound.Value))
                    : 60;
                var emptyRoundsInARow = 0;

                for (var round = fromRound; round <= totalRounds && currentIndex < take; round++)
                {
                    await NavigateToGoogleRoundSearchAsync(page, round);
                    var expansion = await ExpandAllGoogleMatchesAsync(page);
                    var candidates = await ExtractGoogleMatchCandidatesAsync(page);

                    var discoveredRounds = candidates
                        .Where(x => x.RoundNumber.HasValue)
                        .Select(x => x.RoundNumber!.Value)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    foreach (var discoveredRound in discoveredRounds)
                        allDiscoveredRounds.Add(discoveredRound);

                    foreach (var candidate in candidates)
                        allDiscoveredKeys.Add(BuildCandidateKey(candidate));

                    totalDiscovered = allDiscoveredKeys.Count;

                    Console.WriteLine("====================================");
                    Console.WriteLine($"GOOGLE CHILE 2026 JORNADA {round}");
                    Console.WriteLine($"Clicks 'Mas partidos': {expansion.ClickCount}");
                    Console.WriteLine($"Rondas visibles en expansion: {string.Join(", ", expansion.VisibleRounds)}");
                    Console.WriteLine($"Rondas detectadas en candidatos: {string.Join(", ", discoveredRounds)}");
                    List<GoogleMatchCandidate> roundCandidates;
                    if (round == fromRound)
                    {
                        roundCandidates = candidates
                            .Where(x => x.RoundNumber.HasValue
                                && x.RoundNumber.Value >= fromRound
                                && x.RoundNumber.Value <= totalRounds)
                            .ToList();
                    }
                    else
                    {
                        roundCandidates = candidates.Where(x => x.RoundNumber == round).ToList();
                    }
                    Console.WriteLine($"Partidos detectados en jornada: {roundCandidates.Count}");
                    Console.WriteLine($"Partidos detectados acumulados: {totalDiscovered}");
                    Console.WriteLine("====================================");

                    var orderedCandidates = roundCandidates
                        .OrderBy(x => ParseChileMatchDateOrMax(x.DateLabel))
                        .ThenBy(x => x.HomeTeam)
                        .ToList();

                    if (!orderedCandidates.Any())
                    {
                        emptyRoundsInARow++;
                        if (round > 1 && emptyRoundsInARow >= 6)
                            break;
                        continue;
                    }

                    emptyRoundsInARow = 0;

                    foreach (var candidate in orderedCandidates)
                    {
                        if (currentIndex >= take)
                            break;

                        if (processedKeys.Contains(BuildCandidateKey(candidate)))
                            continue;

                        currentIndex++;
                        var result = await ProcessGoogleMatchCandidateAsync(page, conn, candidate, currentIndex, take);
                        results.Add(result);
                        processedKeys.Add(BuildCandidateKey(candidate));
                    }
                }

                if (!results.Any())
                {
                    await SaveGoogleDiscoveryDebugAsync(page, "google_chile_2026_discovery_empty");

                    return Ok(new ChileHistoryBatchResponse
                    {
                        Message = "No se encontraron partidos nuevos para procesar en Google Sports Chile 2026 por jornada.",
                        TotalDiscovered = totalDiscovered,
                        TotalProcessed = 0,
                        MinRoundDiscovered = allDiscoveredRounds.Any() ? allDiscoveredRounds.Min() : null,
                        MaxRoundDiscovered = allDiscoveredRounds.Any() ? allDiscoveredRounds.Max() : null,
                        DiscoveredRounds = allDiscoveredRounds.OrderBy(x => x).ToList()
                    });
                }

                var response = new ChileHistoryBatchResponse
                {
                    Message = "Scraping histórico Google Sports Chile 2026 finalizado.",
                    TotalDiscovered = totalDiscovered,
                    TotalProcessed = results.Count,
                    Inserted = results.Count(x => x.Status == "INSERTED"),
                    Updated = results.Count(x => x.Status == "UPDATED"),
                    Duplicates = results.Count(x => x.DuplicateDetected),
                    Skipped = results.Count(x => x.Status == "SKIPPED"),
                    Errors = results.Count(x => x.Status == "ERROR"),
                    MinRoundDiscovered = allDiscoveredRounds.Any() ? allDiscoveredRounds.Min() : null,
                    MaxRoundDiscovered = allDiscoveredRounds.Any() ? allDiscoveredRounds.Max() : null,
                    DiscoveredRounds = allDiscoveredRounds.OrderBy(x => x).ToList(),
                    Results = results
                };

                _logger.LogInformation(
                    "Resumen final Google Chile 2026. Inserted={Inserted}, Updated={Updated}, Duplicates={Duplicates}, Skipped={Skipped}, Errors={Errors}",
                    response.Inserted,
                    response.Updated,
                    response.Duplicates,
                    response.Skipped,
                    response.Errors);

                return Ok(response);
            }
            finally
            {
                try { await context.CloseAsync(); } catch { }
            }
        }

        private static async Task SaveGoogleDiscoveryDebugAsync(IPage page, string prefix)
        {
            try
            {
                var debugDir = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    ".runtime",
                    "tmp",
                    "google-history-debug");

                Directory.CreateDirectory(debugDir);

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var htmlPath = Path.Combine(debugDir, $"{prefix}_{stamp}.html");
                var pngPath = Path.Combine(debugDir, $"{prefix}_{stamp}.png");

                await System.IO.File.WriteAllTextAsync(htmlPath, await page.ContentAsync());
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = pngPath,
                    FullPage = true,
                    Timeout = 10000
                });
            }
            catch
            {
                // Best effort debug only.
            }
        }

        private static async Task DismissGoogleConsentIfPresentAsync(IPage page)
        {
            var buttons = new[]
            {
                "Aceptar todo",
                "Aceptar",
                "I agree",
                "Accept all"
            };

            foreach (var label in buttons)
            {
                var locator = page.GetByRole(AriaRole.Button, new() { Name = label });
                if (await locator.CountAsync() == 0)
                    continue;

                try
                {
                    await locator.First.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                    await page.WaitForTimeoutAsync(1000);
                    return;
                }
                catch
                {
                    // Best effort.
                }
            }
        }

        private static async Task<bool> OpenGoogleMatchesTabAsync(IPage page)
        {
            if (!IsGoogleHost(page.Url))
                return false;

            var candidates = new[]
            {
                page.Locator("g-tabs").GetByText("Partidos", new LocatorGetByTextOptions { Exact = true }),
                page.GetByRole(AriaRole.Tab, new() { Name = "Partidos" }),
                page.GetByRole(AriaRole.Button, new() { Name = "Partidos" }),
                page.GetByRole(AriaRole.Link, new() { Name = "Partidos" })
            };

            foreach (var locator in candidates)
            {
                if (await locator.CountAsync() == 0)
                    continue;

                try
                {
                    await locator.First.ClickAsync(new LocatorClickOptions { Timeout = 5000 });
                    await page.WaitForTimeoutAsync(1000);
                    if (!IsGoogleHost(page.Url))
                    {
                        try
                        {
                            await page.GoBackAsync(new PageGoBackOptions
                            {
                                WaitUntil = WaitUntilState.DOMContentLoaded,
                                Timeout = 10000
                            });
                            await page.WaitForTimeoutAsync(700);
                        }
                        catch
                        {
                            // Keep trying.
                        }

                        continue;
                    }

                    return true;
                }
                catch
                {
                    // Try next selector.
                }
            }

            return false;
        }

        private static async Task NavigateToGoogleRoundSearchAsync(IPage page, int round)
        {
            var queries = new[]
            {
                $"partidos de primera división de chile 2026 jornada {round}",
                $"liga de primera chile 2026 partidos jornada {round}",
                "partidos de primera división de chile 2026",
                "liga de primera chile 2026 partidos",
                $"partidos liga chilena jornada {round}",
                $"partidos de primera división de chile jornada {round}",
                $"liga de primera chile jornada {round}",
                $"partidos liga chilena jornada {round} 2026"
            };

            foreach (var searchQuery in queries)
            {
                var searchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(searchQuery)}";

                await page.GotoAsync(searchUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 20000
                });

                await page.WaitForTimeoutAsync(1000);
                await DismissGoogleConsentIfPresentAsync(page);
                var openedMatchesTab = await OpenGoogleMatchesTabAsync(page);
                if (!openedMatchesTab)
                    continue;
                await EnsureGoogleMatchesModalVisibleAsync(page);
                await ScrollGoogleMatchesToTopAsync(page);

                var quickCandidates = await ExtractGoogleMatchCandidatesAsync(page);
                if (quickCandidates.Any(x => x.RoundNumber == round))
                    return;
            }
        }

        private static async Task EnsureGoogleMatchesModalVisibleAsync(IPage page)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (await IsGoogleMatchesModalVisibleAsync(page))
                {
                    await ScrollGoogleMatchesToTopAsync(page);
                    return;
                }

                var clicked = await ClickExpandMatchesIfVisibleAsync(page);
                if (!clicked)
                    await ScrollGoogleMatchesOutsideAsync(page);

                await page.WaitForTimeoutAsync(800);
            }
        }

        private static async Task<bool> IsGoogleMatchesModalVisibleAsync(IPage page)
        {
            if (!IsGoogleHost(page.Url))
                return false;

            var modalLocators = new[]
            {
                page.Locator("div[role='dialog']"),
                page.Locator("div[aria-label*='Liga de Primera']"),
                page.Locator("div.OcbAbf")
            };

            foreach (var locator in modalLocators)
            {
                try
                {
                    if (await locator.CountAsync() == 0)
                        continue;

                    var box = await locator.First.BoundingBoxAsync();
                    if (box != null && box.Width > 300 && box.Height > 200)
                        return true;
                }
                catch
                {
                    // Try next locator.
                }
            }

            return false;
        }

        private static async Task<GoogleExpansionResult> ExpandAllGoogleMatchesAsync(IPage page)
        {
            var result = new GoogleExpansionResult();
            var bestMinRound = int.MaxValue;
            var stableIterations = 0;

            for (var attempt = 0; attempt < 18; attempt++)
            {
                var visibleRounds = await ExtractVisibleRoundNumbersAsync(page);
                result.VisibleRounds = visibleRounds;
                if (visibleRounds.Any())
                {
                    var minRound = visibleRounds.Min();
                    if (minRound < bestMinRound)
                    {
                        bestMinRound = minRound;
                        stableIterations = 0;
                    }
                    else
                    {
                        stableIterations++;
                    }
                }

                try
                {
                    if (await ClickExpandMatchesIfVisibleAsync(page))
                    {
                        result.ClickCount++;
                        await page.WaitForTimeoutAsync(900);
                        continue;
                    }

                    if (await TryScrollGoogleMatchesModalAsync(page))
                    {
                        await page.WaitForTimeoutAsync(800);
                        continue;
                    }

                    await ScrollGoogleMatchesOutsideAsync(page);
                    await page.WaitForTimeoutAsync(800);

                    if (bestMinRound <= 1 && stableIterations >= 2)
                        break;

                    if (stableIterations >= 4)
                        break;
                }
                catch
                {
                    await ScrollGoogleMatchesOutsideAsync(page);
                    await page.WaitForTimeoutAsync(1000);
                }
            }

            result.VisibleRounds = await ExtractVisibleRoundNumbersAsync(page);
            return result;
        }

        private static async Task ScrollGoogleMatchesUpAsync(IPage page)
        {
            if (await IsGoogleMatchesModalVisibleAsync(page))
            {
                await TryScrollGoogleMatchesModalAsync(page);
                return;
            }

            await ClickExpandMatchesIfVisibleAsync(page);
            await ScrollGoogleMatchesOutsideAsync(page);
        }

        private static async Task<bool> ClickExpandMatchesIfVisibleAsync(IPage page)
        {
            var labels = new[] { "Ver más", "Más partidos" };

            foreach (var label in labels)
            {
                var locator = page.GetByRole(AriaRole.Button, new() { Name = label });
                if (await locator.CountAsync() == 0)
                    locator = page.Locator($"text={label}");

                if (await locator.CountAsync() == 0)
                    continue;

                try
                {
                    await locator.First.ScrollIntoViewIfNeededAsync();
                    await locator.First.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                    await page.WaitForTimeoutAsync(1000);
                    return true;
                }
                catch
                {
                    // Try next label.
                }
            }

            return false;
        }

        private static async Task<bool> TryScrollGoogleMatchesModalAsync(IPage page)
        {
            var modalTargets = new[]
            {
                page.Locator("div[role='dialog']"),
                page.Locator("div[aria-label*='Liga de Primera']"),
                page.Locator("div.OcbAbf").First,
                page.Locator("g-scrolling-carousel").Locator("xpath=ancestor::div[1]")
            };

            foreach (var modalTarget in modalTargets)
            {
                try
                {
                    if (await modalTarget.CountAsync() == 0)
                        continue;

                    var box = await modalTarget.First.BoundingBoxAsync();
                    if (box != null && box.Width > 200 && box.Height > 200)
                    {
                        var hoverX = box.X + Math.Min(box.Width - 24, Math.Max(24, box.Width * 0.86));
                        var hoverY = box.Y + Math.Min(box.Height - 24, Math.Max(24, box.Height * 0.50));

                        await page.Mouse.MoveAsync((float)hoverX, (float)hoverY);
                        await page.Mouse.WheelAsync(0, -2600);
                        await page.WaitForTimeoutAsync(120);
                    }

                    var scrolled = await modalTarget.First.EvaluateAsync<bool>(
                        @"element => {
                            const nodes = [element, ...element.querySelectorAll('*')];
                            const scrollables = nodes
                                .filter(node => node instanceof HTMLElement)
                                .map(node => node)
                                .filter(node => {
                                    const style = window.getComputedStyle(node);
                                    const overflowY = style.overflowY || '';
                                    const canScrollBySize = node.scrollHeight > node.clientHeight + 40;
                                    const hasScrollableOverflow = overflowY.includes('auto') || overflowY.includes('scroll') || overflowY.includes('overlay');
                                    return canScrollBySize && (hasScrollableOverflow || node === element);
                                })
                                .sort((a, b) => {
                                    const aScore = (a.scrollHeight - a.clientHeight) + a.clientHeight;
                                    const bScore = (b.scrollHeight - b.clientHeight) + b.clientHeight;
                                    return bScore - aScore;
                                });

                            for (const node of scrollables) {
                                const before = node.scrollTop;
                                node.scrollTop = Math.max(0, node.scrollTop - 2400);
                                if (node.scrollTop !== before || before > 0) {
                                    return true;
                                }
                            }

                            return false;
                        }");

                    if (scrolled)
                        return true;
                }
                catch
                {
                    // Try next target.
                }
            }

            return false;
        }

        private static async Task ScrollGoogleMatchesOutsideAsync(IPage page)
        {
            var scrollTargets = new[]
            {
                page.Locator("div[role='main']"),
                page.Locator("div[jsname]"),
                page.Locator("body")
            };

            foreach (var target in scrollTargets)
            {
                if (await target.CountAsync() == 0)
                    continue;

                try
                {
                    await target.First.EvaluateAsync(
                        @"element => {
                            if (element === document.body) {
                                window.scrollBy(0, -1200);
                                window.scrollBy(0, -1200);
                                window.scrollBy(0, -1200);
                            } else {
                                element.scrollBy(0, -1200);
                                element.scrollBy(0, -1200);
                                element.scrollBy(0, -1200);
                            }
                        }");
                    return;
                }
                catch
                {
                    // Try next target.
                }
            }

            try
            {
                await page.Mouse.WheelAsync(0, -2400);
            }
            catch
            {
                // Best effort only.
            }
        }

        private static async Task ScrollGoogleMatchesToTopAsync(IPage page)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await ScrollGoogleMatchesUpAsync(page);
                await page.WaitForTimeoutAsync(400);

                var visibleRounds = await ExtractVisibleRoundNumbersAsync(page);
                if (visibleRounds.Count > 0 && visibleRounds.Min() <= 2)
                    return;
            }
        }

        private static bool IsGoogleHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Host.Contains("google.", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<List<GoogleMatchCandidate>> ExtractGoogleMatchCandidatesAsync(IPage page)
        {
            var html = await page.ContentAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var roundRegex = new Regex(@"^Jornada\s+(\d+)\s+de\s+(\d+)$", RegexOptions.IgnoreCase);
            var dateRegex = new Regex(@"(\d{1,2}/\d{1,2})", RegexOptions.IgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<GoogleMatchCandidate>();

            var sections = doc.DocumentNode.SelectNodes("//div[contains(@class,'OcbAbf')]");
            if (sections == null || sections.Count == 0)
                return items;

            foreach (var section in sections)
            {
                var headingNode = section.SelectSingleNode(".//*[@role='heading' and @data-title]");
                var headingText = NormalizeNullable(
                    headingNode?.GetAttributeValue("data-title", null)
                    ?? HtmlEntity.DeEntitize(headingNode?.InnerText ?? string.Empty));

                if (string.IsNullOrWhiteSpace(headingText))
                    continue;

                var roundMatch = roundRegex.Match(headingText);
                if (!roundMatch.Success)
                    continue;

                var roundNumber = int.Parse(roundMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var matchNodes = section.SelectNodes(".//div[@jscontroller='ThULI']");
                if (matchNodes == null || matchNodes.Count == 0)
                    continue;

                foreach (var matchNode in matchNodes)
                {
                    var teamTexts = matchNode
                        .SelectNodes(".//*[contains(@class,'xNfnlf')]")
                        ?.Select(x => NormalizeNullable(HtmlEntity.DeEntitize(x.InnerText)))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (teamTexts == null || teamTexts.Count < 2)
                        continue;

                    var homeTeam = teamTexts[0]!;
                    var awayTeam = teamTexts[1]!;
                    var nodeText = NormalizeNullable(HtmlEntity.DeEntitize(matchNode.InnerText)) ?? string.Empty;
                    if (!nodeText.Contains("Fin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dateMatch = dateRegex.Match(nodeText);
                    var dateLabel = dateMatch.Success ? dateMatch.Groups[1].Value : null;
                    var scoreNodes = matchNode
                        .SelectNodes(".//div[contains(@class,'imspo_mt__t-sc')]//div[contains(@class,'imspo_mt__tt-w')]")
                        ?.Select(x => NormalizeNullable(HtmlEntity.DeEntitize(x.InnerText)))
                        .Where(x => !string.IsNullOrWhiteSpace(x) && Regex.IsMatch(x!, @"^\d+$"))
                        .Select(x => int.Parse(x!, CultureInfo.InvariantCulture))
                        .ToList();
                    var fallbackScore = ExtractFlexibleScorePair(nodeText);
                    var key = $"{roundNumber}|{homeTeam}|{awayTeam}|{dateLabel}";

                    if (!seen.Add(key))
                        continue;

                    items.Add(new GoogleMatchCandidate
                    {
                        RoundLabel = headingText,
                        RoundNumber = roundNumber,
                        MatchCardXPath = BuildGoogleNodeXPath(matchNode),
                        HomeTeam = homeTeam,
                        AwayTeam = awayTeam,
                        DateLabel = dateLabel,
                        HomeGoals = scoreNodes != null && scoreNodes.Count > 0 ? scoreNodes[0] : fallbackScore?.HomeGoals,
                        AwayGoals = scoreNodes != null && scoreNodes.Count > 1 ? scoreNodes[1] : fallbackScore?.AwayGoals
                    });
                }
            }

            return items;
        }

        private static string BuildCandidateKey(GoogleMatchCandidate candidate)
        {
            return $"{candidate.RoundNumber}|{NormalizeNullable(candidate.DateLabel)}|{NormalizeNullable(candidate.HomeTeam)}|{NormalizeNullable(candidate.AwayTeam)}";
        }

        private static async Task<List<int>> ExtractVisibleRoundNumbersAsync(IPage page)
        {
            var html = await page.ContentAsync();
            var matches = Regex.Matches(
                html,
                @"Jornada\s+(\d+)\s+de\s+\d+",
                RegexOptions.IgnoreCase);

            return matches
                .Select(x => int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture))
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        private static string? BuildGoogleNodeXPath(HtmlNode? node)
        {
            if (node == null)
                return null;

            var segments = new Stack<string>();
            var current = node;

            while (current != null && current.NodeType == HtmlNodeType.Element)
            {
                var index = 1;
                var sibling = current.PreviousSibling;
                while (sibling != null)
                {
                    if (sibling.NodeType == HtmlNodeType.Element && sibling.Name == current.Name)
                        index++;

                    sibling = sibling.PreviousSibling;
                }

                segments.Push($"{current.Name}[{index}]");
                current = current.ParentNode;
            }

            return "/" + string.Join("/", segments);
        }

        private async Task<ChileHistoryProcessResult> ProcessGoogleMatchCandidateAsync(
            IPage page,
            SqlConnection conn,
            GoogleMatchCandidate candidate,
            int index,
            int total)
        {
            MatchHistoryUpsertDto? extractedMatch = null;

            try
            {
                Console.WriteLine("====================================");
                Console.WriteLine($"PROCESANDO HISTORICO CHILE {index}/{total}");
                Console.WriteLine($"{candidate.RoundLabel} - {candidate.HomeTeam} vs {candidate.AwayTeam}");
                Console.WriteLine("====================================");

                await OpenGoogleMatchCardSafelyAsync(page, candidate);

                var details = await ExtractGoogleMatchDetailsAsync(page, candidate);
                extractedMatch = details;
                if ((details.HomeGoals == 0 && details.AwayGoals == 0)
                    && (candidate.HomeGoals.HasValue || candidate.AwayGoals.HasValue))
                {
                    details.HomeGoals = candidate.HomeGoals ?? details.HomeGoals;
                    details.AwayGoals = candidate.AwayGoals ?? details.AwayGoals;
                }

                var validationError = ValidateHistoryRow(details);

                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    await CloseMatchDialogAsync(page);

                    return new ChileHistoryProcessResult
                    {
                        Index = index,
                        Round = candidate.RoundLabel,
                        HomeTeam = candidate.HomeTeam,
                        AwayTeam = candidate.AwayTeam,
                        Status = "SKIPPED",
                        Error = validationError,
                        Match = details
                    };
                }

                try
                {
                    var persistResult = await _matchHistoryRepository.UpsertMatchHistoryAsync(details);
                    await CloseMatchDialogAsync(page);

                    return new ChileHistoryProcessResult
                    {
                        Index = index,
                        Round = candidate.RoundLabel,
                        HomeTeam = details.HomeTeam,
                        AwayTeam = details.AwayTeam,
                        Status = persistResult.Status == MatchHistoryPersistStatus.Inserted ? "INSERTED" : "UPDATED",
                        InsertedId = persistResult.MatchId,
                        DuplicateDetected = persistResult.DuplicateDetected,
                        Match = details
                    };
                }
                catch (SqlException sqlEx) when (MatchHistoryRepository.IsControlledSqlException(sqlEx))
                {
                    _logger.LogWarning(
                        "Error SQL controlado en scraping Chile. SQL={SqlNumber}, Message={SqlMessage}, Round={Round}, HomeTeam={HomeTeam}, AwayTeam={AwayTeam}",
                        sqlEx.Number,
                        sqlEx.Message,
                        candidate.RoundLabel,
                        details.HomeTeam,
                        details.AwayTeam);
                    await CloseMatchDialogAsync(page);

                    return new ChileHistoryProcessResult
                    {
                        Index = index,
                        Round = candidate.RoundLabel,
                        HomeTeam = details.HomeTeam,
                        AwayTeam = details.AwayTeam,
                        Status = "SKIPPED",
                        Error = $"SQL {sqlEx.Number}: {sqlEx.Message}",
                        Detail = sqlEx.Message,
                        Match = details
                    };
                }
            }
            catch (Exception ex)
            {
                try { await CloseMatchDialogAsync(page); } catch { }

                return new ChileHistoryProcessResult
                {
                    Index = index,
                    Round = candidate.RoundLabel,
                    HomeTeam = candidate.HomeTeam,
                    AwayTeam = candidate.AwayTeam,
                    Status = "ERROR",
                    Error = ex.Message,
                    Detail = ex.ToString(),
                    Match = extractedMatch
                };
            }
        }

        private static async Task OpenGoogleMatchCardSafelyAsync(IPage page, GoogleMatchCandidate candidate)
        {
            var card = await FindGoogleMatchCardLocatorAsync(page, candidate);
            if (card == null)
                throw new Exception("No se encontró la tarjeta del partido para abrir el detalle.");

            await card.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions
            {
                Timeout = 5000
            });
            await page.WaitForTimeoutAsync(300);

            var clickTargets = new List<ILocator>();

            if (!string.IsNullOrWhiteSpace(candidate.HomeTeam))
                clickTargets.Add(card.GetByText(candidate.HomeTeam, new LocatorGetByTextOptions { Exact = true }));

            if (!string.IsNullOrWhiteSpace(candidate.AwayTeam))
                clickTargets.Add(card.GetByText(candidate.AwayTeam, new LocatorGetByTextOptions { Exact = true }));

            clickTargets.Add(card.Locator(".xNfnlf"));
            clickTargets.Add(card);

            foreach (var target in clickTargets)
            {
                if (await target.CountAsync() == 0)
                    continue;

                try
                {
                    await target.First.ClickAsync(new LocatorClickOptions { Timeout = 4000 });
                    await page.WaitForTimeoutAsync(900);

                    if (IsYouTubeHost(page.Url))
                    {
                        await page.GoBackAsync(new PageGoBackOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 10000
                        });
                        await page.WaitForTimeoutAsync(900);
                        await EnsureGoogleMatchesModalVisibleAsync(page);
                        await ScrollGoogleMatchesToTopAsync(page);
                        continue;
                    }

                    if (await IsMatchDetailsPanelOpenAsync(page))
                        return;
                }
                catch
                {
                    // Try next click target.
                }
            }

            var box = await card.BoundingBoxAsync();
            if (box != null)
            {
                var clickX = box.X + Math.Min(box.Width * 0.24, 180);
                var clickY = box.Y + Math.Max(42, Math.Min(box.Height * 0.5, box.Height - 24));
                await page.Mouse.ClickAsync((float)clickX, (float)clickY);
                await page.WaitForTimeoutAsync(900);

                if (IsYouTubeHost(page.Url))
                {
                    await page.GoBackAsync(new PageGoBackOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 10000
                    });
                    await page.WaitForTimeoutAsync(900);
                    throw new Exception("Se abrió YouTube al intentar abrir el partido; reintenta en la siguiente iteración.");
                }

                if (await IsMatchDetailsPanelOpenAsync(page))
                    return;
            }

            throw new Exception("No se pudo abrir el detalle del partido sin activar el link de video.");
        }

        private static async Task<ILocator?> FindGoogleMatchCardLocatorAsync(IPage page, GoogleMatchCandidate candidate)
        {
            var locatorCandidates = new List<ILocator>();

            if (!string.IsNullOrWhiteSpace(candidate.HomeTeam) && !string.IsNullOrWhiteSpace(candidate.AwayTeam))
            {
                var home = ToXPathLiteral(candidate.HomeTeam!);
                var away = ToXPathLiteral(candidate.AwayTeam!);
                var byTeamsXPath =
                    $"//div[@jscontroller='ThULI'][.//*[contains(@class,'xNfnlf') and normalize-space()={home}] and .//*[contains(@class,'xNfnlf') and normalize-space()={away}]]";
                locatorCandidates.Add(page.Locator($"xpath={byTeamsXPath}"));
            }

            if (!string.IsNullOrWhiteSpace(candidate.MatchCardXPath))
                locatorCandidates.Add(page.Locator($"xpath={candidate.MatchCardXPath}"));

            foreach (var locator in locatorCandidates)
            {
                try
                {
                    var visible = await FirstVisibleLocatorAsync(locator);
                    if (visible != null)
                        return visible;
                }
                catch
                {
                    // Try next locator candidate.
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate.HomeTeam) && !string.IsNullOrWhiteSpace(candidate.AwayTeam))
            {
                var allCards = page.Locator("div[jscontroller='ThULI']");
                var count = await allCards.CountAsync();

                for (var i = 0; i < count && i < 120; i++)
                {
                    var card = allCards.Nth(i);
                    try
                    {
                        var box = await card.BoundingBoxAsync();
                        if (box == null || box.Width <= 0 || box.Height <= 0)
                            continue;

                        var text = NormalizeNullable(await card.InnerTextAsync()) ?? string.Empty;
                        if (text.Contains(candidate.HomeTeam!, StringComparison.OrdinalIgnoreCase)
                            && text.Contains(candidate.AwayTeam!, StringComparison.OrdinalIgnoreCase))
                        {
                            return card;
                        }
                    }
                    catch
                    {
                        // Keep scanning.
                    }
                }
            }

            return null;
        }

        private static async Task<ILocator?> FirstVisibleLocatorAsync(ILocator locator)
        {
            var count = await locator.CountAsync();
            for (var i = 0; i < count && i < 40; i++)
            {
                var candidate = locator.Nth(i);
                try
                {
                    var box = await candidate.BoundingBoxAsync();
                    if (box != null && box.Width > 0 && box.Height > 0)
                        return candidate;
                }
                catch
                {
                    // Check next candidate.
                }
            }

            return null;
        }

        private static string ToXPathLiteral(string value)
        {
            if (!value.Contains('\''))
                return $"'{value}'";

            if (!value.Contains('"'))
                return $"\"{value}\"";

            var parts = value.Split('\'');
            return "concat('" + string.Join("',\"'\",'", parts) + "')";
        }

        private static async Task<bool> IsMatchDetailsPanelOpenAsync(IPage page)
        {
            if (IsYouTubeHost(page.Url))
                return false;

            var detailLocators = new[]
            {
                page.GetByRole(AriaRole.Tab, new() { Name = "CRONOLOGÍA" }),
                page.GetByRole(AriaRole.Tab, new() { Name = "ESTADÍSTICAS" }),
                page.GetByRole(AriaRole.Tab, new() { Name = "ALINEACIONES" }),
                page.GetByText("CRONOLOGÍA"),
                page.GetByText("ESTADÍSTICAS"),
                page.GetByText("ALINEACIONES")
            };

            foreach (var locator in detailLocators)
            {
                try
                {
                    if (await locator.CountAsync() > 0)
                        return true;
                }
                catch
                {
                    // Try next locator.
                }
            }

            return false;
        }

        private static bool IsYouTubeHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Host.Contains("youtube.", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<GoogleMatchCandidate> RefreshCandidateForRoundAsync(IPage page, int round, GoogleMatchCandidate candidate)
        {
            await NavigateToGoogleRoundSearchAsync(page, round);
            await ExpandAllGoogleMatchesAsync(page);

            var candidates = await ExtractGoogleMatchCandidatesAsync(page);
            var refreshed = candidates.FirstOrDefault(x =>
                x.RoundNumber == candidate.RoundNumber
                && string.Equals(NormalizeNullable(x.HomeTeam), NormalizeNullable(candidate.HomeTeam), StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeNullable(x.AwayTeam), NormalizeNullable(candidate.AwayTeam), StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeNullable(x.DateLabel), NormalizeNullable(candidate.DateLabel), StringComparison.OrdinalIgnoreCase));

            if (refreshed == null)
            {
                throw new Exception(
                    $"No se pudo reubicar el partido en Google tras reconstruir el calendario: {candidate.RoundLabel} - {candidate.HomeTeam} vs {candidate.AwayTeam} ({candidate.DateLabel}).");
            }

            return refreshed;
        }

        private async Task<MatchHistoryUpsertDto> ExtractGoogleMatchDetailsAsync(IPage page, GoogleMatchCandidate candidate)
        {
            await OpenMatchTabAsync(page, "ESTADÍSTICAS");
            await page.WaitForTimeoutAsync(700);

            var statsText = NormalizeNullable(await page.Locator("body").InnerTextAsync()) ?? string.Empty;
            var scorePair = ExtractFlexibleScorePair(statsText);
            var homeGoals = scorePair?.HomeGoals ?? candidate.HomeGoals;
            var awayGoals = scorePair?.AwayGoals ?? candidate.AwayGoals;

            var details = new GoogleMatchDetailsDto
            {
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                HomeShots = ExtractMirrorStat(statsText, "Remates")?.Left,
                AwayShots = ExtractMirrorStat(statsText, "Remates")?.Right,
                HomeShotsOnGoal = ExtractMirrorStat(statsText, "Remates al arco")?.Left,
                AwayShotsOnGoal = ExtractMirrorStat(statsText, "Remates al arco")?.Right,
                HomePossession = ExtractMirrorStat(statsText, "Posesi[oó]n")?.Left,
                AwayPossession = ExtractMirrorStat(statsText, "Posesi[oó]n")?.Right,
                HomeCorners = ExtractMirrorStat(statsText, "Tiros de esquina")?.Left,
                AwayCorners = ExtractMirrorStat(statsText, "Tiros de esquina")?.Right
            };

            await OpenMatchTabAsync(page, "ALINEACIONES");
            await page.WaitForTimeoutAsync(900);

            var lineupText = NormalizeNullable(await page.Locator("body").InnerTextAsync()) ?? string.Empty;
            details.Formations = Regex.Matches(lineupText, @"\b\d(?:-\d){2,4}\b")
                .Select(x => x.Value)
                .Distinct()
                .ToList();

            var matchDate = ParseChileMatchDate(candidate.DateLabel);

            return new MatchHistoryUpsertDto
            {
                League = "Liga de Primera",
                Season = "2026",
                MatchDate = matchDate,
                IsKnockout = false,
                HomeTeam = NormalizeRequired(candidate.HomeTeam),
                AwayTeam = NormalizeRequired(candidate.AwayTeam),
                HomeFormation = NormalizeNullable(details.Formations.ElementAtOrDefault(0)),
                AwayFormation = NormalizeNullable(details.Formations.ElementAtOrDefault(1)),
                HomeGoals = details.HomeGoals,
                AwayGoals = details.AwayGoals,
                HomeCorners = ParseNullableInt(details.HomeCorners),
                AwayCorners = ParseNullableInt(details.AwayCorners),
                HomeShots = ParseNullableInt(details.HomeShots),
                AwayShots = ParseNullableInt(details.AwayShots),
                HomeShotsOnGoal = ParseNullableInt(details.HomeShotsOnGoal),
                AwayShotsOnGoal = ParseNullableInt(details.AwayShotsOnGoal),
                HomePossession = ParseNullablePossession(details.HomePossession),
                AwayPossession = ParseNullablePossession(details.AwayPossession),
                SourceMatchId = null,
                HomeTeamGender = "M",
                AwayTeamGender = "M"
            };
        }

        private static async Task OpenMatchTabAsync(IPage page, string tabName)
        {
            var candidates = new[]
            {
                page.GetByRole(AriaRole.Tab, new() { Name = tabName }),
                page.GetByRole(AriaRole.Button, new() { Name = tabName }),
                page.Locator($"text={tabName}")
            };

            foreach (var locator in candidates)
            {
                if (await locator.CountAsync() == 0)
                    continue;

                try
                {
                    await locator.First.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                    return;
                }
                catch
                {
                    // Try next locator.
                }
            }
        }

        private static MirrorStat? ExtractMirrorStat(string text, string labelPattern)
        {
            var regex = new Regex($@"(\d+%?)\s+{labelPattern}\s+(\d+%?)", RegexOptions.IgnoreCase);
            var match = regex.Match(text);
            if (!match.Success)
                return null;

            return new MirrorStat
            {
                Left = NormalizeNullable(match.Groups[1].Value),
                Right = NormalizeNullable(match.Groups[2].Value)
            };
        }

        private static ScorePair? ExtractFlexibleScorePair(string text)
        {
            var scoreMatch = Regex.Match(text, @"\b(\d+)\s+Fin\s+(\d+)\b", RegexOptions.IgnoreCase);
            if (scoreMatch.Success)
            {
                return new ScorePair
                {
                    HomeGoals = int.Parse(scoreMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                    AwayGoals = int.Parse(scoreMatch.Groups[2].Value, CultureInfo.InvariantCulture)
                };
            }

            var tokens = Regex
                .Split(text, @"\s+")
                .Select(NormalizeNullable)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var finIndex = tokens.FindIndex(x => x!.Equals("Fin", StringComparison.OrdinalIgnoreCase));
            if (finIndex < 0)
                return null;

            var previousNumbers = tokens
                .Take(finIndex)
                .Where(x => Regex.IsMatch(x!, @"^\d+$"))
                .TakeLast(2)
                .Select(x => int.Parse(x!, CultureInfo.InvariantCulture))
                .ToList();

            if (previousNumbers.Count == 2)
            {
                return new ScorePair
                {
                    HomeGoals = previousNumbers[0],
                    AwayGoals = previousNumbers[1]
                };
            }

            var prevNumber = tokens
                .Take(finIndex)
                .Reverse<string?>()
                .FirstOrDefault(x => Regex.IsMatch(x!, @"^\d+$"));

            var nextNumber = tokens
                .Skip(finIndex + 1)
                .FirstOrDefault(x => Regex.IsMatch(x!, @"^\d+$"));

            if (prevNumber != null && nextNumber != null)
            {
                return new ScorePair
                {
                    HomeGoals = int.Parse(prevNumber, CultureInfo.InvariantCulture),
                    AwayGoals = int.Parse(nextNumber, CultureInfo.InvariantCulture)
                };
            }

            return null;
        }

        private static async Task CloseMatchDialogAsync(IPage page)
        {
            try
            {
                if (await page.GetByRole(AriaRole.Button, new() { Name = "Cerrar" }).CountAsync() > 0)
                {
                    await page.GetByRole(AriaRole.Button, new() { Name = "Cerrar" }).First.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                    await page.WaitForTimeoutAsync(500);
                    return;
                }
            }
            catch
            {
                // Fall back to Escape.
            }

            try
            {
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(500);
            }
            catch
            {
                // Best effort.
            }
        }

        private static DateTime ParseChileMatchDate(string? dateLabel)
        {
            if (!string.IsNullOrWhiteSpace(dateLabel)
                && DateTime.TryParseExact(
                    dateLabel,
                    "d/M",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return new DateTime(2026, parsed.Month, parsed.Day);
            }

            throw new Exception($"No se pudo interpretar la fecha del partido: '{dateLabel}'.");
        }

        private static DateTime ParseChileMatchDateOrMax(string? dateLabel)
        {
            if (string.IsNullOrWhiteSpace(dateLabel))
                return DateTime.MaxValue;

            if (DateTime.TryParseExact(
                dateLabel,
                "d/M",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            {
                return new DateTime(2026, parsed.Month, parsed.Day);
            }

            return DateTime.MaxValue;
        }

        private static string NormalizeRequired(string? value)
        {
            var normalized = NormalizeNullable(value);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new Exception("String requerido vacío.");

            return normalized;
        }

        private static string? NormalizeNullable(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = Regex.Replace(value, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static int? ParseNullableInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Replace("%", "").Trim();
            if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return null;

            return result;
        }

        private static decimal? ParseNullablePossession(string? value)
        {
            var number = ParseNullableInt(value);
            return number;
        }

        private static string? ValidateHistoryRow(MatchHistoryUpsertDto row)
        {
            if (row.HomeTeam.Equals(row.AwayTeam, StringComparison.OrdinalIgnoreCase))
                return "HomeTeam y AwayTeam no pueden ser iguales.";

            if (row.HomePossession.HasValue && (row.HomePossession < 0 || row.HomePossession > 100))
                return "HomePossession fuera de rango.";

            if (row.AwayPossession.HasValue && (row.AwayPossession < 0 || row.AwayPossession > 100))
                return "AwayPossession fuera de rango.";

            return null;
        }

        private class GoogleMatchDetailsDto
        {
            public int? HomeGoals { get; set; }
            public int? AwayGoals { get; set; }
            public string? HomeShots { get; set; }
            public string? AwayShots { get; set; }
            public string? HomeShotsOnGoal { get; set; }
            public string? AwayShotsOnGoal { get; set; }
            public string? HomePossession { get; set; }
            public string? AwayPossession { get; set; }
            public string? HomeCorners { get; set; }
            public string? AwayCorners { get; set; }
            public List<string> Formations { get; set; } = new();
        }

        private class MirrorStat
        {
            public string? Left { get; set; }
            public string? Right { get; set; }
        }

        private class ScorePair
        {
            public int HomeGoals { get; set; }
            public int AwayGoals { get; set; }
        }

        private class GoogleMatchCandidate
        {
            public string? RoundLabel { get; set; }
            public int? RoundNumber { get; set; }
            public string? MatchCardXPath { get; set; }
            public string? HomeTeam { get; set; }
            public string? AwayTeam { get; set; }
            public string? DateLabel { get; set; }
            public int? HomeGoals { get; set; }
            public int? AwayGoals { get; set; }
        }

        public class ChileHistoryBatchResponse
        {
            public string? Message { get; set; }
            public int TotalDiscovered { get; set; }
            public int TotalProcessed { get; set; }
            public int Inserted { get; set; }
            public int Updated { get; set; }
            public int Duplicates { get; set; }
            public int Skipped { get; set; }
            public int Errors { get; set; }
            public int? MinRoundDiscovered { get; set; }
            public int? MaxRoundDiscovered { get; set; }
            public List<int> DiscoveredRounds { get; set; } = new();
            public List<ChileHistoryProcessResult> Results { get; set; } = new();
        }

        public class ChileHistoryProcessResult
        {
            public int Index { get; set; }
            public string? Round { get; set; }
            public string? HomeTeam { get; set; }
            public string? AwayTeam { get; set; }
            public string Status { get; set; } = "";
            public long? InsertedId { get; set; }
            public bool DuplicateDetected { get; set; }
            public string? Error { get; set; }
            public string? Detail { get; set; }
            public MatchHistoryUpsertDto? Match { get; set; }
        }

        private class GoogleExpansionResult
        {
            public int ClickCount { get; set; }
            public List<int> VisibleRounds { get; set; } = new();
        }
    }
}
