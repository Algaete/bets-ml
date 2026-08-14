using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.CompetitionFiltering;

public sealed class CompetitionFilterOptions
{
    public const string SectionName = "CompetitionFilter";

    public bool Enabled { get; set; } = true;

    public List<string> ExcludedPatterns { get; set; } = new();

    public List<string> AllowedFirstDivisionPatterns { get; set; } = new();

    public List<string> AllowedSecondDivisionPatterns { get; set; } = new();

    public List<string> AllowedInternationalPatterns { get; set; } = new();
}

public sealed record CompetitionEligibilityDecision(
    bool IsEligible,
    string Category,
    string Reason);

public sealed class CompetitionEligibilityPolicy
{
    private readonly CompetitionFilterOptions _options;

    public CompetitionEligibilityPolicy(IOptions<CompetitionFilterOptions> options)
    {
        _options = options.Value;

        if (_options.Enabled
            && _options.AllowedFirstDivisionPatterns.Count == 0
            && _options.AllowedSecondDivisionPatterns.Count == 0
            && _options.AllowedInternationalPatterns.Count == 0)
        {
            throw new InvalidOperationException(
                "CompetitionFilter is enabled but no allowed competition patterns are configured.");
        }
    }

    public CompetitionEligibilityDecision Evaluate(
        string? competition,
        string? context = null,
        params string?[] genders)
    {
        if (!_options.Enabled)
            return new CompetitionEligibilityDecision(true, "Disabled", "Competition filtering is disabled.");

        if (genders.Any(IsFemaleGender))
            return new CompetitionEligibilityDecision(false, "Excluded", "Female competition or team gender.");

        var normalizedCompetition = Normalize(competition);
        var normalizedContext = Normalize(context);
        var searchable = string.Join(' ', new[] { normalizedCompetition, normalizedContext }
            .Where(value => value.Length > 0));

        if (searchable.Length == 0)
            return new CompetitionEligibilityDecision(false, "Unknown", "Competition name was empty.");

        var excludedPattern = FindMatchingPattern(
            _options.ExcludedPatterns,
            normalizedCompetition,
            normalizedContext,
            searchable);
        if (excludedPattern is not null)
        {
            return new CompetitionEligibilityDecision(
                false,
                "Excluded",
                $"Matched excluded pattern '{excludedPattern}'.");
        }

        var internationalPattern = FindMatchingPattern(
            _options.AllowedInternationalPatterns,
            normalizedCompetition,
            normalizedContext,
            searchable);
        if (internationalPattern is not null)
        {
            return new CompetitionEligibilityDecision(
                true,
                "International",
                $"Matched international competition pattern '{internationalPattern}'.");
        }

        var firstDivisionPattern = FindMatchingPattern(
            _options.AllowedFirstDivisionPatterns,
            normalizedCompetition,
            normalizedContext,
            searchable);
        if (firstDivisionPattern is not null)
        {
            return new CompetitionEligibilityDecision(
                true,
                "FirstDivision",
                $"Matched first division pattern '{firstDivisionPattern}'.");
        }

        var secondDivisionPattern = FindMatchingPattern(
            _options.AllowedSecondDivisionPatterns,
            normalizedCompetition,
            normalizedContext,
            searchable);
        if (secondDivisionPattern is not null)
        {
            return new CompetitionEligibilityDecision(
                true,
                "SecondDivision",
                $"Matched second division pattern '{secondDivisionPattern}'.");
        }

        return new CompetitionEligibilityDecision(
            false,
            "Unknown",
            "Competition is not in the configured allowlist.");
    }

    public bool IsEligible(string? competition, string? context = null, params string?[] genders) =>
        Evaluate(competition, context, genders).IsEligible;

    private static string? FindMatchingPattern(
        IEnumerable<string> patterns,
        string normalizedCompetition,
        string normalizedContext,
        string searchable)
    {
        foreach (var configuredPattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(configuredPattern))
                continue;

            var exact = configuredPattern.StartsWith('=');
            var pattern = Normalize(exact ? configuredPattern[1..] : configuredPattern);
            if (pattern.Length == 0)
                continue;

            var matches = exact
                ? normalizedCompetition.Equals(pattern, StringComparison.Ordinal)
                  || normalizedContext.Equals(pattern, StringComparison.Ordinal)
                : searchable.Contains(pattern, StringComparison.Ordinal);

            if (matches)
                return configuredPattern;
        }

        return null;
    }

    private static bool IsFemaleGender(string? gender)
    {
        var normalized = Normalize(gender);
        return normalized is "f" or "female" or "femenino" or "femenina";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && result.Length > 0)
                    result.Append(' ');

                result.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return result.ToString().Trim();
    }
}
