using System.Globalization;
using System.Net;
using System.Text.Json;
using CornersPrediction.Application.FootballIntelligence;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.FootballIntelligence;

public sealed class BraveNewsSearchProvider : INewsSearchProvider
{
    private readonly HttpClient _httpClient;
    private readonly NewsSearchOptions _options;
    private readonly ILogger<BraveNewsSearchProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastRequestAt;

    public BraveNewsSearchProvider(
        HttpClient httpClient,
        IOptions<NewsSearchOptions> options,
        ILogger<BraveNewsSearchProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<NewsSearchResult>> SearchAsync(
        NewsSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || !_options.Provider.Equals("Brave", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "News search skipped for FixtureId={FixtureId}, TeamId={TeamId}: provider or API key is not configured",
                request.FixtureId,
                request.TeamId);
            return [];
        }

        await ThrottleAsync(cancellationToken);
        var count = Math.Clamp(
            Math.Min(request.MaximumResults, _options.MaximumResultsPerQuery),
            1,
            20);
        var path = $"web/search?q={Uri.EscapeDataString(request.Query)}&count={count}";
        if (request.LanguageCode is "en" or "es" or "pt")
            path += $"&search_lang={request.LanguageCode}";

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, path);
            message.Headers.Accept.ParseAdd("application/json");
            message.Headers.TryAddWithoutValidation("X-Subscription-Token", _options.ApiKey);
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 2);
                await Task.Delay(retryAfter, cancellationToken);
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Parse(document.RootElement, count);
        }
        return [];
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_lastRequestAt.HasValue)
            {
                var remaining = TimeSpan.FromMilliseconds(_options.MinimumRequestDelayMilliseconds)
                    - (DateTimeOffset.UtcNow - _lastRequestAt.Value);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken);
            }
            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyCollection<NewsSearchResult> Parse(JsonElement root, int maximum)
    {
        var results = new List<NewsSearchResult>();
        AddCollection(root, "news", results);
        AddCollection(root, "web", results);
        return results
            .GroupBy(value => value.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(maximum)
            .ToArray();
    }

    private static void AddCollection(
        JsonElement root,
        string property,
        ICollection<NewsSearchResult> target)
    {
        if (!root.TryGetProperty(property, out var group)
            || !group.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in results.EnumerateArray())
        {
            var urlText = ReadString(item, "url");
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
                continue;
            var published = ParseDate(ReadString(item, "page_age"))
                ?? ParseDate(ReadString(item, "age"));
            target.Add(new NewsSearchResult(
                uri,
                ReadString(item, "title") ?? string.Empty,
                ReadString(item, "description"),
                uri.Host.ToLowerInvariant(),
                published,
                ReadString(item, "language")));
        }
    }

    private static DateTime? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed.UtcDateTime
            : null;

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;
}
