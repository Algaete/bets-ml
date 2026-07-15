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

    private static string BuildQuery(BotPickFiltersViewModel filters)
    {
        var query = new List<string>();
        Add(query, "dateFrom", filters.DateFrom?.ToString("yyyy-MM-dd"));
        Add(query, "dateTo", filters.DateTo?.ToString("yyyy-MM-dd"));
        Add(query, "status", filters.Status);
        Add(query, "league", filters.League);
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
