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
        CancellationToken cancellationToken,
        string? marketFamily = null)
    {
        var selections = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotPickSelectionViewModel>>(
            $"/api/automated-corners/selections{BuildQuery(filters, marketFamily)}",
            cancellationToken);

        return selections ?? Array.Empty<BotPickSelectionViewModel>();
    }

    public async Task<IReadOnlyList<BotPickMonthlySummaryViewModel>> GetMonthlyHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        string marketFamily,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery(new BotPickFiltersViewModel { DateFrom = dateFrom, DateTo = dateTo }, marketFamily);
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<BotPickMonthlySummaryViewModel>>(
            $"/api/automated-corners/monthly-history{query}", cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<BotPerformanceScorecardViewModel>> GetPerformanceScorecardsAsync(
        CancellationToken cancellationToken)
    {
        var scorecards = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotPerformanceScorecardViewModel>>(
            "/api/automated-corners/performance/scorecards",
            cancellationToken);
        return scorecards ?? [];
    }

    public async Task<IReadOnlyList<BotMonitoringSummaryViewModel>> GetMonitoringSummaryAsync(
        BotPickFiltersViewModel filters,
        string marketFamily,
        CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (filters.DateFrom.HasValue)
            query.Add($"dateFrom={Uri.EscapeDataString(filters.DateFrom.Value.ToString("yyyy-MM-dd"))}");
        if (filters.DateTo.HasValue)
            query.Add($"dateTo={Uri.EscapeDataString(filters.DateTo.Value.ToString("yyyy-MM-dd"))}");
        query.Add($"marketFamily={Uri.EscapeDataString(marketFamily.Trim().ToUpperInvariant())}");
        if (!string.IsNullOrWhiteSpace(filters.MarketType))
            query.Add($"marketType={Uri.EscapeDataString(filters.MarketType.Trim())}");

        var rows = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotMonitoringSummaryViewModel>>(
            $"/api/automated-corners/monitoring-summary?{string.Join('&', query)}",
            cancellationToken);
        return rows ?? [];
    }

    public Task<BotResearchEvaluationPageViewModel> GetResearchEvaluationsAsync(
        BotResearchEvaluationFiltersViewModel filters,
        CancellationToken cancellationToken) =>
        GetEvaluationPageAsync("research-evaluations", filters, cancellationToken);

    public Task<BotResearchEvaluationPageViewModel> GetGeneralPicksAsync(
        BotResearchEvaluationFiltersViewModel filters,
        CancellationToken cancellationToken) =>
        GetEvaluationPageAsync("general-picks", filters, cancellationToken);

    public async Task<GeneralPickLabViewModel> GetGeneralPickLabAsync(
        BotResearchEvaluationFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var lab = await _httpClient.GetFromJsonAsync<GeneralPickLabViewModel>(
            $"/api/automated-corners/general-picks/lab{BuildResearchQuery(filters, includePaging: false, includeDecision: false)}",
            cancellationToken);
        return lab ?? new GeneralPickLabViewModel();
    }

    public async Task SettleGeneralPickAsync(long recordId, GeneralPickManualSettlementViewModel request,
        string actor, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put,
            $"/api/automated-corners/general-picks/{recordId}/settlement")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Acting-User", actor);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var text = "No se pudo guardar la liquidación manual.";
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
            {
                using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (body.RootElement.TryGetProperty("error", out var error)) text = error.GetString() ?? text;
            }
            throw new HttpRequestException(text, null, response.StatusCode);
        }
    }

    public async Task<System.Text.Json.JsonElement?> GetGeneralPickEvidenceAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/api/automated-corners/general-picks/{id}/evidence", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cancellationToken);
    }

    private async Task<BotResearchEvaluationPageViewModel> GetEvaluationPageAsync(
        string endpoint,
        BotResearchEvaluationFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var page = await _httpClient.GetFromJsonAsync<BotResearchEvaluationPageViewModel>(
            $"/api/automated-corners/{endpoint}{BuildResearchQuery(filters)}",
            cancellationToken);

        return page ?? new BotResearchEvaluationPageViewModel
        {
            Page = filters.Page,
            PageSize = filters.PageSize
        };
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

    private static string BuildQuery(BotPickFiltersViewModel filters, string? marketFamily = null)
    {
        var query = new List<string>();
        Add(query, "dateFrom", filters.DateFrom?.ToString("yyyy-MM-dd"));
        Add(query, "dateTo", filters.DateTo?.ToString("yyyy-MM-dd"));
        Add(query, "status", filters.Status);
        Add(query, "league", filters.League);
        Add(query, "source", filters.Bookmaker);
        Add(query, "marketType", filters.MarketType);
        Add(query, "marketFamily", marketFamily?.Trim().ToUpperInvariant());

        if (filters.OnlyPending)
        {
            query.Add("onlyPending=true");
        }

        return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private static string BuildResearchQuery(
        BotResearchEvaluationFiltersViewModel filters,
        bool includePaging = true,
        bool includeDecision = true)
    {
        var query = new List<string>();
        if (includePaging)
        {
            Add(query, "sortBy", filters.SortBy);
            Add(query, "sortDirection", filters.SortDirection);
        }
        Add(query, "dateFrom", filters.DateFrom?.ToString("yyyy-MM-dd"));
        Add(query, "dateTo", filters.DateTo?.ToString("yyyy-MM-dd"));
        Add(query, "marketFamily", filters.MarketFamily);
        Add(query, "marketType", filters.MarketType);
        Add(query, "botKey", filters.BotKey);
        if (includeDecision)
            Add(query, "modelDecision", filters.ModelDecision);
        Add(query, "publicationStatus", filters.PublicationStatus);
        if (includePaging)
        {
            query.Add($"page={Math.Max(1, filters.Page)}");
            query.Add($"pageSize={Math.Clamp(filters.PageSize, 1, 200)}");
        }
        return "?" + string.Join("&", query);
    }

    private static void Add(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
