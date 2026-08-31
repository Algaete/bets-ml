using System.Net.Http.Json;
using System.Text.Json;
using CornersPrediction.Web.Models.BotAutomation;

namespace CornersPrediction.Web.Clients;

public sealed class RecommendationAutomationApiClient
{
    private readonly HttpClient _httpClient;

    public RecommendationAutomationApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<RecommendationBotDefinitionViewModel>> GetBotsAsync(
        CancellationToken cancellationToken) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<RecommendationBotDefinitionViewModel>>(
            "/api/recommendation-bots",
            cancellationToken) ?? [];

    public async Task<IReadOnlyList<RecommendationBotLeagueCatalogItemViewModel>> GetLeagueCatalogAsync(
        CancellationToken cancellationToken) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<RecommendationBotLeagueCatalogItemViewModel>>(
            "/api/recommendation-bots/league-catalog",
            cancellationToken) ?? [];

    public async Task<IReadOnlyList<RecommendationJobViewModel>> GetJobsAsync(
        int take,
        CancellationToken cancellationToken) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<RecommendationJobViewModel>>(
            $"/api/recommendation-jobs?take={Math.Clamp(take, 1, 200)}",
            cancellationToken) ?? [];

    public async Task<RecommendationJobViewModel> EnqueueJobAsync(
        CreateRecommendationJobViewModel request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/recommendation-jobs", request, cancellationToken);
        return await ReadAsync<RecommendationJobViewModel>(response, cancellationToken);
    }

    public async Task<RecommendationBotDefinitionViewModel> SaveBotAsync(
        SaveRecommendationBotDefinitionViewModel request,
        CancellationToken cancellationToken)
    {
        var path = request.IsNew
            ? "/api/recommendation-bots"
            : $"/api/recommendation-bots/{Uri.EscapeDataString(request.BotKey)}";
        using var response = request.IsNew
            ? await _httpClient.PostAsJsonAsync(path, request, cancellationToken)
            : await _httpClient.PutAsJsonAsync(path, request, cancellationToken);
        return await ReadAsync<RecommendationBotDefinitionViewModel>(response, cancellationToken);
    }

    public async Task CancelJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync($"/api/recommendation-jobs/{jobId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteBotAsync(string botKey, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync(
            $"/api/recommendation-bots/{Uri.EscapeDataString(botKey)}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The backend API returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var error = TryReadError(body);
        throw new InvalidOperationException(error ?? $"Backend API returned HTTP {(int)response.StatusCode}.");
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var property in new[] { "error", "detail", "title" })
            {
                if (document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}
