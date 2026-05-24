using System.Net.Http.Json;
using CornersPrediction.Web.Models.Betting;

namespace CornersPrediction.Web.Clients;

public sealed class BettingApiClient
{
    private readonly HttpClient _httpClient;

    public BettingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<BettingRecordViewModel>> GetAsync(
        BettingFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var records = await _httpClient.GetFromJsonAsync<IReadOnlyList<BettingRecordViewModel>>(
            $"/api/betting{BuildQuery(filters)}",
            cancellationToken);

        return records ?? Array.Empty<BettingRecordViewModel>();
    }

    public async Task<BettingSummaryViewModel> GetSummaryAsync(
        BettingFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var summary = await _httpClient.GetFromJsonAsync<BettingSummaryViewModel>(
            $"/api/betting/summary{BuildQuery(filters)}",
            cancellationToken);

        return summary ?? new BettingSummaryViewModel();
    }

    public async Task<IReadOnlyList<BankrollTransactionViewModel>> GetBankrollTransactionsAsync(
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var transactions = await _httpClient.GetFromJsonAsync<IReadOnlyList<BankrollTransactionViewModel>>(
            $"/api/betting/bankroll?currencyCode={Uri.EscapeDataString(currencyCode)}",
            cancellationToken);

        return transactions ?? Array.Empty<BankrollTransactionViewModel>();
    }

    public async Task<decimal> GetCurrentBankrollAsync(string currencyCode, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<CurrentBankrollResponse>(
            $"/api/betting/bankroll/current?currencyCode={Uri.EscapeDataString(currencyCode)}",
            cancellationToken);

        return response?.CurrentBankroll ?? 0;
    }

    public async Task CreateBankrollTransactionAsync(
        BankrollTransactionFormViewModel form,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            form.CurrencyCode,
            TransactionDate = form.TransactionDate.Date,
            form.Type,
            form.Amount,
            form.BettingRecordId,
            form.Notes
        };

        var response = await _httpClient.PostAsJsonAsync("/api/betting/bankroll", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<BettingRecordViewModel?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<BettingRecordViewModel>(
            $"/api/betting/{id}",
            cancellationToken);
    }

    public async Task CreateAsync(BettingRecordFormViewModel form, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/betting", ToCreatePayload(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateAsync(BettingRecordFormViewModel form, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/betting/{form.Id}", ToUpdatePayload(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"/api/betting/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static object ToCreatePayload(BettingRecordFormViewModel form)
    {
        return new
        {
            form.League,
            form.CurrencyCode,
            form.Season,
            MatchDate = form.MatchDate.Date,
            form.HomeTeam,
            form.AwayTeam,
            form.Bookmaker,
            form.MarketType,
            form.BetSelection,
            form.Line,
            form.Odds,
            form.Stake,
            form.Status,
            form.BankrollBefore,
            form.ClosingOdds,
            form.ConfidenceLevel,
            form.Notes
        };
    }

    private static object ToUpdatePayload(BettingRecordFormViewModel form)
    {
        return new
        {
            form.League,
            form.CurrencyCode,
            form.Season,
            MatchDate = form.MatchDate.Date,
            form.HomeTeam,
            form.AwayTeam,
            form.Bookmaker,
            form.MarketType,
            form.BetSelection,
            form.Line,
            form.Odds,
            form.Stake,
            form.Status,
            form.ActualHomeCorners,
            form.ActualAwayCorners,
            form.ActualTotalCorners,
            form.CashoutAmount,
            form.BankrollBefore,
            form.ClosingOdds,
            form.ConfidenceLevel,
            form.Notes,
            form.AutoResolveStatus
        };
    }

    private static string BuildQuery(BettingFiltersViewModel filters)
    {
        var query = new List<string>();
        Add(query, "currencyCode", filters.CurrencyCode);
        Add(query, "league", filters.League);
        Add(query, "season", filters.Season);
        Add(query, "homeTeam", filters.HomeTeam);
        Add(query, "awayTeam", filters.AwayTeam);
        Add(query, "status", filters.Status);
        Add(query, "marketType", filters.MarketType);
        Add(query, "bookmaker", filters.Bookmaker);
        Add(query, "dateFrom", filters.DateFrom?.ToString("yyyy-MM-dd"));
        Add(query, "dateTo", filters.DateTo?.ToString("yyyy-MM-dd"));
        return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private static void Add(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Backend API rejected the betting request: {error}");
    }

    private sealed class CurrentBankrollResponse
    {
        public decimal CurrentBankroll { get; init; }
    }
}
