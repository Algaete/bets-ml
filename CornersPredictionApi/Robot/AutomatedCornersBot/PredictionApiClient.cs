using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AutomatedCornersBot.Api;

public sealed class PredictionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PredictionApiClient> _logger;

    public PredictionApiClient(
        HttpClient httpClient,
        IOptions<AutomatedBotOptions> options,
        ILogger<PredictionApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var config = options.Value.PredictionApi;
        _httpClient.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(config.InternalApiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("X-Internal-Api-Key");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Api-Key", config.InternalApiKey);
        }
    }

    public async Task<PredictionContextDto?> GetPredictionContextAsync(
        string? league,
        string homeTeam,
        string awayTeam,
        string teamGender,
        DateOnly? beforeDate,
        CancellationToken cancellationToken)
    {
        var query = $"/api/matches/prediction-context?homeTeam={Uri.EscapeDataString(homeTeam)}&awayTeam={Uri.EscapeDataString(awayTeam)}&teamGender={Uri.EscapeDataString(teamGender)}";
        if (!string.IsNullOrWhiteSpace(league))
        {
            query += $"&league={Uri.EscapeDataString(league)}";
        }
        if (beforeDate is DateOnly cutoff)
        {
            query += $"&beforeDate={cutoff:yyyy-MM-dd}";
        }

        using var response = await _httpClient.GetAsync(query, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Prediction context request failed ({(int)response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync<PredictionContextDto>(JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamBi3InfoDto>> GetTeamInfoAsync(
        string league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var query = $"/api/teams/big3-info?league={Uri.EscapeDataString(league)}&teamGender={Uri.EscapeDataString(teamGender)}";
        using var response = await _httpClient.GetAsync(query, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Could not load team big3 info for league {League}. Status {StatusCode}", league, response.StatusCode);
            return Array.Empty<TeamBi3InfoDto>();
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TeamBi3InfoDto>>(JsonOptions, cancellationToken)
            ?? Array.Empty<TeamBi3InfoDto>();
    }

    public async Task<PredictionResultDto> PredictCornersAsync(
        IReadOnlyDictionary<string, object?> features,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/predict", features, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Corners prediction request failed ({(int)response.StatusCode}): {error}");
        }

        return (await response.Content.ReadFromJsonAsync<PredictionResultDto>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Corners prediction response was empty.");
    }

    public async Task<MultiMarketPredictionDto> PredictMultiMarketAsync(
        IReadOnlyDictionary<string, object?> features,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/predict/shots-on-goal", features, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Multi-market prediction request failed ({(int)response.StatusCode}): {error}");
        }

        return (await response.Content.ReadFromJsonAsync<MultiMarketPredictionDto>(JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("Multi-market prediction response was empty.");
    }

    public async Task<OverUnderPredictionResultDto?> PredictOverUnderAsync(
        IReadOnlyDictionary<string, object?> features,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("/predict/over-under", features, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Over/under prediction failed ({StatusCode}): {Error}", response.StatusCode, error);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<OverUnderPredictionResultDto>(JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Over/under prediction could not be executed. Falling back to the corners model only.");
            return null;
        }
    }
}
