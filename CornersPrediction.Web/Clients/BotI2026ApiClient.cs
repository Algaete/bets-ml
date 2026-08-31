using System.Globalization;
using System.Net.Http.Json;
using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Clients;

/// <summary>
/// Client for the isolated I2026 market-movement lab. Collection only appends
/// shadow evidence; this client intentionally exposes no publication endpoint.
/// </summary>
public sealed class BotI2026ApiClient
{
    private readonly HttpClient _httpClient;

    public BotI2026ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BotI2026StatusViewModel> GetStatusAsync(CancellationToken cancellationToken)
    {
        var result = await _httpClient.GetFromJsonAsync<BotI2026StatusViewModel>(
            "/api/bot-i2026/status",
            cancellationToken);
        return result ?? new BotI2026StatusViewModel();
    }

    public async Task<BotI2026EvaluationPageViewModel> GetEvaluationsAsync(
        BotI2026FiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var query = BuildCommonQuery(filters);
        Add(query, "decision", filters.Decision);
        Add(query, "marketType", filters.MarketType);
        Add(query, "selection", filters.Selection);
        Add(query, "source", filters.Source);
        query.Add($"page={Math.Max(1, filters.Page).ToString(CultureInfo.InvariantCulture)}");
        query.Add($"pageSize={Math.Clamp(filters.PageSize, 1, 1000).ToString(CultureInfo.InvariantCulture)}");

        var result = await _httpClient.GetFromJsonAsync<BotI2026EvaluationPageViewModel>(
            $"/api/bot-i2026/evaluations{ToQueryString(query)}",
            cancellationToken);
        return result ?? new BotI2026EvaluationPageViewModel
        {
            Page = filters.Page,
            PageSize = filters.PageSize
        };
    }

    public async Task<IReadOnlyList<BotI2026ScorecardViewModel>> GetScorecardsAsync(
        BotI2026FiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var query = new List<string>();
        AddDate(query, "asOfUtc", filters.AsOfUtc);
        Add(query, "configurationVersion", filters.ConfigurationVersion);
        var result = await _httpClient.GetFromJsonAsync<IReadOnlyList<BotI2026ScorecardViewModel>>(
            $"/api/bot-i2026/scorecards{ToQueryString(query)}",
            cancellationToken);
        return result ?? [];
    }

    public async Task<BotI2026CollectResultViewModel> CollectAsync(
        BotI2026CollectViewModel command,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/bot-i2026/collect",
            new
            {
                command.DateFrom,
                command.DateTo,
                command.AsOfUtc,
                command.MaximumFixtures
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BotI2026CollectResultViewModel>(
                   cancellationToken: cancellationToken)
               ?? new BotI2026CollectResultViewModel();
    }

    private static List<string> BuildCommonQuery(BotI2026FiltersViewModel filters)
    {
        var query = new List<string>();
        AddDate(query, "predictionFromUtc", filters.PredictionFromUtc);
        AddDate(query, "predictionToUtc", filters.PredictionToUtc);
        AddDate(query, "asOfUtc", filters.AsOfUtc);
        Add(query, "configurationVersion", filters.ConfigurationVersion);
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
