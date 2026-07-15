using System.Net.Http.Json;
using CornersPrediction.Web.Models.RobotPanel;

namespace CornersPrediction.Web.Clients;

public sealed class CornersPipelineApiClient
{
    private readonly HttpClient _httpClient;

    public CornersPipelineApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<RobotPanelStepResultViewModel> RunMatchHistoryAsync(int days, CancellationToken cancellationToken) =>
        PostStepAsync("/api/corners-pipeline/match-history", new RobotPanelStepRequestViewModel { Days = days }, cancellationToken);

    public Task<RobotPanelStepResultViewModel> RunWorldCupMatchHistoryAsync(int days, CancellationToken cancellationToken) =>
        PostStepAsync("/api/corners-pipeline/world-cup-match-history", new RobotPanelStepRequestViewModel { Days = days }, cancellationToken);

    public Task<RobotPanelStepResultViewModel> RunUpcomingMatchesAsync(int days, CancellationToken cancellationToken) =>
        PostStepAsync("/api/corners-pipeline/upcoming-matches", new RobotPanelStepRequestViewModel { Days = days }, cancellationToken);

    public Task<RobotPanelStepResultViewModel> RunPinnacleOddsAsync(CancellationToken cancellationToken) =>
        PostStepAsync("/api/corners-pipeline/pinnacle-odds", body: null, cancellationToken);

    public Task<RobotPanelStepResultViewModel> RunBetanoOddsAsync(CancellationToken cancellationToken) =>
        PostStepAsync("/api/corners-pipeline/betano-odds", body: null, cancellationToken);

    public Task<RobotPanelStepResultViewModel> RunBotsAsync(bool excludeExistingSelections, CancellationToken cancellationToken) =>
        PostStepAsync(
            "/api/corners-pipeline/bots",
            new RobotPanelBotsRequestViewModel { ExcludeExistingSelections = excludeExistingSelections },
            cancellationToken);

    public async Task<RobotPanelRunResultViewModel> RunFullPipelineAsync(
        int matchHistoryDays,
        int upcomingDays,
        bool excludeExistingSelections,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/corners-pipeline/full-run",
            new RobotPanelFullRunRequestViewModel
            {
                MatchHistoryDays = matchHistoryDays,
                UpcomingDays = upcomingDays,
                ExcludeExistingSelections = excludeExistingSelections
            },
            cancellationToken);

        return await ReadResponseAsync<RobotPanelRunResultViewModel>(response, cancellationToken);
    }

    private async Task<RobotPanelStepResultViewModel> PostStepAsync(
        string url,
        object? body,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        if (body is null)
        {
            response = await _httpClient.PostAsync(url, content: null, cancellationToken);
        }
        else
        {
            response = await _httpClient.PostAsJsonAsync(url, body, cancellationToken);
        }

        return await ReadResponseAsync<RobotPanelStepResultViewModel>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Backend API failed with {(int)response.StatusCode}."
                    : errorBody);
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return payload ?? throw new InvalidOperationException("Backend API returned an empty pipeline response.");
    }
}
