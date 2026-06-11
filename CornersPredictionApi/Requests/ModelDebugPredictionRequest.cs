using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPredictionApi.Requests;

/// <summary>
/// Generic feature payload used to execute one raw model artifact from Swagger.
/// </summary>
public sealed class ModelDebugPredictionRequest
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Features { get; init; }

    public JsonElement ToJsonElement()
    {
        return JsonSerializer.SerializeToElement(Features ?? new Dictionary<string, JsonElement>());
    }
}
