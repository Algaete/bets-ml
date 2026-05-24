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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return Array.Empty<MatchHistoryItemViewModel>();
        }

        var matches = await _httpClient.GetFromJsonAsync<IReadOnlyList<MatchHistoryItemViewModel>>(
            $"/api/matches?homeTeam={Uri.EscapeDataString(homeTeam.Trim())}&awayTeam={Uri.EscapeDataString(awayTeam.Trim())}",
            cancellationToken);

        return matches ?? Array.Empty<MatchHistoryItemViewModel>();
    }

    public async Task<IReadOnlyList<TeamBi3InfoViewModel>> GetTeamOptionsAsync(
        string league,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return Array.Empty<TeamBi3InfoViewModel>();
        }

        var teams = await _httpClient.GetFromJsonAsync<IReadOnlyList<TeamBi3InfoViewModel>>(
            $"/api/teams/big3-info?league={Uri.EscapeDataString(league.Trim())}",
            cancellationToken);

        return teams ?? Array.Empty<TeamBi3InfoViewModel>();
    }

    public async Task<IReadOnlyList<string>> GetLeagueOptionsAsync(CancellationToken cancellationToken)
    {
        var leagues = await _httpClient.GetFromJsonAsync<IReadOnlyList<string>>(
            "/api/teams/big3-leagues",
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
}
