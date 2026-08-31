using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Clients;

public sealed class BotG2026ApiClient
{
    private readonly HttpClient _httpClient;

    public BotG2026ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BotG2026CandidatePageViewModel> GetCandidatesAsync(
        BotG2026FiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var result = await _httpClient.GetFromJsonAsync<BotG2026CandidatePageViewModel>(
            $"/api/bot-g2026/candidates{BuildCandidateQuery(filters)}",
            cancellationToken);
        return result ?? new BotG2026CandidatePageViewModel
        {
            Page = filters.Page,
            PageSize = filters.PageSize
        };
    }

    public async Task<BotG2026CandidateViewModel?> GetCandidateAsync(
        long candidateId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/api/bot-g2026/candidates/{candidateId}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, "Bot G candidate lookup", cancellationToken);
        return await response.Content.ReadFromJsonAsync<BotG2026CandidateViewModel>(cancellationToken);
    }

    public async Task<IReadOnlyList<BotG2026ScorecardViewModel>> GetScorecardAsync(
        BotG2026FiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var query = new List<string>();
        AddDate(query, "dateFromUtc", filters.DateFromUtc);
        AddDate(query, "dateToUtc", filters.DateToUtc);
        Add(query, "configurationVersion", filters.ConfigurationVersion);
        var rows = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotG2026ScorecardViewModel>>(
            $"/api/bot-g2026/scorecard{ToQueryString(query)}",
            cancellationToken);
        return rows ?? [];
    }

    public async Task<BotG2026RuntimeStatusViewModel> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var status = await _httpClient.GetFromJsonAsync<BotG2026RuntimeStatusViewModel>(
            "/api/bot-g2026/status",
            cancellationToken);
        return status ?? new BotG2026RuntimeStatusViewModel();
    }

    private static string BuildCandidateQuery(BotG2026FiltersViewModel filters)
    {
        var query = new List<string>();
        AddDate(query, "dateFromUtc", filters.DateFromUtc);
        AddDate(query, "dateToUtc", filters.DateToUtc);
        Add(query, "decision", filters.Decision);
        Add(query, "publicationStatus", filters.PublicationStatus);
        Add(query, "marketType", filters.MarketType);
        Add(query, "selection", filters.Selection);
        Add(query, "bookmaker", filters.Bookmaker);
        Add(query, "configurationVersion", filters.ConfigurationVersion);
        Add(query, "result", filters.Result);
        query.Add($"page={Math.Max(1, filters.Page).ToString(CultureInfo.InvariantCulture)}");
        query.Add($"pageSize={Math.Clamp(filters.PageSize, 1, 1000).ToString(CultureInfo.InvariantCulture)}");
        return ToQueryString(query);
    }

    private static void Add(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
    }

    private static void AddDate(List<string> query, string name, DateTime? value)
    {
        if (value.HasValue)
            query.Add($"{name}={Uri.EscapeDataString(value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
    }

    private static string ToQueryString(IReadOnlyList<string> query) =>
        query.Count == 0 ? string.Empty : $"?{string.Join('&', query)}";

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(body)
                ? $"{operation} failed with HTTP {(int)response.StatusCode}."
                : body);
    }
}
