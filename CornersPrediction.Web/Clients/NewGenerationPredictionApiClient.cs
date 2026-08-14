using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CornersPrediction.Web.Models.NewGeneration;

namespace CornersPrediction.Web.Clients;

public sealed class NewGenerationPredictionApiClient
{
    private readonly HttpClient _httpClient;

    public NewGenerationPredictionApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<NewGenerationModelCatalogViewModel> GetModelCatalogAsync(CancellationToken cancellationToken) =>
        await _httpClient.GetFromJsonAsync<NewGenerationModelCatalogViewModel>(
            "/api/ml/models-2026/model-info",
            cancellationToken) ?? new NewGenerationModelCatalogViewModel
        {
            Error = "Backend API returned an empty model-info response."
        };

    public async Task<NewGenerationModelCatalogViewModel> GetHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("/api/ml/models-2026/health", cancellationToken);
        var catalog = await response.Content.ReadFromJsonAsync<NewGenerationModelCatalogViewModel>(
            cancellationToken: cancellationToken);
        return catalog ?? new NewGenerationModelCatalogViewModel
        {
            Status = response.IsSuccessStatusCode ? "unknown" : "unhealthy",
            Error = $"Backend API returned HTTP {(int)response.StatusCode}."
        };
    }

    public async Task<NewGenerationBatchPredictionResultViewModel> PredictAllAsync(
        NewGenerationPredictViewModel request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/ml/models-2026/predict",
            request,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = ReadError(body) ?? $"Backend API returned HTTP {(int)response.StatusCode}.";
            throw new NewGenerationApiException(response.StatusCode, error);
        }
        return await response.Content.ReadFromJsonAsync<NewGenerationBatchPredictionResultViewModel>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Backend API returned an empty multi-model prediction response.");
    }

    private static string? ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString();
            }
            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString();
            }
        }
        catch (JsonException)
        {
        }
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}

public sealed class NewGenerationApiException : InvalidOperationException
{
    public NewGenerationApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
