using System.Net.Http.Json;
using CornersPrediction.Web.Models.UpcomingMatches;

namespace CornersPrediction.Web.Clients;

public sealed class UpcomingMatchesApiClient
{
    private readonly HttpClient _httpClient;

    public UpcomingMatchesApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<UpcomingMatchDto>> GetNextWeekAsync(
        string? genero,
        string? liga,
        CancellationToken cancellationToken)
    {
        var query = "/api/upcoming-matches";
        var queryParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(genero))
        {
            queryParts.Add($"genero={Uri.EscapeDataString(genero.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(liga))
        {
            queryParts.Add($"liga={Uri.EscapeDataString(liga.Trim())}");
        }

        if (queryParts.Count > 0)
        {
            query += "?" + string.Join("&", queryParts);
        }

        var matches = await _httpClient.GetFromJsonAsync<IReadOnlyList<UpcomingMatchDto>>(
            query,
            cancellationToken);

        return matches ?? Array.Empty<UpcomingMatchDto>();
    }
}
