using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPredictionApi.Requests;

/// <summary>
/// HTTP request DTO for the shots-on-goal model feature payload.
/// </summary>
public sealed class ShotsOnGoalPredictionRequest
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Features { get; init; }

    public JsonElement ToJsonElement()
    {
        return JsonSerializer.SerializeToElement(Features ?? new Dictionary<string, JsonElement>());
    }
}
