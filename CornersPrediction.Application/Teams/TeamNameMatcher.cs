using System.Globalization;
using System.Text;

namespace CornersPrediction.Application.Teams;

public enum TeamNameMatchKind
{
    Normalized,
    ClubTokens,
    OptionalSuffix,
    RegionalSuffix,
    TokenOrder,
    Acronym,
    Fuzzy
}

public sealed record TeamNameMatch(
    string Name,
    TeamNameMatchKind Kind,
    double Confidence,
    bool CanPersistAlias = false);

public static class TeamNameMatcher
{
    private const double MinimumFuzzyScore = 0.93;
    private const double MinimumFuzzyGap = 0.055;

    private static readonly HashSet<string> ClubTokens = new(StringComparer.Ordinal)
    {
        "ac", "afc", "bk", "ca", "cd", "cf", "club", "clube", "deportivo", "ec", "fc", "fk", "sc", "sk", "sv"
    };

    private static readonly HashSet<string> RegionalSuffixTokens = new(StringComparer.Ordinal)
    {
        "ba", "go", "mg", "pe", "pr", "rj", "rn", "rs", "sp"
    };

    // Some providers omit a legal part of the club name (for example API-Football
    // exposes "Coventry" while the bookmaker uses "Coventry City"). This is kept
    // separate from ClubTokens: it requires an otherwise exact token sequence and
    // is never persisted as a global alias automatically.
    private static readonly HashSet<string> OptionalSuffixTokens = new(StringComparer.Ordinal)
    {
        "city"
    };

    private static readonly IReadOnlyDictionary<string, string> TokenAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["espana"] = "spain",
            ["st"] = "saint",
            ["utd"] = "united"
        };

    // Exact provider variants observed in official fixture feeds. Keeping these
    // as full-name aliases is intentionally stricter than dropping geographic
    // tokens such as "Boyaca" or "de Cordoba" for every club in the world.
    private static readonly IReadOnlyDictionary<string, string> KnownTeamAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["boyaca chico"] = "chico",
            ["jaguares de cordoba"] = "jaguares",
            ["vicenza virtus"] = "vicenza"
        };

    public static bool AreEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftIdentity = CreateIdentity(left);
        var rightIdentity = CreateIdentity(right);

        return leftIdentity.FullKey == rightIdentity.FullKey ||
            (leftIdentity.TokenCount >= 2 && leftIdentity.FullSortedKey == rightIdentity.FullSortedKey);
    }

    public static TeamNameMatch? FindBestMatch(string input, IEnumerable<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var inputIdentity = CreateIdentity(input);
        var candidateIdentities = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(CreateIdentity)
            .DistinctBy(candidate => candidate.FullKey)
            .ToArray();

        var normalizedMatch = candidateIdentities
            .FirstOrDefault(candidate => candidate.FullKey == inputIdentity.FullKey);
        if (normalizedMatch is not null)
        {
            return new TeamNameMatch(normalizedMatch.Original, TeamNameMatchKind.Normalized, 1, true);
        }

        var clubTokenMatches = candidateIdentities
            .Where(candidate => !string.IsNullOrEmpty(inputIdentity.LooseKey))
            .Where(candidate => candidate.LooseKey == inputIdentity.LooseKey)
            .ToArray();
        if (clubTokenMatches.Length == 1)
        {
            var canPersistAlias = inputIdentity.LooseTokens.Length >= 2 &&
                clubTokenMatches[0].LooseTokens.Length >= 2;
            return new TeamNameMatch(
                clubTokenMatches[0].Original,
                TeamNameMatchKind.ClubTokens,
                0.99,
                canPersistAlias);
        }

        var optionalSuffixMatches = candidateIdentities
            .Where(candidate => IsOptionalSuffixVariant(inputIdentity.LooseTokens, candidate.LooseTokens))
            .ToArray();
        if (optionalSuffixMatches.Length == 1)
        {
            return new TeamNameMatch(
                optionalSuffixMatches[0].Original,
                TeamNameMatchKind.OptionalSuffix,
                0.98,
                CanPersistAlias: false);
        }

        var regionalMatches = candidateIdentities
            .Where(candidate => IsRegionalVariant(inputIdentity.LooseTokens, candidate.LooseTokens))
            .ToArray();
        if (regionalMatches.Length == 1)
        {
            return new TeamNameMatch(
                regionalMatches[0].Original,
                TeamNameMatchKind.RegionalSuffix,
                0.985,
                true);
        }

        if (inputIdentity.LooseTokens.Length >= 2)
        {
            var tokenOrderMatches = candidateIdentities
                .Where(candidate => candidate.LooseTokens.Length >= 2)
                .Where(candidate => candidate.SortedKey == inputIdentity.SortedKey)
                .ToArray();
            if (tokenOrderMatches.Length == 1)
            {
                return new TeamNameMatch(tokenOrderMatches[0].Original, TeamNameMatchKind.TokenOrder, 0.97);
            }

            var abbreviationMatches = candidateIdentities
                .Where(candidate => IsAbbreviationVariant(inputIdentity.LooseTokens, candidate.LooseTokens))
                .ToArray();
            if (abbreviationMatches.Length == 1)
            {
                return new TeamNameMatch(
                    abbreviationMatches[0].Original,
                    TeamNameMatchKind.TokenOrder,
                    0.965);
            }
        }

        if (IsAcronym(inputIdentity.FullKey))
        {
            var acronymMatches = candidateIdentities
                .Where(candidate => candidate.Acronym == inputIdentity.FullKey)
                .ToArray();
            if (acronymMatches.Length == 1)
            {
                return new TeamNameMatch(acronymMatches[0].Original, TeamNameMatchKind.Acronym, 0.96);
            }
        }

        var fuzzyMatches = candidateIdentities
            .Where(candidate => IsPlausibleFuzzyPair(inputIdentity, candidate))
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = JaroWinkler(inputIdentity.LooseKey, candidate.LooseKey)
            })
            .OrderByDescending(match => match.Score)
            .ToArray();

        if (fuzzyMatches.Length == 0 || fuzzyMatches[0].Score < MinimumFuzzyScore)
        {
            return null;
        }

        if (fuzzyMatches.Length > 1 &&
            fuzzyMatches[0].Score - fuzzyMatches[1].Score < MinimumFuzzyGap)
        {
            return null;
        }

        return new TeamNameMatch(
            fuzzyMatches[0].Candidate.Original,
            TeamNameMatchKind.Fuzzy,
            Math.Round(fuzzyMatches[0].Score, 3));
    }

    private static TeamNameIdentity CreateIdentity(string value)
    {
        var tokens = Tokenize(value);
        var tokenKey = string.Join(' ', tokens);
        if (KnownTeamAliases.TryGetValue(tokenKey, out var canonicalName))
        {
            tokens = canonicalName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        var looseTokens = tokens.Where(token => !ClubTokens.Contains(token)).ToArray();
        if (looseTokens.Length == 0)
        {
            looseTokens = tokens;
        }

        return new TeamNameIdentity(
            value.Trim(),
            string.Join(' ', tokens),
            string.Join(' ', tokens.OrderBy(token => token, StringComparer.Ordinal)),
            string.Join(' ', looseTokens),
            string.Join(' ', looseTokens.OrderBy(token => token, StringComparer.Ordinal)),
            BuildAcronym(tokens),
            tokens.Length,
            looseTokens);
    }

    private static string[] Tokenize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(FoldLetter(character));
            }
            else if (normalized.Length > 0 && normalized[^1] != ' ')
            {
                normalized.Append(' ');
            }
        }

        return normalized
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => TokenAliases.TryGetValue(token, out var alias) ? alias : token)
            .ToArray();
    }

    private static char FoldLetter(char value) => char.ToLowerInvariant(value) switch
    {
        'ğ' => 'g',
        'ı' => 'i',
        'ł' => 'l',
        'ø' => 'o',
        _ => char.ToLowerInvariant(value)
    };

    private static string BuildAcronym(IReadOnlyList<string> tokens)
    {
        var acronym = new StringBuilder(tokens.Count);
        foreach (var token in tokens)
        {
            if (ClubTokens.Contains(token) && token.Length <= 3)
            {
                acronym.Append(token);
            }
            else if (token.Length > 0)
            {
                acronym.Append(token[0]);
            }
        }

        return acronym.ToString();
    }

    private static bool IsAcronym(string value) =>
        value.Length is >= 3 and <= 6 && value.All(char.IsLetterOrDigit);

    private static bool IsPlausibleFuzzyPair(TeamNameIdentity input, TeamNameIdentity candidate)
    {
        if (HasConflictingRegionalSuffixes(input.LooseTokens, candidate.LooseTokens))
        {
            return false;
        }

        if (input.LooseKey.Length < 5 || candidate.LooseKey.Length < 5)
        {
            return false;
        }

        var lengthRatio = (double)Math.Min(input.LooseKey.Length, candidate.LooseKey.Length) /
            Math.Max(input.LooseKey.Length, candidate.LooseKey.Length);
        if (lengthRatio < 0.72)
        {
            return false;
        }

        if (input.LooseTokens.Length == 1 && candidate.LooseTokens.Length == 1)
        {
            return true;
        }

        if (input.LooseTokens.Intersect(candidate.LooseTokens, StringComparer.Ordinal).Any())
        {
            return true;
        }

        return JaroWinkler(input.LooseTokens[0], candidate.LooseTokens[0]) >= 0.92 ||
            JaroWinkler(input.LooseTokens[^1], candidate.LooseTokens[^1]) >= 0.92;
    }

    private static bool IsRegionalVariant(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var leftSuffix = GetRegionalSuffix(left);
        var rightSuffix = GetRegionalSuffix(right);
        if (leftSuffix is null && rightSuffix is null)
        {
            return false;
        }

        if (leftSuffix is not null && rightSuffix is not null && leftSuffix != rightSuffix)
        {
            return false;
        }

        var leftCoreCount = leftSuffix is null ? left.Count : left.Count - 1;
        var rightCoreCount = rightSuffix is null ? right.Count : right.Count - 1;
        if (leftCoreCount == 0 || leftCoreCount != rightCoreCount)
        {
            return false;
        }

        for (var index = 0; index < leftCoreCount; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOptionalSuffixVariant(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var longer = left.Count > right.Count ? left : right;
        var shorter = left.Count > right.Count ? right : left;
        if (shorter.Count == 0 || longer.Count != shorter.Count + 1 ||
            !OptionalSuffixTokens.Contains(longer[^1]))
        {
            return false;
        }

        for (var index = 0; index < shorter.Count; index++)
        {
            if (longer[index] != shorter[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAbbreviationVariant(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (left.Count < 2 || left.Count != right.Count)
        {
            return false;
        }

        var leftRemaining = left.ToList();
        foreach (var rightToken in right)
        {
            var exactIndex = leftRemaining.FindIndex(token => token == rightToken);
            if (exactIndex >= 0)
            {
                leftRemaining.RemoveAt(exactIndex);
                continue;
            }

            var abbreviationIndex = leftRemaining.FindIndex(token =>
                (token.Length == 1 || rightToken.Length == 1) && token[0] == rightToken[0]);
            if (abbreviationIndex < 0)
            {
                return false;
            }

            leftRemaining.RemoveAt(abbreviationIndex);
        }

        return leftRemaining.Count == 0;
    }

    private static bool HasConflictingRegionalSuffixes(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var leftSuffix = GetRegionalSuffix(left);
        var rightSuffix = GetRegionalSuffix(right);
        return leftSuffix is not null && rightSuffix is not null && leftSuffix != rightSuffix;
    }

    private static string? GetRegionalSuffix(IReadOnlyList<string> tokens) =>
        tokens.Count > 1 && RegionalSuffixTokens.Contains(tokens[^1])
            ? tokens[^1]
            : null;

    private static double JaroWinkler(string left, string right)
    {
        if (left == right)
        {
            return 1;
        }

        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var matchDistance = Math.Max(left.Length, right.Length) / 2 - 1;
        var leftMatches = new bool[left.Length];
        var rightMatches = new bool[right.Length];
        var matches = 0;

        for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            var start = Math.Max(0, leftIndex - matchDistance);
            var end = Math.Min(leftIndex + matchDistance + 1, right.Length);

            for (var rightIndex = start; rightIndex < end; rightIndex++)
            {
                if (rightMatches[rightIndex] || left[leftIndex] != right[rightIndex])
                {
                    continue;
                }

                leftMatches[leftIndex] = true;
                rightMatches[rightIndex] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
        {
            return 0;
        }

        var transpositions = 0;
        var matchedRightIndex = 0;
        for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            if (!leftMatches[leftIndex])
            {
                continue;
            }

            while (!rightMatches[matchedRightIndex])
            {
                matchedRightIndex++;
            }

            if (left[leftIndex] != right[matchedRightIndex])
            {
                transpositions++;
            }

            matchedRightIndex++;
        }

        var jaro = (
            (double)matches / left.Length +
            (double)matches / right.Length +
            (matches - transpositions / 2d) / matches) / 3d;

        var prefixLength = 0;
        var maximumPrefixLength = Math.Min(4, Math.Min(left.Length, right.Length));
        while (prefixLength < maximumPrefixLength && left[prefixLength] == right[prefixLength])
        {
            prefixLength++;
        }

        return jaro + prefixLength * 0.1 * (1 - jaro);
    }

    private sealed record TeamNameIdentity(
        string Original,
        string FullKey,
        string FullSortedKey,
        string LooseKey,
        string SortedKey,
        string Acronym,
        int TokenCount,
        string[] LooseTokens);
}
