using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.ApiFootball;

public sealed class ApiFootballClient
{
    private readonly HttpClient _httpClient;
    private readonly ApiFootballOptions _options;
    private readonly ConcurrentDictionary<string, JsonElement> _responseCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset? _lastRequestAt;

    public ApiFootballClient(HttpClient httpClient, IOptions<ApiFootballOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string? DailyRemaining { get; private set; }
    public string? MinuteRemaining { get; private set; }

    public async Task<ApiFootballStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        var root = await GetAsync("/status", cancellationToken);
        var response = root.GetProperty("response");
        var subscription = response.GetProperty("subscription");
        var requests = response.GetProperty("requests");
        return new ApiFootballStatusResult(
            ReadString(subscription, "plan") ?? "unknown",
            ReadInt(requests, "current") ?? 0,
            ReadInt(requests, "limit") ?? 0,
            DailyRemaining,
            MinuteRemaining);
    }

    public Task<JsonElement> GetLeagueAsync(int leagueId, int season, CancellationToken cancellationToken) =>
        GetCachedAsync($"/leagues?id={leagueId}&season={season}", cancellationToken);

    public Task<JsonElement> GetFixturesAsync(
        int leagueId,
        int season,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        var query = $"/fixtures?league={leagueId}&season={season}&status=FT-AET-PEN";
        if (dateFrom.HasValue)
        {
            query += $"&from={dateFrom:yyyy-MM-dd}";
        }
        if (dateTo.HasValue)
        {
            query += $"&to={dateTo:yyyy-MM-dd}";
        }
        return GetCachedAsync(query, cancellationToken);
    }

    public Task<JsonElement> GetFixturesForDateAsync(
        DateOnly date,
        CancellationToken cancellationToken) =>
        GetAsync($"/fixtures?date={date:yyyy-MM-dd}&status=FT-AET-PEN", cancellationToken);

    public Task<JsonElement> GetFixtureAsync(long fixtureId, CancellationToken cancellationToken) =>
        GetAsync($"/fixtures?id={fixtureId}", cancellationToken);

    public Task<JsonElement> GetUpcomingFixturesForDateAsync(
        DateOnly date,
        CancellationToken cancellationToken) =>
        GetAsync(
            $"/fixtures?date={date:yyyy-MM-dd}&timezone=America%2FSantiago",
            cancellationToken);

    public Task<JsonElement> GetFixtureStatisticsAsync(long fixtureId, CancellationToken cancellationToken) =>
        // API-Football can revise post-match statistics after initially marking a
        // fixture FT. Always fetch a fresh snapshot so a reconciliation run does
        // not reuse an earlier, incomplete response from the same process.
        GetAsync($"/fixtures/statistics?fixture={fixtureId}", cancellationToken);

    public Task<JsonElement> GetFixtureLineupsAsync(long fixtureId, CancellationToken cancellationToken) =>
        GetCachedAsync($"/fixtures/lineups?fixture={fixtureId}", cancellationToken);

    public Task<JsonElement> GetStandingsAsync(int leagueId, int season, CancellationToken cancellationToken) =>
        GetAsync($"/standings?league={leagueId}&season={season}", cancellationToken);

    private async Task<JsonElement> GetCachedAsync(string path, CancellationToken cancellationToken)
    {
        if (_responseCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var response = await GetAsync(path, cancellationToken);
        _responseCache[path] = response;
        return response;
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("API_FOOTBALL_KEY is not configured.");
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await GetOnceAsync(path, cancellationToken);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw new InvalidOperationException($"API-Football request retries were exhausted for {path}.");
    }

    private async Task<JsonElement> GetOnceAsync(string path, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            if (_lastRequestAt.HasValue && _options.RequestDelayMilliseconds > 0)
            {
                var delay = TimeSpan.FromMilliseconds(_options.RequestDelayMilliseconds) -
                    (DateTimeOffset.UtcNow - _lastRequestAt.Value);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _requestGate.Release();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("x-apisports-key", _options.ApiKey);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        DailyRemaining = ReadHeader(response, "x-ratelimit-requests-remaining");
        MinuteRemaining = ReadHeader(response, "x-ratelimit-remaining");
        using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyTimeout.CancelAfter(TimeSpan.FromMinutes(5));
        var body = await response.Content.ReadAsStringAsync(bodyTimeout.Token);
        if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            throw new ApiFootballQuotaExceededException(path);
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"API-Football returned HTTP {(int)response.StatusCode} for {path}.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();
        var error = ReadApiError(root);
        if (error is not null)
        {
            throw new InvalidOperationException($"API-Football error for {path}: {error}");
        }

        return root;
    }

    private static string? ReadHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? ReadApiError(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors))
        {
            return null;
        }

        return errors.ValueKind switch
        {
            JsonValueKind.Array when errors.GetArrayLength() == 0 => null,
            JsonValueKind.Object when !errors.EnumerateObject().Any() => null,
            JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(item => item.ToString())),
            JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(item => $"{item.Name}: {item.Value}")),
            JsonValueKind.Null => null,
            _ => errors.ToString()
        };
    }

    internal static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    internal static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }
}

public sealed class ApiFootballQuotaExceededException : InvalidOperationException
{
    public ApiFootballQuotaExceededException(string path)
        : base($"API-Football returned HTTP 429 for {path}.")
    {
    }
}
