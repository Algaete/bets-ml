using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CornersPrediction.Domain.FootballIntelligence;

namespace CornersPrediction.Application.FootballIntelligence;

public sealed class FootballNewsQueryBuilder : INewsQueryBuilder
{
    private static readonly IReadOnlyDictionary<string, string[]> Terms =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = ["injury update", "team news", "press conference", "predicted lineup", "suspended players", "late fitness test", "training", "rotation"],
            ["es"] = ["lesionados", "bajas", "suspendidos", "convocatoria", "rueda de prensa", "alineación probable", "entrenamiento", "rotación"],
            ["pt"] = ["desfalques", "suspensos", "provável escalação", "relacionados", "coletiva", "treino", "retorno", "rodízio"]
        };

    public IReadOnlyCollection<string> Build(
        string teamName,
        string opponentName,
        IReadOnlyCollection<string>? aliases = null,
        IReadOnlyCollection<string>? languages = null)
    {
        var names = new[] { teamName }
            .Concat(aliases ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        var selectedLanguages = (languages is null || languages.Count == 0 ? Terms.Keys : languages)
            .Where(Terms.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0 || selectedLanguages.Length == 0)
            return [];
        return names
            .SelectMany(name => Enumerable.Range(0, selectedLanguages.Max(language => Terms[language].Length))
                .SelectMany(index => selectedLanguages
                    .Where(language => index < Terms[language].Length)
                    .Select(language => $"\"{name}\" {Terms[language][index]} \"{opponentName}\"")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class ArticleDeduplicator : IArticleDeduplicator
{
    public IReadOnlyCollection<ExtractedArticle> Deduplicate(IReadOnlyCollection<ExtractedArticle> articles)
    {
        var exact = articles
            .OrderByDescending(value => value.UpdatedAtUtc ?? value.PublishedAtUtc ?? DateTime.MinValue)
            .GroupBy(value => value.CanonicalUrl?.AbsoluteUri ?? value.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .GroupBy(value => value.ContentHash, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var result = new List<ExtractedArticle>();
        foreach (var article in exact)
        {
            var normalizedTitle = Normalize(article.Title);
            if (result.Any(existing => Similarity(normalizedTitle, Normalize(existing.Title)) >= 0.92d))
                continue;
            result.Add(article);
        }
        return result;
    }

    private static string Normalize(string value) => Regex.Replace(
        RemoveDiacritics(value).ToLowerInvariant(),
        @"[^a-z0-9]+",
        " ").Trim();

    private static double Similarity(string left, string right)
    {
        if (left == right)
            return 1d;
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0d;
        var intersection = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();
        return union == 0 ? 0d : intersection / (double)union;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return string.Concat(normalized.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
    }
}

public sealed class RelevantTextSelector : IRelevantTextSelector
{
    private static readonly string[] Keywords =
    [
        "injury", "injured", "doubt", "suspension", " ruled out", "training", "return", "available", "rest", "rotation", "lineup", "squad",
        "lesión", "lesionado", "duda", "baja", "suspendido", "entrenamiento", "regreso", "convocatoria", "rotación", "alineación",
        "lesão", "desfalque", "suspenso", "treino", "retorno", "escalação", "rodízio"
    ];

    public string Select(
        string normalizedText,
        string teamName,
        string opponentName,
        IReadOnlyCollection<string> playerNames,
        int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || maximumCharacters <= 0)
            return string.Empty;
        var terms = Keywords
            .Concat([teamName, opponentName])
            .Concat(playerNames)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var paragraphs = Regex.Split(normalizedText, @"(?:\r?\n){2,}")
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Where(value => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selected = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        if (selected.Length == 0)
            return string.Empty;
        return selected.Length <= maximumCharacters ? selected : selected[..maximumCharacters];
    }
}

/// <summary>
/// Conservative no-key extractor for explicit statements only. It extracts facts;
/// it never infers a betting consequence. An LLM provider can replace this service.
/// </summary>
public sealed partial class RuleBasedNewsFactExtractor : INewsFactExtractor
{
    public Task<NewsExtractionResult> ExtractAsync(
        NewsExtractionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var facts = new List<ExtractedNewsFact>();
        var sentences = SentenceSplit().Split(request.ArticleText)
            .Select(value => Regex.Replace(value, @"\s+", " ").Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        foreach (var sentence in sentences)
        {
            if (ContainsAny(sentence, "not injured", "isn't injured", "no está lesionado", "no esta lesionado", "não está lesionado"))
                continue;
            var player = request.KnownPlayerNames?
                .Where(name => !string.IsNullOrWhiteSpace(name) && sentence.Contains(name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(player))
                continue;

            var classification = Classify(sentence);
            if (classification is null)
                continue;
            facts.Add(new ExtractedNewsFact(
                request.TeamName,
                player,
                classification.Value.EventType,
                classification.Value.Status,
                classification.Value.Certainty,
                classification.Value.ProbabilityAvailable,
                classification.Value.Reason,
                null,
                null,
                sentence.Length <= 500 ? sentence : sentence[..500],
                classification.Value.Confidence));
        }

        var rotation = sentences.Any(value => ContainsAny(value, "rotation", "rotación", "rotacao", "rotação", "rodízio")) ? 0.65m : 0m;
        return Task.FromResult(new NewsExtractionResult(
            facts.Count > 0 ? 0.80m : 0m,
            facts,
            new TeamNewsSignals(rotation, 0m, 0m, false, false, false),
            "DeterministicLexicalExtractor",
            "football-news-rules-v1"));
    }

    private static Classification? Classify(string sentence)
    {
        if (ContainsAny(sentence, "served his suspension", "served the suspension", "cumplió su suspensión", "cumplio su suspension", "cumpriu suspensão"))
            return new(FootballNewsEventType.Return, AvailabilityStatus.ExpectedAvailable, FactCertainty.Reported, 0.80m, "Suspension served", 0.78m);
        if (ContainsAny(sentence, "ruled out", "has been ruled out", "fue descartado", "está descartado", "esta descartado", "fora do jogo"))
            return new(FootballNewsEventType.Injury, AvailabilityStatus.ConfirmedOut, FactCertainty.Confirmed, 0m, "Ruled out", 0.90m);
        if (ContainsAny(sentence, "suspended", "suspendido", "suspenso"))
            return new(FootballNewsEventType.Suspension, AvailabilityStatus.Suspended, FactCertainty.Reported, 0m, "Suspension", 0.85m);
        if (ContainsAny(sentence, "returned to training", "returned to full training", "volvió a entrenar", "volvio a entrenar", "retornou aos treinos"))
            return new(FootballNewsEventType.TrainingReturn, AvailabilityStatus.Unknown, FactCertainty.Reported, null, "Training return does not confirm availability", 0.80m);
        if (ContainsAny(sentence, "will be assessed", "late fitness test", "será evaluado", "sera evaluado", "será reavaliado", "em dúvida"))
            return new(FootballNewsEventType.Doubt, AvailabilityStatus.Doubtful, FactCertainty.Reported, 0.50m, "Pending assessment", 0.78m);
        if (ContainsAny(sentence, "did not travel", "didn't travel", "no viajó", "no viajo", "não viajou"))
            return new(FootballNewsEventType.NotCalled, AvailabilityStatus.ExpectedOut, FactCertainty.Reported, 0.10m, "Did not travel", 0.82m);
        if (ContainsAny(sentence, "will be rested", "will rest", "será reservado", "sera reservado", "será poupado", "sera poupado"))
            return new(FootballNewsEventType.Rest, AvailabilityStatus.Rested, FactCertainty.Expected, 0.10m, "Expected rest", 0.78m);
        if (ContainsAny(sentence, "not called up", "not in the squad", "no fue convocado", "no convocado", "não relacionado"))
            return new(FootballNewsEventType.NotCalled, AvailabilityStatus.NotCalled, FactCertainty.Reported, 0m, "Not called", 0.85m);
        return null;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private readonly record struct Classification(
        FootballNewsEventType EventType,
        AvailabilityStatus Status,
        FactCertainty Certainty,
        decimal? ProbabilityAvailable,
        string Reason,
        decimal Confidence);

    [GeneratedRegex(@"(?<=[.!?])\s+|(?:\r?\n)+", RegexOptions.Compiled)]
    private static partial Regex SentenceSplit();
}

public sealed class NewsFactConsolidator : INewsFactConsolidator
{
    public IReadOnlyCollection<FootballNewsFact> Consolidate(
        IReadOnlyCollection<FootballNewsFact> facts,
        DateTime cutoffAtUtc)
    {
        var cutoff = EnsureUtc(cutoffAtUtc);
        var accepted = facts
            .Where(fact => EnsureUtc(fact.FirstSeenAtUtc) <= cutoff)
            .ToArray();
        var result = new List<FootballNewsFact>(accepted.Length);
        foreach (var group in accepted.GroupBy(FactIdentity, StringComparer.OrdinalIgnoreCase))
        {
            var winner = group
                .OrderByDescending(fact => IsOfficial(fact.EventType))
                .ThenByDescending(fact => fact.EventEffectiveAtUtc ?? fact.FirstSeenAtUtc)
                .ThenByDescending(fact => CertaintyPrecedence(fact.Certainty))
                .ThenByDescending(fact => fact.EffectiveConfidence)
                .ThenByDescending(fact => Precedence(fact.EventType))
                .ThenByDescending(fact => fact.Id)
                .First();
            result.AddRange(group.Select(fact => fact with { IsCurrent = ReferenceEquals(fact, winner) }));
        }
        return result.OrderBy(fact => fact.CreatedAtUtc).ThenBy(fact => fact.Id).ToArray();
    }

    private static string FactIdentity(FootballNewsFact fact)
    {
        var subject = fact.PlayerId.HasValue
            ? $"ID:{fact.PlayerId.Value}"
            : $"NAME:{Normalize(fact.PlayerNameExtracted)}";
        return $"{fact.FixtureId}|{fact.TeamId}|{subject}";
    }

    private static bool IsOfficial(FootballNewsEventType value) =>
        value is FootballNewsEventType.OfficialStarter or FootballNewsEventType.OfficialBench;

    private static int Precedence(FootballNewsEventType value) => value switch
    {
        FootballNewsEventType.OfficialStarter or FootballNewsEventType.OfficialBench => 100,
        FootballNewsEventType.NotCalled => 90,
        FootballNewsEventType.Suspension => 85,
        FootballNewsEventType.Injury => 80,
        FootballNewsEventType.Return => 75,
        FootballNewsEventType.TrainingReturn => 50,
        _ => 40
    };

    private static int CertaintyPrecedence(FactCertainty value) => value switch
    {
        FactCertainty.Confirmed => 5,
        FactCertainty.Reported => 4,
        FactCertainty.Expected => 3,
        FactCertainty.Speculation => 2,
        _ => 1
    };

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public sealed class PlayerImpactCalculator : IPlayerImpactCalculator
{
    public PlayerAvailabilityImpact Calculate(
        PlayerMarketImportance importance,
        decimal probabilityAvailable,
        decimal effectiveConfidence,
        decimal replacementGap)
    {
        var available = Math.Clamp(probabilityAvailable, 0m, 1m);
        var confidence = Math.Clamp(effectiveConfidence, 0m, 1m);
        var gap = Math.Clamp(replacementGap, 0m, 1m);
        return new PlayerAvailabilityImpact(
            importance.PlayerId,
            importance.MarketType,
            importance.Importance,
            available,
            confidence,
            gap,
            Math.Clamp(importance.Importance * (1m - available) * confidence * gap, 0m, 1m));
    }
}

public static class StructuredFootballEvidenceClassifier
{
    private static readonly string[] SuspensionTerms =
    [
        "susp", "yellow card", "red card", "card accumulation", "disciplinary",
        "tarjeta amarilla", "tarjeta roja", "acumulación de tarjetas", "acumulacion de tarjetas",
        "cartão amarelo", "cartao amarelo", "cartão vermelho", "cartao vermelho"
    ];

    public static bool IsSuspension(string? type, string? reason)
    {
        var evidence = $"{type} {reason}";
        return SuspensionTerms.Any(term =>
            evidence.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public static class FootballIntelligenceHash
{
    public static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
