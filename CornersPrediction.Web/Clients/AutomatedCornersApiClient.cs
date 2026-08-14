using System.Net.Http.Json;
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
