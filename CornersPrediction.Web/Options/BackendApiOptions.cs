namespace CornersPrediction.Web.Options;

public sealed class BackendApiOptions
{
    public const string SectionName = "BackendApi";

    public string BaseUrl { get; init; } = "http://localhost:5070";

    public string? InternalApiKey { get; init; }
}
