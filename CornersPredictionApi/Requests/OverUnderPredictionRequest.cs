using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPredictionApi.Requests;

/// <summary>
/// HTTP request DTO for the Over/Under model feature payload.
/// </summary>
public sealed class OverUnderPredictionRequest
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Features { get; init; }

    public JsonElement ToJsonElement()
    {
        return JsonSerializer.SerializeToElement(Features ?? new Dictionary<string, JsonElement>());
    }
}
