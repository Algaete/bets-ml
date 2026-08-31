using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Domain.FootballIntelligence;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.FootballIntelligence;

public sealed class OpenAiNewsFactExtractor : INewsFactExtractor, ILlmFactExtractionClient
{
    private const string Instructions = """
Actúa como extractor de hechos prepartido de fútbol. No recomiendes apuestas ni predigas resultados.
Usa únicamente el artículo y los metadatos entregados. No inventes lesiones, jugadores, posiciones ni identificadores.
La ausencia de información no significa disponibilidad. "Volvió a entrenar" no significa automáticamente "disponible".
"Será evaluado" mantiene al jugador como duda. "Fue descartado" significa que no estará disponible.
"Cumplió suspensión" es retorno desde suspensión. "Será reservado" es descanso o rotación.
Distingue confirmado, reportado, esperado, especulación y rumor. Ignora partidos antiguos salvo que afecten explícitamente este fixture.
Incluye una evidencia textual breve por hecho y devuelve exclusivamente la salida estructurada solicitada.
""";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly HttpClient _httpClient;
    private readonly NewsLlmOptions _options;
    private readonly ILogger<OpenAiNewsFactExtractor> _logger;

    public OpenAiNewsFactExtractor(
        HttpClient httpClient,
        IOptions<NewsLlmOptions> options,
        ILogger<OpenAiNewsFactExtractor> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<NewsExtractionResult> ExtractAsync(
        NewsExtractionRequest request,
        CancellationToken cancellationToken) =>
        ExtractStructuredAsync(request, cancellationToken);

    public async Task<NewsExtractionResult> ExtractStructuredAsync(
        NewsExtractionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("OpenAI news extraction is not completely configured.");

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var message = BuildRequest(request);
                using var response = await _httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests
                     || (int)response.StatusCode >= 500)
                    && attempt < 3)
                {
                    await DelayAsync(response, attempt, cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var responseJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ParseResponse(responseJson.RootElement, request);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 3)
            {
                lastError = new TimeoutException("OpenAI news extraction timed out.");
            }
            catch (JsonException exception) when (attempt < 3)
            {
                lastError = exception;
            }
            catch (InvalidDataException exception) when (attempt < 3)
            {
                lastError = exception;
            }

            if (attempt < 3)
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
        }

        _logger.LogWarning(lastError, "Structured news extraction failed after the configured retries");
        throw lastError ?? new InvalidDataException("Structured news extraction failed.");
    }

    private HttpRequestMessage BuildRequest(NewsExtractionRequest request)
    {
        var input = JsonSerializer.Serialize(new
        {
            request.FixtureId,
            request.KickoffUtc,
            request.CutoffAtUtc,
            request.TeamName,
            request.OpponentName,
            request.ArticleTitle,
            request.PublishedAtUtc,
            request.LanguageCode,
            KnownPlayers = request.KnownPlayerNames ?? [],
            Article = request.ArticleText
        }, JsonOptions);
        var payload = new
        {
            model = _options.Model,
            instructions = Instructions,
            input,
            store = false,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "football_news_extraction",
                    strict = true,
                    schema = BuildSchema()
                }
            }
        };
        var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        message.Headers.Accept.ParseAdd("application/json");
        message.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
        return message;
    }

    private NewsExtractionResult ParseResponse(JsonElement root, NewsExtractionRequest request)
    {
        var refusal = FindContent(root, "refusal");
        if (!string.IsNullOrWhiteSpace(refusal))
            throw new InvalidDataException("The semantic extractor refused the article.");
        var outputText = FindContent(root, "output_text");
        if (string.IsNullOrWhiteSpace(outputText))
            throw new InvalidDataException("OpenAI response did not contain structured output text.");
        var payload = JsonSerializer.Deserialize<LlmExtractionPayload>(outputText, JsonOptions)
            ?? throw new InvalidDataException("OpenAI structured output was empty.");
        ValidatePayload(payload, request);
        var usage = root.TryGetProperty("usage", out var usageNode) ? usageNode : default;
        var inputTokens = ReadInt(usage, "input_tokens");
        var outputTokens = ReadInt(usage, "output_tokens");
        return new NewsExtractionResult(
            payload.FixtureRelevance,
            payload.Facts.Select(value => new ExtractedNewsFact(
                value.TeamName,
                value.PlayerName,
                value.EventType,
                value.AvailabilityStatus,
                value.Certainty,
                value.ProbabilityAvailable,
                value.Reason,
                value.ExpectedReturnAtUtc,
                value.ExpectedMinutesDelta,
                value.Evidence,
                value.ExtractionConfidence)).ToArray(),
            payload.TeamSignals,
            $"OpenAI:{_options.Model}",
            _options.PromptVersion,
            inputTokens,
            outputTokens);
    }

    private static void ValidatePayload(LlmExtractionPayload payload, NewsExtractionRequest request)
    {
        if (payload.FixtureRelevance is < 0m or > 1m
            || payload.Facts is null
            || payload.TeamSignals is null)
            throw new InvalidDataException("Structured extraction contains invalid top-level values.");
        var players = (request.KnownPlayerNames ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in payload.Facts)
        {
            if (!fact.TeamName.Equals(request.TeamName, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(fact.Evidence)
                || fact.ExtractionConfidence is < 0m or > 1m
                || fact.ProbabilityAvailable is < 0m or > 1m
                || fact.PlayerName is not null && !players.Contains(fact.PlayerName))
                throw new InvalidDataException("Structured extraction contains an unknown entity or invalid fact.");
        }
        if (payload.TeamSignals.RotationRisk is < 0m or > 1m
            || payload.TeamSignals.FatigueRisk is < 0m or > 1m
            || payload.TeamSignals.MoraleSignal is < -1m or > 1m)
            throw new InvalidDataException("Structured extraction contains invalid team signals.");
    }

    private static string? FindContent(JsonElement root, string contentType)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type)
                    && type.GetString() == contentType)
                {
                    var property = contentType == "refusal" ? "refusal" : "text";
                    return part.TryGetProperty(property, out var value) ? value.GetString() : null;
                }
            }
        }
        return null;
    }

    private static async Task DelayAsync(
        HttpResponseMessage response,
        int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 2);
        await Task.Delay(delay, cancellationToken);
    }

    private static int ReadInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static object BuildSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "fixtureRelevance", "facts", "teamSignals" },
        properties = new Dictionary<string, object>
        {
            ["fixtureRelevance"] = NumberSchema(0, 1),
            ["facts"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[]
                    {
                        "teamName", "playerName", "eventType", "availabilityStatus", "certainty",
                        "probabilityAvailable", "reason", "expectedReturnAtUtc", "expectedMinutesDelta",
                        "evidence", "extractionConfidence"
                    },
                    properties = new Dictionary<string, object>
                    {
                        ["teamName"] = new { type = "string" },
                        ["playerName"] = NullableSchema("string"),
                        ["eventType"] = EnumSchema<FootballNewsEventType>(),
                        ["availabilityStatus"] = EnumSchema<AvailabilityStatus>(),
                        ["certainty"] = EnumSchema<FactCertainty>(),
                        ["probabilityAvailable"] = NullableNumberSchema(0, 1),
                        ["reason"] = NullableSchema("string"),
                        ["expectedReturnAtUtc"] = NullableSchema("string"),
                        ["expectedMinutesDelta"] = NullableNumberSchema(-180, 180),
                        ["evidence"] = new { type = "string" },
                        ["extractionConfidence"] = NumberSchema(0, 1)
                    }
                }
            },
            ["teamSignals"] = new
            {
                type = "object",
                additionalProperties = false,
                required = new[]
                {
                    "rotationRisk", "fatigueRisk", "moraleSignal", "coachChangeDetected",
                    "tacticalChangeExpected", "formationChangeExpected"
                },
                properties = new Dictionary<string, object>
                {
                    ["rotationRisk"] = NumberSchema(0, 1),
                    ["fatigueRisk"] = NumberSchema(0, 1),
                    ["moraleSignal"] = NumberSchema(-1, 1),
                    ["coachChangeDetected"] = new { type = "boolean" },
                    ["tacticalChangeExpected"] = new { type = "boolean" },
                    ["formationChangeExpected"] = new { type = "boolean" }
                }
            }
        }
    };

    private static object NumberSchema(decimal minimum, decimal maximum) =>
        new { type = "number", minimum, maximum };
    private static object NullableNumberSchema(decimal minimum, decimal maximum) =>
        new { type = new[] { "number", "null" }, minimum, maximum };
    private static object NullableSchema(string type) => new { type = new[] { type, "null" } };
    private static object EnumSchema<T>() where T : struct, Enum =>
        new { type = "string", @enum = Enum.GetNames<T>() };

    private sealed record LlmExtractionPayload(
        decimal FixtureRelevance,
        IReadOnlyList<LlmFactPayload> Facts,
        TeamNewsSignals TeamSignals);

    private sealed record LlmFactPayload(
        string TeamName,
        string? PlayerName,
        FootballNewsEventType EventType,
        AvailabilityStatus AvailabilityStatus,
        FactCertainty Certainty,
        decimal? ProbabilityAvailable,
        string? Reason,
        DateTime? ExpectedReturnAtUtc,
        decimal? ExpectedMinutesDelta,
        string Evidence,
        decimal ExtractionConfidence);
}
