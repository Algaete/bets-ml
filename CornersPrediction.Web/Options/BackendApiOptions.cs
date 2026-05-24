namespace CornersPrediction.Web.Options;

public sealed class BackendApiOptions
{
    public const string SectionName = "BackendApi";

    public string BaseUrl { get; init; } = "http://localhost:5070";
}
