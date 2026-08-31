using System.Globalization;
using System.Net.Http.Json;
using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Clients;

/// <summary>
/// Read-only client for the H2026 shadow laboratory. Intentionally exposes no
/// command, publication, promotion or settlement method.
/// </summary>
public sealed class BotH2026ApiClient
{
    private readonly HttpClient _httpClient;

    public BotH2026ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BotH2026StatusViewModel> GetStatusAsync(CancellationToken cancellationToken)
    {
        var result = await _httpClient.GetFromJsonAsync<BotH2026StatusViewModel>(
            "/api/bot-h2026/status",
            cancellationToken);
        return result ?? new BotH2026StatusViewModel();
    }

    public async Task<BotH2026EvaluationPageViewModel> GetEvaluationsAsync(
        BotH2026FiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var query = BuildCommonQuery(filters, includeEvaluationFilters: true);
        query.Add($"page={Math.Max(1, filters.Page).ToString(CultureInfo.InvariantCulture)}");
        query.Add($"pageSize={Math.Clamp(filters.PageSize, 1, 1000).ToString(CultureInfo.InvariantCulture)}");
        var result = await _httpClient.GetFromJsonAsync<BotH2026EvaluationPageViewModel>(
            $"/api/bot-h2026/evaluations{ToQueryString(query)}",
            cancellationToken);
        return result ?? new BotH2026EvaluationPageViewModel
        {
            Page = filters.Page,
            PageSize = filters.PageSize
        };
    }

    public async Task<IReadOnlyList<BotH2026ScorecardViewModel>> GetScorecardsAsync(
        BotH2026FiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var query = new List<string>();
        AddDate(query, "asOfUtc", filters.AsOfUtc);
        Add(query, "configurationVersion", filters.ConfigurationVersion);
        var result = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotH2026ScorecardViewModel>>(
            $"/api/bot-h2026/scorecards{ToQueryString(query)}",
            cancellationToken);
        return result ?? [];
    }

    private static List<string> BuildCommonQuery(
        BotH2026FiltersViewModel filters,
        bool includeEvaluationFilters)
    {
        var query = new List<string>();
        AddDate(query, "predictionFromUtc", filters.PredictionFromUtc);
        AddDate(query, "predictionToUtc", filters.PredictionToUtc);
        AddDate(query, "asOfUtc", filters.AsOfUtc);
        Add(query, "configurationVersion", filters.ConfigurationVersion);

        if (includeEvaluationFilters)
        {
            Add(query, "decision", filters.Decision);
            Add(query, "marketType", filters.MarketType);
            Add(query, "selection", filters.Selection);
            Add(query, "settlementState", filters.SettlementState);
        }

        return query;
    }

    private static void Add(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
    }

    private static void AddDate(List<string> query, string name, DateTime? value)
    {
        if (!value.HasValue)
            return;

        var utc = value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        query.Add($"{name}={Uri.EscapeDataString(utc.ToString("O", CultureInfo.InvariantCulture))}");
    }

    private static string ToQueryString(IReadOnlyCollection<string> query) =>
        query.Count == 0 ? string.Empty : $"?{string.Join('&', query)}";
}
