using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.ApiFootball;

public sealed class ApiFootballClient
{
    private readonly HttpClient _httpClient;
    private readonly ApiFootballOptions _options;
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
        GetAsync($"/leagues?id={leagueId}&season={season}", cancellationToken);

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
        return GetAsync(query, cancellationToken);
    }

    public Task<JsonElement> GetFixturesForDateAsync(
        DateOnly date,
        CancellationToken cancellationToken) =>
        GetAsync($"/fixtures?date={date:yyyy-MM-dd}&status=FT-AET-PEN", cancellationToken);

    public Task<JsonElement> GetFixtureStatisticsAsync(long fixtureId, CancellationToken cancellationToken) =>
        GetAsync($"/fixtures/statistics?fixture={fixtureId}", cancellationToken);

    public Task<JsonElement> GetFixtureLineupsAsync(long fixtureId, CancellationToken cancellationToken) =>
        GetAsync($"/fixtures/lineups?fixture={fixtureId}", cancellationToken);

    public Task<JsonElement> GetStandingsAsync(int leagueId, int season, CancellationToken cancellationToken) =>
        GetAsync($"/standings?league={leagueId}&season={season}", cancellationToken);

    private async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("API_FOOTBALL_KEY is not configured.");
        }

        if (_lastRequestAt.HasValue && _options.RequestDelayMilliseconds > 0)
        {
            var delay = TimeSpan.FromMilliseconds(_options.RequestDelayMilliseconds) -
                (DateTimeOffset.UtcNow - _lastRequestAt.Value);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("x-apisports-key", _options.ApiKey);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        _lastRequestAt = DateTimeOffset.UtcNow;
        DailyRemaining = ReadHeader(response, "x-ratelimit-requests-remaining");
        MinuteRemaining = ReadHeader(response, "x-ratelimit-remaining");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
