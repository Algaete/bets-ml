using CornersMLData.Models;
using CornersMLData.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Services
{
    public sealed class EspnPartidosProximosScraper
    {
        private static readonly HttpClient HttpClient = BuildHttpClient();
        private readonly ILogger<EspnPartidosProximosScraper> _logger;
        private readonly TimeZoneInfo _chileTimeZone;

        public EspnPartidosProximosScraper(ILogger<EspnPartidosProximosScraper> logger)
        {
            _logger = logger;
            _chileTimeZone = ResolveChileTimeZone();
        }

        public async Task<List<PartidoProximoUpsertDto>> FetchUpcomingMatchesAsync(
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PartidoProximoUpsertDto>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var date = fromDate.Date; date <= toDate.Date; date = date.AddDays(1))
            {
                var html = await FetchStringWithRetryAsync(
                    $"https://www.espn.cl/futbol/calendario/_/fecha/{date:yyyyMMdd}",
                    cancellationToken);

                var dailyMatches = ParseSchedulePage(html, date);
                foreach (var match in dailyMatches)
                {
                    var uniqueKey = $"{match.FechaPartido:yyyy-MM-dd HH:mm:ss}|{match.EquipoLocal}|{match.EquipoVisita}|{match.Liga}";
                    if (!seen.Add(uniqueKey))
                        continue;

                    results.Add(match);
                }
            }

            _logger.LogInformation(
                "Proximos partidos ESPN descubiertos. From={FromDate:yyyy-MM-dd}, To={ToDate:yyyy-MM-dd}, Total={Total}",
                fromDate.Date,
                toDate.Date,
                results.Count);

            return results
                .OrderBy(x => x.FechaPartido)
                .ThenBy(x => x.Liga)
                .ThenBy(x => x.EquipoLocal)
                .ToList();
        }

        private List<PartidoProximoUpsertDto> ParseSchedulePage(string html, DateTime date)
        {
            var results = new List<PartidoProximoUpsertDto>();
            if (string.IsNullOrWhiteSpace(html))
                return results;

            var tokenPattern = new Regex(
                @"<div class=""Table__Title"">(?<league>.*?)</div>|<tr class=""[^""]*Table__TR[^""]*"" data-idx=""\d+"">(?<row>.*?)</tr>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            string? currentLeague = null;

            foreach (Match tokenMatch in tokenPattern.Matches(html))
            {
                if (tokenMatch.Groups["league"].Success)
                {
                    currentLeague = NormalizeNullable(StripHtml(tokenMatch.Groups["league"].Value));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentLeague) || !tokenMatch.Groups["row"].Success)
                    continue;

                var match = ParseScheduleRow(tokenMatch.Groups["row"].Value, currentLeague!, date);
                if (match != null)
                {
                    results.Add(match);
                }
            }

            return results;
        }

        private PartidoProximoUpsertDto? ParseScheduleRow(string rowHtml, string league, DateTime date)
        {
            var cells = Regex.Matches(
                    rowHtml,
                    @"<td class=""[^""]*Table__TD[^""]*"">(?<cell>.*?)</td>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Select(x => x.Groups["cell"].Value)
                .ToList();

            if (cells.Count < 3)
                return null;

            // ESPN calendario muestra primero el local y luego el rival en la columna "v Equipo".
            var homeTeam = ExtractLastAnchorText(cells[0]);
            var awayTeam = ExtractLastAnchorText(cells[1]);
            var middleAnchorText = ExtractAnchorTextByClass(cells[1], "AnchorLink at");
            var scheduleText = NormalizeNullable(StripHtml(cells[2]));

            if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
                return null;

            // Solo próximos partidos: los ya jugados traen score o "F" y no deben entrar.
            if (!string.IsNullOrWhiteSpace(middleAnchorText) && Regex.IsMatch(middleAnchorText, @"\d+\s*-\s*\d+"))
                return null;

            if (string.Equals(scheduleText, "F", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheduleText, "FT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheduleText, "Final", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var fechaPartido = ParseScheduledDateTime(date, scheduleText);
            if (!fechaPartido.HasValue)
                return null;

            return new PartidoProximoUpsertDto
            {
                FechaPartido = fechaPartido.Value,
                EquipoLocal = NormalizeRequired(homeTeam),
                EquipoVisita = NormalizeRequired(awayTeam),
                Liga = NormalizeRequired(CanonicalNameCatalog.CanonicalizeLeague(league)),
                Genero = InferGenero(league),
                EsKnockout = InferEsKnockout(league)
            };
        }

        private DateTime? ParseScheduledDateTime(DateTime date, string? scheduleText)
        {
            var normalized = NormalizeNullable(scheduleText);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (string.Equals(normalized, "TBD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "POR DEFINIR", StringComparison.OrdinalIgnoreCase))
            {
                return date.Date.AddHours(12);
            }

            if (DateTime.TryParseExact(
                normalized,
                new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedTime))
            {
                return new DateTime(
                    date.Year,
                    date.Month,
                    date.Day,
                    parsedTime.Hour,
                    parsedTime.Minute,
                    0,
                    DateTimeKind.Unspecified);
            }

            return null;
        }

        private static string InferGenero(string league)
        {
            var normalized = league.ToLowerInvariant();
            return normalized.Contains("women")
                || normalized.Contains("femen")
                || normalized.Contains("w ")
                || normalized.Contains(" w")
                ? "Femenino"
                : "Masculino";
        }

        private static bool InferEsKnockout(string league)
        {
            var normalized = league.ToLowerInvariant();
            return normalized.Contains("copa")
                || normalized.Contains("cup")
                || normalized.Contains("playoff")
                || normalized.Contains("playoffs")
                || normalized.Contains("supercopa")
                || normalized.Contains("super cup")
                || normalized.Contains("trofeo");
        }

        private static string? ExtractLastAnchorText(string html)
        {
            var matches = Regex.Matches(
                html,
                @"<a class=""AnchorLink""[^>]*>(?<text>.*?)</a>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var text = NormalizeNullable(StripHtml(matches[i].Groups["text"].Value));
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private static string? ExtractAnchorTextByClass(string html, string className)
        {
            var pattern = $@"<a class=""{Regex.Escape(className)}""[^>]*>(?<text>.*?)</a>";
            var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? NormalizeNullable(StripHtml(match.Groups["text"].Value)) : null;
        }

        private static string NormalizeRequired(string? value)
        {
            var normalized = NormalizeNullable(value);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new Exception("String requerido vacio.");

            return normalized;
        }

        private static string? NormalizeNullable(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = Regex.Replace(value, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string StripHtml(string? value)
        {
            var withoutTags = Regex.Replace(value ?? string.Empty, "<.*?>", " ");
            return System.Net.WebUtility.HtmlDecode(withoutTags);
        }

        private static async Task<string> FetchStringWithRetryAsync(string url, CancellationToken cancellationToken)
        {
            Exception? last = null;

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await HttpClient.SendAsync(req, cancellationToken);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                        throw new Exception($"HTTP {(int)resp.StatusCode} en ESPN ({url}). Body: {Truncate(body, 200)}");
                    }

                    return await resp.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < 3)
                        await Task.Delay(500 * attempt, cancellationToken);
                }
            }

            throw new Exception($"Error consultando ESPN calendario: {url}", last);
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static HttpClient BuildHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; FootballDataRep/1.0; +https://www.espn.cl/futbol/calendario)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/json");
            return client;
        }

        private static TimeZoneInfo ResolveChileTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"); }
            catch { return TimeZoneInfo.Local; }
        }
    }
}
