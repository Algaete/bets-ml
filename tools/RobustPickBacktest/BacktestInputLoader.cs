using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RobustPickBacktest;

public static class BacktestInputLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static async Task<(IReadOnlyList<ResolvedEvaluation> Rows, string Sha256)> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return (Parse(bytes), Hash(bytes));
    }

    public static IReadOnlyList<ResolvedEvaluation> Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => DeserializeArray(document.RootElement),
                JsonValueKind.Object when TryGetEvaluations(document.RootElement, out var evaluations) =>
                    DeserializeArray(evaluations),
                JsonValueKind.Object =>
                    [document.RootElement.Deserialize<ResolvedEvaluation>(Options)
                        ?? throw new InvalidDataException("The JSON object is not an evaluation.")],
                _ => throw new InvalidDataException("Expected a JSON array, object, or JSONL stream.")
            };
        }
        catch (JsonException)
        {
            return ParseJsonLines(Encoding.UTF8.GetString(utf8));
        }
    }

    public static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IReadOnlyList<ResolvedEvaluation> DeserializeArray(JsonElement element) =>
        element.Deserialize<List<ResolvedEvaluation>>(Options)
        ?? throw new InvalidDataException("The JSON evaluation array is null.");

    private static bool TryGetEvaluations(JsonElement root, out JsonElement evaluations)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("evaluations", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Array)
            {
                evaluations = property.Value;
                return true;
            }
        }
        evaluations = default;
        return false;
    }

    private static IReadOnlyList<ResolvedEvaluation> ParseJsonLines(string content)
    {
        var rows = new List<ResolvedEvaluation>();
        var lineNumber = 0;
        foreach (var line in content.Split('\n'))
        {
            lineNumber++;
            var value = line.Trim().TrimStart('\uFEFF');
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }
            try
            {
                rows.Add(JsonSerializer.Deserialize<ResolvedEvaluation>(value, Options)
                    ?? throw new JsonException("Null JSONL row."));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Invalid JSONL at line {lineNumber}.", exception);
            }
        }
        return rows;
    }
}
