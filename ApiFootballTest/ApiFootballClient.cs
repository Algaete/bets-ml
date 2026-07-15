using System.Net;
using System.Text.Json;

namespace ApiFootballTest;

internal sealed class ApiFootballClient : IDisposable
{
    private const string BaseUrl = "https://v3.football.api-sports.io";

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, JsonElement> _cache = new(StringComparer.Ordinal);
    private readonly TimeSpan _requestDelay;
    private DateTimeOffset? _lastRequestAt;

    public ApiFootballClient(string apiKey, TimeSpan requestDelay, TimeSpan timeout)
    {
        _requestDelay = requestDelay;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = timeout
        };
        _httpClient.DefaultRequestHeaders.Add("x-apisports-key", apiKey);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CornersPrediction-ApiFootballTest/1.0");
    }

    public int NetworkRequestCount { get; private set; }
    public string? RequestsRemaining { get; private set; }
    public string? MinuteRequestsRemaining { get; private set; }

    public async Task<JsonElement> GetAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(pathAndQuery, out var cached))
        {
            return cached;
        }

        await WaitForRateLimitAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        _lastRequestAt = DateTimeOffset.UtcNow;
        NetworkRequestCount++;
        RequestsRemaining = ReadHeader(response, "x-ratelimit-requests-remaining");
        MinuteRequestsRemaining = ReadHeader(response, "x-ratelimit-remaining");

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiFootballException(
                $"API-Football returned HTTP {(int)response.StatusCode} ({response.StatusCode}) for {pathAndQuery}.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ApiFootballException($"API-Football returned invalid JSON for {pathAndQuery}.", exception);
        }

        var apiError = ReadApiError(root);
        if (apiError is not null)
        {
            throw new ApiFootballException($"API-Football error for {pathAndQuery}: {apiError}");
        }

        if (!root.TryGetProperty("response", out var responseNode) || responseNode.ValueKind != JsonValueKind.Array)
        {
            throw new ApiFootballException($"API-Football response has no response array for {pathAndQuery}.");
        }

        _cache[pathAndQuery] = root;
        return root;
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        if (_lastRequestAt is null || _requestDelay <= TimeSpan.Zero)
        {
            return;
        }

        var remainingDelay = _requestDelay - (DateTimeOffset.UtcNow - _lastRequestAt.Value);
        if (remainingDelay > TimeSpan.Zero)
        {
            await Task.Delay(remainingDelay, cancellationToken);
        }
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
}

internal sealed class ApiFootballException : Exception
{
    public ApiFootballException(string message) : base(message)
    {
    }

    public ApiFootballException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
