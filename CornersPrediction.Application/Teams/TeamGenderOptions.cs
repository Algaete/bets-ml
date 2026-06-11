namespace CornersPrediction.Application.Teams;

public static class TeamGenderOptions
{
    public const string Male = "M";
    public const string Female = "F";
    public const string Unknown = "U";

    public static readonly string[] All = [Male, Female, Unknown];

    public static string Normalize(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? Male
            : value.Trim().ToUpperInvariant();

        return All.Contains(normalized)
            ? normalized
            : throw new ArgumentException("TeamGender must be M, F or U.");
    }
}
