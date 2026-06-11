using System.Net.Http.Json;
using System.Text.Json;
using CornersPrediction.Web.Models.MatchHistory;
using CornersPrediction.Web.Models.Predictions;
using CornersPrediction.Web.Models.Teams;

namespace CornersPrediction.Web.Clients;

public sealed class MatchHistoryApiClient
{
    private readonly HttpClient _httpClient;

    public MatchHistoryApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MatchHistoryItemViewModel>> GetRecentAsync(
        string homeTeam,
        string awayTeam,
        string? league,
        string? teamGender,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return Array.Empty<MatchHistoryItemViewModel>();
        }

        var matches = await _httpClient.GetFromJsonAsync<IReadOnlyList<MatchHistoryItemViewModel>>(
            $"/api/matches?homeTeam={Uri.EscapeDataString(homeTeam.Trim())}&awayTeam={Uri.EscapeDataString(awayTeam.Trim())}&league={Uri.EscapeDataString(league?.Trim() ?? string.Empty)}&teamGender={Uri.EscapeDataString(NormalizeTeamGender(teamGender))}",
            cancellationToken);

        return matches ?? Array.Empty<MatchHistoryItemViewModel>();
    }

    public async Task<IReadOnlyList<MatchHistoryItemViewModel>> GetManualEntriesAsync(
        MatchHistoryFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        var take = filters.Take <= 0 ? 20 : Math.Min(filters.Take, 100);
        var query = $"/api/matches/records?take={take}";

        if (!string.IsNullOrWhiteSpace(filters.League))
        {
            query += $"&league={Uri.EscapeDataString(filters.League.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(filters.Team))
        {
            query += $"&team={Uri.EscapeDataString(filters.Team.Trim())}";
        }

        var matches = await _httpClient.GetFromJsonAsync<IReadOnlyList<MatchHistoryItemViewModel>>(
            query,
            cancellationToken);

        return matches ?? Array.Empty<MatchHistoryItemViewModel>();
    }

    public async Task<PredictionContextViewModel?> GetPredictionContextAsync(
        string league,
        string homeTeam,
        string awayTeam,
        string? teamGender,
        double? baseLocalAwayPrediction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return null;
        }

        var query = $"/api/matches/prediction-context?homeTeam={Uri.EscapeDataString(homeTeam.Trim())}&awayTeam={Uri.EscapeDataString(awayTeam.Trim())}&teamGender={Uri.EscapeDataString(NormalizeTeamGender(teamGender))}";
        if (!string.IsNullOrWhiteSpace(league))
        {
            query += $"&league={Uri.EscapeDataString(league.Trim())}";
        }

        if (baseLocalAwayPrediction is not null)
        {
            query += $"&baseLocalAwayPrediction={Uri.EscapeDataString(baseLocalAwayPrediction.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
        }

        return await _httpClient.GetFromJsonAsync<PredictionContextViewModel>(
            query,
            cancellationToken);
    }

    public async Task<IReadOnlyList<TeamBi3InfoViewModel>> GetTeamOptionsAsync(
        string league,
        string? teamGender,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return Array.Empty<TeamBi3InfoViewModel>();
        }

        var teams = await _httpClient.GetFromJsonAsync<IReadOnlyList<TeamBi3InfoViewModel>>(
            $"/api/teams/big3-info?league={Uri.EscapeDataString(league.Trim())}&teamGender={Uri.EscapeDataString(NormalizeTeamGender(teamGender))}",
            cancellationToken);

        return teams ?? Array.Empty<TeamBi3InfoViewModel>();
    }

    public async Task<IReadOnlyList<string>> GetLeagueOptionsAsync(string? teamGender, CancellationToken cancellationToken)
    {
        var leagues = await _httpClient.GetFromJsonAsync<IReadOnlyList<string>>(
            $"/api/teams/big3-leagues?teamGender={Uri.EscapeDataString(NormalizeTeamGender(teamGender))}",
            cancellationToken);

        return leagues ?? Array.Empty<string>();
    }

    public async Task<IReadOnlyList<string>> GetFormationOptionsAsync(CancellationToken cancellationToken)
    {
        var formations = await _httpClient.GetFromJsonAsync<IReadOnlyList<string>>(
            "/api/teams/formations",
            cancellationToken);

        return formations ?? Array.Empty<string>();
    }

    public async Task<MatchHistoryItemViewModel> CreateAsync(
        CreateMatchHistoryViewModel form,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            form.League,
            form.Season,
            MatchDate = DateOnly.FromDateTime(form.MatchDate),
            form.IsKnockout,
            form.HomeTeam,
            form.AwayTeam,
            form.HomeFormation,
            form.AwayFormation,
            form.HomeCorners,
            form.AwayCorners,
            form.HomeGoals,
            form.AwayGoals,
            form.HomeShots,
            form.AwayShots,
            form.HomeShotsOnGoal,
            form.AwayShotsOnGoal,
            form.HomePossession,
            form.AwayPossession
        };

        var response = await _httpClient.PostAsJsonAsync("/api/matches", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Backend API rejected the match: {error}");
        }

        var created = await response.Content.ReadFromJsonAsync<MatchHistoryItemViewModel>(
            cancellationToken: cancellationToken);

        return created ?? throw new InvalidOperationException("Backend API returned an empty response.");
    }

    public async Task UpdateAsync(
        UpdateMatchHistoryViewModel form,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            form.League,
            form.Season,
            MatchDate = DateOnly.FromDateTime(form.MatchDate),
            form.IsKnockout,
            form.HomeTeam,
            form.AwayTeam,
            form.HomeFormation,
            form.AwayFormation,
            form.HomeCorners,
            form.AwayCorners,
            form.HomeGoals,
            form.AwayGoals,
            form.HomeShots,
            form.AwayShots,
            form.HomeShotsOnGoal,
            form.AwayShotsOnGoal,
            form.HomePossession,
            form.AwayPossession
        };

        var response = await _httpClient.PutAsJsonAsync($"/api/matches/{form.Id}", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Backend API rejected the match update: {error}");
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"/api/matches/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Backend API rejected the match delete: {error}");
        }
    }

    public async Task<PredictionResultViewModel> PredictAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/predict", features, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Backend API rejected the prediction request: {error}");
        }

        var prediction = await response.Content.ReadFromJsonAsync<PredictionResultViewModel>(
            cancellationToken: cancellationToken);

        return prediction ?? throw new InvalidOperationException("Backend API returned an empty prediction response.");
    }

    public async Task<OverUnderPredictionResultViewModel> PredictOverUnderAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/predict/over-under", features, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Backend API rejected the Over/Under prediction request: {error}");
        }

        var prediction = await response.Content.ReadFromJsonAsync<OverUnderPredictionResultViewModel>(
            cancellationToken: cancellationToken);

        return prediction ?? throw new InvalidOperationException("Backend API returned an empty Over/Under prediction response.");
    }

    public async Task<ShotsOnGoalPredictionResultViewModel> PredictShotsOnGoalAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/predict/shots-on-goal", features, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Backend API rejected the shots-on-goal prediction request: {error}");
        }

        var prediction = await response.Content.ReadFromJsonAsync<ShotsOnGoalPredictionResultViewModel>(
            cancellationToken: cancellationToken);

        return prediction ?? throw new InvalidOperationException("Backend API returned an empty shots-on-goal prediction response.");
    }

    private static string NormalizeTeamGender(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "M" : value.Trim().ToUpperInvariant();
        return normalized is "M" or "F" or "U" ? normalized : "M";
    }
}
