using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Clients;

public sealed class AutomatedCornersApiClient
{
    private readonly HttpClient _httpClient;

    public AutomatedCornersApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<BotPickSelectionViewModel>> GetSelectionsAsync(
        BotPickFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var selections = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotPickSelectionViewModel>>(
            $"/api/automated-corners/selections{BuildQuery(filters)}",
            cancellationToken);

        return selections ?? Array.Empty<BotPickSelectionViewModel>();
    }

    public async Task<IReadOnlyList<BotPerformanceScorecardViewModel>> GetPerformanceScorecardsAsync(
        CancellationToken cancellationToken)
    {
        var scorecards = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotPerformanceScorecardViewModel>>(
            "/api/automated-corners/performance/scorecards",
            cancellationToken);
        return scorecards ?? [];
    }

    public async Task<BotPickRobustEvaluationDetailViewModel?> GetRobustEvaluationAsync(
        long selectionId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/api/robust-pick-evaluations/{selectionId}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Robust pick evaluation lookup failed with {(int)response.StatusCode}."
                    : errorBody);
        }

        return await response.Content.ReadFromJsonAsync<BotPickRobustEvaluationDetailViewModel>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Robust pick evaluation returned an empty response.");
    }

    public async Task<BotPickIntelligenceDetailViewModel> GetFootballIntelligenceAsync(
        long fixtureId,
        DateTime? cutoffUtc,
        CancellationToken cancellationToken)
    {
        var cutoffQuery = cutoffUtc.HasValue
            ? $"?cutoffUtc={Uri.EscapeDataString(cutoffUtc.Value.ToUniversalTime().ToString("O"))}"
            : string.Empty;
        var latestTask = _httpClient.GetAsync(
            $"/api/intelligence/fixtures/{fixtureId}/latest{cutoffQuery}",
            cancellationToken);
        var factsTask = _httpClient.GetFromJsonAsync<IReadOnlyList<BotPickIntelligenceFactViewModel>>(
            $"/api/intelligence/fixtures/{fixtureId}/facts{cutoffQuery}",
            cancellationToken);
        var documentsTask = _httpClient.GetFromJsonAsync<IReadOnlyList<BotPickIntelligenceDocumentViewModel>>(
            $"/api/intelligence/fixtures/{fixtureId}/documents{cutoffQuery}",
            cancellationToken);
        var snapshotsTask = _httpClient.GetFromJsonAsync<IReadOnlyList<BotPickIntelligenceSnapshotViewModel>>(
            $"/api/intelligence/fixtures/{fixtureId}/snapshots",
            cancellationToken);

        await Task.WhenAll(latestTask, factsTask, documentsTask, snapshotsTask);
        using var latestResponse = await latestTask;
        JsonElement? latest = null;
        if (latestResponse.IsSuccessStatusCode)
        {
            latest = await latestResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        else if (latestResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var errorBody = await latestResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Football intelligence lookup failed with {(int)latestResponse.StatusCode}."
                    : errorBody);
        }

        return new BotPickIntelligenceDetailViewModel
        {
            Latest = latest,
            Facts = await factsTask ?? [],
            Documents = await documentsTask ?? [],
            Snapshots = await snapshotsTask ?? []
        };
    }

    public async Task<BotPickSelectionViewModel> UpdateSelectionStatusAsync(
        long id,
        UpdateBotPickStatusViewModel request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/automated-corners/selections/{id}/status",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Status update failed with {(int)response.StatusCode}."
                    : errorBody);
        }

        var updatedSelection = await response.Content.ReadFromJsonAsync<BotPickSelectionViewModel>(cancellationToken);
        return updatedSelection ?? throw new InvalidOperationException("Status update returned an empty response.");
    }

    public async Task<BotPickSelectionViewModel> ResolveSelectionAsync(
        long id,
        ResolveBotPickViewModel request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/automated-corners/selections/{id}/resolve",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Settlement failed with {(int)response.StatusCode}."
                    : errorBody);
        }

        var updatedSelection = await response.Content.ReadFromJsonAsync<BotPickSelectionViewModel>(cancellationToken);
        return updatedSelection ?? throw new InvalidOperationException("Settlement returned an empty response.");
    }

    public async Task<BotPickSettlementResponseViewModel> SettlePendingAsync(
        SettlePendingBotPicksViewModel request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/automated-corners/settle",
            new
            {
                MatchDateTo = request.MatchDateTo?.ToString("yyyy-MM-dd"),
                DryRun = false,
                request.MaxRows,
                request.BotKey,
                request.MarketFamily
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Pending settlement failed with {(int)response.StatusCode}."
                    : errorBody);
        }

        var settlement = await response.Content.ReadFromJsonAsync<BotPickSettlementResponseViewModel>(cancellationToken);
        return settlement ?? throw new InvalidOperationException("Pending settlement returned an empty response.");
    }

    public async Task<ReconcileAvailableBotPicksResponseViewModel> ReconcileAvailableAsync(
        ReconcileAvailableBotPicksViewModel request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/api-football/reconcile-bot-picks",
            new
            {
                DateFrom = request.DateFrom?.ToString("yyyy-MM-dd"),
                DateTo = request.DateTo?.ToString("yyyy-MM-dd"),
                request.MaxSelections,
                request.DryRun
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Bot Pick reconciliation failed with {(int)response.StatusCode}."
                    : errorBody);
        }

        var result = await response.Content.ReadFromJsonAsync<ReconcileAvailableBotPicksResponseViewModel>(
            cancellationToken);
        return result ?? throw new InvalidOperationException("Bot Pick reconciliation returned an empty response.");
    }

    public async Task DeleteSelectionAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync(
            $"/api/automated-corners/selections/{id}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"Delete failed with {(int)response.StatusCode}."
                    : errorBody);
        }
    }

    private static string BuildQuery(BotPickFiltersViewModel filters)
    {
        var query = new List<string>();
        Add(query, "dateFrom", filters.DateFrom?.ToString("yyyy-MM-dd"));
        Add(query, "dateTo", filters.DateTo?.ToString("yyyy-MM-dd"));
        Add(query, "status", filters.Status);
        Add(query, "league", filters.League);
        Add(query, "source", filters.Bookmaker);
        Add(query, "marketType", filters.MarketType);

        if (filters.OnlyPending)
        {
            query.Add("onlyPending=true");
        }

        return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private static void Add(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
