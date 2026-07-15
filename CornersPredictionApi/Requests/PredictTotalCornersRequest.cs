using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPredictionApi.Requests;

/// <summary>
/// HTTP request DTO for the model features expected by the corners prediction model.
/// </summary>
public sealed class PredictTotalCornersRequest
{
    public string? League { get; init; }
    public string? Season { get; init; }
    public DateTimeOffset? MatchDate { get; init; }

    [JsonPropertyName("big3home")]
    public int? Big3Home { get; init; }

    [JsonPropertyName("big3away")]
    public int? Big3Away { get; init; }

    public double? Home_AvgCornersForLast3 { get; init; }
    public double? Home_AvgCornersAgainstLast3 { get; init; }
    public double? Home_AvgCornersForLast3_HomeOnly { get; init; }
    public double? Home_AvgCornersAgainstLast3_HomeOnly { get; init; }
    public double? Home_AvgGoalsForLast3 { get; init; }
    public double? Home_AvgGoalsAgainstLast3 { get; init; }
    public double? Home_AvgGoalsForLast3_HomeOnly { get; init; }
    public double? Home_AvgGoalsAgainstLast3_HomeOnly { get; init; }
    public double? Home_AvgShotsLast3 { get; init; }
    public double? Home_AvgShotsOnGoalLast3 { get; init; }
    public double? Home_AvgPossessionLast3 { get; init; }

    public double? Home_AvgCornersForLast5 { get; init; }
    public double? Home_AvgCornersAgainstLast5 { get; init; }
    public double? Home_AvgCornersForLast5_HomeOnly { get; init; }
    public double? Home_AvgCornersAgainstLast5_HomeOnly { get; init; }
    public double? Home_AvgGoalsForLast5 { get; init; }
    public double? Home_AvgGoalsAgainstLast5 { get; init; }
    public double? Home_AvgGoalsForLast5_HomeOnly { get; init; }
    public double? Home_AvgGoalsAgainstLast5_HomeOnly { get; init; }
    public double? Home_AvgShotsLast5 { get; init; }
    public double? Home_AvgShotsOnGoalLast5 { get; init; }
    public double? Home_AvgPossessionLast5 { get; init; }

    public double? Home_AvgCornersForLast10 { get; init; }
    public double? Home_AvgCornersAgainstLast10 { get; init; }
    public double? Home_AvgCornersForLast10_HomeOnly { get; init; }
    public double? Home_AvgCornersAgainstLast10_HomeOnly { get; init; }
    public double? Home_AvgGoalsForLast10 { get; init; }
    public double? Home_AvgGoalsAgainstLast10 { get; init; }
    public double? Home_AvgGoalsForLast10_HomeOnly { get; init; }
    public double? Home_AvgGoalsAgainstLast10_HomeOnly { get; init; }
    public double? Home_AvgShotsLast10 { get; init; }
    public double? Home_AvgShotsOnGoalLast10 { get; init; }
    public double? Home_AvgPossessionLast10 { get; init; }

    public double? Away_AvgCornersForLast3 { get; init; }
    public double? Away_AvgCornersAgainstLast3 { get; init; }
    public double? Away_AvgCornersForLast3_AwayOnly { get; init; }
    public double? Away_AvgCornersAgainstLast3_AwayOnly { get; init; }
    public double? Away_AvgGoalsForLast3 { get; init; }
    public double? Away_AvgGoalsAgainstLast3 { get; init; }
    public double? Away_AvgGoalsForLast3_AwayOnly { get; init; }
    public double? Away_AvgGoalsAgainstLast3_AwayOnly { get; init; }
    public double? Away_AvgShotsLast3 { get; init; }
    public double? Away_AvgShotsOnGoalLast3 { get; init; }
    public double? Away_AvgPossessionLast3 { get; init; }

    public double? Away_AvgCornersForLast5 { get; init; }
    public double? Away_AvgCornersAgainstLast5 { get; init; }
    public double? Away_AvgCornersForLast5_AwayOnly { get; init; }
    public double? Away_AvgCornersAgainstLast5_AwayOnly { get; init; }
    public double? Away_AvgGoalsForLast5 { get; init; }
    public double? Away_AvgGoalsAgainstLast5 { get; init; }
    public double? Away_AvgGoalsForLast5_AwayOnly { get; init; }
    public double? Away_AvgGoalsAgainstLast5_AwayOnly { get; init; }
    public double? Away_AvgShotsLast5 { get; init; }
    public double? Away_AvgShotsOnGoalLast5 { get; init; }
    public double? Away_AvgPossessionLast5 { get; init; }

    public double? Away_AvgCornersForLast10 { get; init; }
    public double? Away_AvgCornersAgainstLast10 { get; init; }
    public double? Away_AvgCornersForLast10_AwayOnly { get; init; }
    public double? Away_AvgCornersAgainstLast10_AwayOnly { get; init; }
    public double? Away_AvgGoalsForLast10 { get; init; }
    public double? Away_AvgGoalsAgainstLast10 { get; init; }
    public double? Away_AvgGoalsForLast10_AwayOnly { get; init; }
    public double? Away_AvgGoalsAgainstLast10_AwayOnly { get; init; }
    public double? Away_AvgShotsLast10 { get; init; }
    public double? Away_AvgShotsOnGoalLast10 { get; init; }
    public double? Away_AvgPossessionLast10 { get; init; }

    public string? HomeFormation { get; init; }
    public string? AwayFormation { get; init; }
    public int? HomeHasFormation { get; init; }
    public int? AwayHasFormation { get; init; }

    public int? HomeRankingPosition { get; init; }

    public int? AwayRankingPosition { get; init; }

    public int? RankingTotalTeams { get; init; }

    public string? RankingSource { get; init; }

    public string? RankingSeason { get; init; }

    /// <summary>
    /// Keeps unknown future features instead of dropping them during JSON binding.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFeatures { get; init; }

    /// <summary>
    /// Converts the typed DTO into a JSON object consumed by the Application layer and Python runner.
    /// </summary>
    public JsonElement ToJsonElement()
    {
        var serialized = JsonSerializer.SerializeToElement(this, JsonOptions);
        using var document = JsonDocument.Parse(serialized.GetRawText());
        var filtered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (RankingFeatureNames.Contains(property.Name))
            {
                continue;
            }

            filtered[property.Name] = property.Value.Clone();
        }

        return JsonSerializer.SerializeToElement(filtered, JsonOptions);
    }

    private static readonly HashSet<string> RankingFeatureNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(HomeRankingPosition),
        nameof(AwayRankingPosition),
        nameof(RankingTotalTeams),
        nameof(RankingSource),
        nameof(RankingSeason),
        "PosicionRankingLocal",
        "PosicionRankingVisita",
        "TotalEquiposRanking",
        "FuenteRanking",
        "TemporadaRanking"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
