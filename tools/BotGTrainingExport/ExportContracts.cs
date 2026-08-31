using System.Globalization;
using System.Text.Json;

namespace BotGTrainingExport;

internal static class ExportContract
{
    public const string ConfigurationVersion = "bot-g-goals-market-intelligence-1.1.0";
    public const string FeatureSchemaVersion = "bot-g-goals-features-1.0.0";
    public const string TrainingContractVersion = "bot-g-training-export-1.1.0";
    public const string FootballIntelligenceVersion = "football-intelligence-adjustment-1.0.0";

    private static readonly string[] RequiredDatabaseColumns =
    [
        "CandidateId", "QuoteId", "FixtureId", "FixtureDateUtc", "PredictionTimestampUtc",
        "FeatureAsOfUtc", "OddsTimestampUtc", "OutcomeAvailableUtc", "League", "HomeTeam",
        "AwayTeam", "Bookmaker", "MarketType", "Selection", "Line", "OverOdds", "UnderOdds",
        "SelectedOdds", "LegacyPrediction", "LegacyModelVersion", "LegacyModelTrainedThroughUtc",
        "Prediction2026", "Model2026Version", "Model2026TrainedThroughUtc", "ContextPrediction",
        "HistoricalMean", "HistoricalStd", "HistoryCount", "DataQualityScore", "ActualValue",
        "ConfigurationVersion", "FeatureSchemaVersion", "FeatureSnapshotJson", "IsSynthetic"
    ];

    public static IReadOnlyDictionary<string, object?> Normalize(
        IReadOnlyDictionary<string, object?> source)
    {
        foreach (var column in RequiredDatabaseColumns)
        {
            if (!source.ContainsKey(column) || source[column] is null or DBNull)
                throw new InvalidDataException($"Training export row is missing required value '{column}'.");
        }

        var configurationVersion = Text(source, "ConfigurationVersion");
        if (!string.Equals(configurationVersion, ConfigurationVersion, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Snapshot configuration '{configurationVersion}' is not live v1.1; relabeling is forbidden.");
        var featureSchemaVersion = Text(source, "FeatureSchemaVersion");
        if (!string.Equals(featureSchemaVersion, FeatureSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected feature schema '{featureSchemaVersion}'.");

        using var snapshot = JsonDocument.Parse(Text(source, "FeatureSnapshotJson"));
        var root = snapshot.RootElement;
        var lineage = RequiredObject(root, "lineage");
        var intelligence = RequiredObject(root, "footballIntelligence");
        var intelligenceResult = RequiredObject(intelligence, "result");
        var trainingContract = RequiredText(lineage, "trainingContractVersion");
        if (!string.Equals(trainingContract, TrainingContractVersion, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Snapshot training contract '{trainingContract}' is not v1.1; relabeling is forbidden and no value was inferred.");
        var intelligenceEnabled = RequiredBoolean(intelligence, "enabled");
        if (!intelligenceEnabled)
            throw new InvalidDataException("Bot G v1.1 snapshot has Football Intelligence disabled.");
        var intelligenceVersion = RequiredText(intelligence, "version");
        if (!string.Equals(intelligenceVersion, FootballIntelligenceVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected Football Intelligence version '{intelligenceVersion}'.");

        var marketType = Text(source, "MarketType");
        var lineageMarket = RequiredText(lineage, "marketType");
        if (!string.Equals(marketType, lineageMarket, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Snapshot lineage market '{lineageMarket}' does not match row market '{marketType}'.");
        var legacyVersion = Text(source, "LegacyModelVersion");
        var model2026Version = Text(source, "Model2026Version");
        if (!string.Equals(legacyVersion, RequiredText(lineage, "legacyModelVersion"), StringComparison.Ordinal)
            || !string.Equals(model2026Version, RequiredText(lineage, "model2026Version"), StringComparison.Ordinal))
            throw new InvalidDataException("Stored base-model lineage disagrees with the immutable snapshot.");

        var homeStatus = RequiredText(intelligenceResult, "homeEvidenceStatus");
        var awayStatus = RequiredText(intelligenceResult, "awayEvidenceStatus");
        var adjustment = RequiredDouble(intelligenceResult, "probabilityAdjustment");
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CandidateId"] = Text(source, "CandidateId"),
            ["QuoteId"] = Text(source, "QuoteId"),
            ["FixtureId"] = Text(source, "FixtureId"),
            ["FixtureDateUtc"] = Utc(source, "FixtureDateUtc"),
            ["PredictionTimestampUtc"] = Utc(source, "PredictionTimestampUtc"),
            ["FeatureAsOfUtc"] = Utc(source, "FeatureAsOfUtc"),
            ["OddsTimestampUtc"] = Utc(source, "OddsTimestampUtc"),
            ["OutcomeAvailableUtc"] = Utc(source, "OutcomeAvailableUtc"),
            ["League"] = Text(source, "League"),
            ["HomeTeam"] = Text(source, "HomeTeam"),
            ["AwayTeam"] = Text(source, "AwayTeam"),
            ["Bookmaker"] = Text(source, "Bookmaker"),
            ["MarketType"] = marketType,
            ["Selection"] = Text(source, "Selection"),
            ["Line"] = Decimal(source, "Line"),
            ["OverOdds"] = Decimal(source, "OverOdds"),
            ["UnderOdds"] = Decimal(source, "UnderOdds"),
            ["SelectedOdds"] = Decimal(source, "SelectedOdds"),
            ["LegacyPrediction"] = Double(source, "LegacyPrediction"),
            ["LegacyModelVersion"] = legacyVersion,
            ["LegacyModelTrainedThroughUtc"] = Utc(source, "LegacyModelTrainedThroughUtc"),
            ["Prediction2026"] = Double(source, "Prediction2026"),
            ["Model2026Version"] = model2026Version,
            ["Model2026TrainedThroughUtc"] = Utc(source, "Model2026TrainedThroughUtc"),
            ["ContextPrediction"] = Double(source, "ContextPrediction"),
            ["HistoricalMean"] = Double(source, "HistoricalMean"),
            ["HistoricalStd"] = Double(source, "HistoricalStd"),
            ["HistoryCount"] = Integer(source, "HistoryCount"),
            ["DataQualityScore"] = Double(source, "DataQualityScore"),
            ["ActualValue"] = Double(source, "ActualValue"),
            ["ConfigurationVersion"] = configurationVersion,
            ["FeatureSchemaVersion"] = featureSchemaVersion,
            ["TrainingContractVersion"] = trainingContract,
            ["FootballIntelligenceEnabled"] = intelligenceEnabled,
            ["FootballIntelligenceVersion"] = intelligenceVersion,
            ["FootballIntelligenceProbabilityAdjustment"] = adjustment,
            ["FootballIntelligenceHomeEvidenceStatus"] = homeStatus,
            ["FootballIntelligenceAwayEvidenceStatus"] = awayStatus,
            ["FootballIntelligenceHomeCutoffUtc"] = OptionalUtc(intelligence, "homeCutoffAtUtc"),
            ["FootballIntelligenceAwayCutoffUtc"] = OptionalUtc(intelligence, "awayCutoffAtUtc"),
            ["IsSynthetic"] = Convert.ToBoolean(source["IsSynthetic"], CultureInfo.InvariantCulture)
        };

        foreach (var optional in new[] { "FPublished", "FProbability", "FEdge", "FExpectedValue" })
        {
            if (source.TryGetValue(optional, out var value) && value is not null and not DBNull)
                normalized[optional] = value;
        }
        return normalized;
    }

    private static string Text(IReadOnlyDictionary<string, object?> source, string name)
    {
        var value = Convert.ToString(source[name], CultureInfo.InvariantCulture)?.Trim();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Training export value '{name}' is blank.");
    }

    private static string Utc(IReadOnlyDictionary<string, object?> source, string name) =>
        FormatUtc(source[name] ?? throw new InvalidDataException($"Missing '{name}'."), name);

    private static decimal Decimal(IReadOnlyDictionary<string, object?> source, string name) =>
        Convert.ToDecimal(source[name], CultureInfo.InvariantCulture);

    private static double Double(IReadOnlyDictionary<string, object?> source, string name)
    {
        var value = Convert.ToDouble(source[name], CultureInfo.InvariantCulture);
        return double.IsFinite(value)
            ? value
            : throw new InvalidDataException($"Training export value '{name}' is non-finite.");
    }

    private static int Integer(IReadOnlyDictionary<string, object?> source, string name) =>
        Convert.ToInt32(source[name], CultureInfo.InvariantCulture);

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!TryProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Snapshot object '{name}' is required.");
        return value;
    }

    private static string RequiredText(JsonElement parent, string name)
    {
        if (!TryProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Snapshot text '{name}' is required.");
        return value.GetString()!.Trim();
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!TryProperty(parent, name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Snapshot boolean '{name}' is required.");
        return value.GetBoolean();
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!TryProperty(parent, name, out var value) || !value.TryGetDouble(out var number)
            || !double.IsFinite(number))
            throw new InvalidDataException($"Snapshot finite number '{name}' is required.");
        return number;
    }

    private static string? OptionalUtc(JsonElement parent, string name)
    {
        if (!TryProperty(parent, name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Snapshot UTC value '{name}' is invalid.");
        return FormatUtc(value.GetString()!, name);
    }

    private static string FormatUtc(object value, string name)
    {
        DateTimeOffset instant = value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(
                dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime()),
            string text when DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) => parsed,
            _ => throw new InvalidDataException($"Training export value '{name}' is not UTC.")
        };
        return instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static bool TryProperty(JsonElement parent, string name, out JsonElement value)
    {
        foreach (var property in parent.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
