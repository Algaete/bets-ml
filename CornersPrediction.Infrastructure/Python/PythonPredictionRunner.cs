using System.Diagnostics;
using System.Text.Json;
using CornersPrediction.Application.Abstractions;
using CornersPrediction.Application.Predictions;
using CornersPrediction.Domain.Predictions;
using CornersPrediction.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CornersPrediction.Infrastructure.Python;

/// <summary>
/// Infrastructure adapter that executes the Python prediction script and parses its JSON output.
/// </summary>
public sealed class PythonPredictionRunner : IPythonPredictionRunner
{
    private readonly PythonPredictionOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PythonPredictionRunner> _logger;

    public PythonPredictionRunner(
        IOptions<PythonPredictionOptions> options,
        IHostEnvironment environment,
        ILogger<PythonPredictionRunner> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Runs predict.py in a separate Python process, passing the request features as a JSON argument.
    /// </summary>
    public async Task<PredictionResult> PredictAsync(JsonElement features, CancellationToken cancellationToken)
    {
        var scriptPath = ResolvePath(_options.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new PredictionException(
                PredictionErrorType.ScriptNotFound,
                $"Prediction script was not found at '{scriptPath}'.");
        }

        var payloadJson = JsonSerializer.Serialize(features);
        _logger.LogInformation("Total corners Python payload: {PayloadJson}", payloadJson);
        var stdout = await RunPythonAsync(scriptPath, payloadJson, cancellationToken);
        _logger.LogInformation("Total corners Python response: {Stdout}", stdout);

        return ParsePrediction(stdout);
    }

    public async Task<OverUnderPredictionResult> PredictOverUnderAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        var scriptPath = ResolvePath(_options.OverUnderScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new PredictionException(
                PredictionErrorType.ScriptNotFound,
                $"Over/Under prediction script was not found at '{scriptPath}'.");
        }

        var modelPath = ResolvePath(_options.OverUnderModelPath);
        if (!File.Exists(modelPath))
        {
            throw new PredictionException(
                PredictionErrorType.ScriptNotFound,
                $"Over/Under model was not found at '{modelPath}'.");
        }

        var payloadJson = JsonSerializer.Serialize(features);
        var stdout = await RunPythonAsync(scriptPath, payloadJson, cancellationToken, modelPath);

        return ParseOverUnderPrediction(stdout);
    }

    public async Task<ShotsOnGoalPredictionResult> PredictShotsOnGoalAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        var scriptPath = ResolvePath(_options.ShotsOnGoalScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new PredictionException(
                PredictionErrorType.ScriptNotFound,
                $"Shots-on-goal prediction script was not found at '{scriptPath}'.");
        }

        var modelPath = ResolvePath(_options.ShotsOnGoalModelPath);
        if (!File.Exists(modelPath) && !Directory.Exists(modelPath))
        {
            throw new PredictionException(
                PredictionErrorType.ScriptNotFound,
                $"Shots/SOG model directory or artifact was not found at '{modelPath}'.");
        }

        var payloadJson = JsonSerializer.Serialize(features);
        _logger.LogInformation("Shots/SOG Python payload: {PayloadJson}", payloadJson);
        var stdout = await RunPythonAsync(scriptPath, payloadJson, cancellationToken, modelPath);
        _logger.LogInformation("Shots/SOG Python response: {Stdout}", stdout);

        return ParseShotsOnGoalPrediction(stdout);
    }

    public async Task<DebugModelPredictionResult> PredictDebugModelAsync(
        string modelKey,
        JsonElement features,
        CancellationToken cancellationToken)
    {
        var scriptPath = ResolvePath(_options.ModelDebugScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new PredictionException(
                PredictionErrorType.ScriptNotFound,
                $"Debug model prediction script was not found at '{scriptPath}'.");
        }

        var modelPath = ResolvePath(_options.ModelDebugModelPath);
        if (!Directory.Exists(modelPath))
        {
            throw new PredictionException(
                PredictionErrorType.ScriptNotFound,
                $"Debug model directory was not found at '{modelPath}'.");
        }

        var payloadJson = JsonSerializer.Serialize(new { modelKey, features });
        _logger.LogInformation("Debug model {ModelKey} Python payload: {PayloadJson}", modelKey, payloadJson);
        var stdout = await RunPythonAsync(scriptPath, payloadJson, cancellationToken, modelPath);
        _logger.LogInformation("Debug model {ModelKey} Python response: {Stdout}", modelKey, stdout);

        return ParseDebugModelPrediction(stdout);
    }

    private async Task<string> RunPythonAsync(
        string scriptPath,
        string payloadJson,
        CancellationToken cancellationToken,
        string? modelPath = null)
    {
        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutablePath(_options.PythonExecutable),
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? _environment.ContentRootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(payloadJson);
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            startInfo.ArgumentList.Add(modelPath);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            _logger.LogInformation("Starting Python prediction script {ScriptPath}", scriptPath);

            if (!process.Start())
            {
                throw new PredictionException(
                    PredictionErrorType.ProcessFailed,
                    "Python process could not be started.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "Python prediction failed with exit code {ExitCode}. stderr: {StdErr}",
                    process.ExitCode,
                    stderr);

                var errorType = ResolveProcessErrorType(stderr);

                throw new PredictionException(
                    errorType,
                    $"Python prediction failed with exit code {process.ExitCode}: {stderr}");
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                var actionableStderr = ExtractActionableStderr(stderr, out var debugLineCount);
                if (!string.IsNullOrWhiteSpace(actionableStderr))
                {
                    _logger.LogWarning("Python prediction stderr: {StdErr}", actionableStderr);
                }
                else
                {
                    _logger.LogDebug(
                        "Python prediction completed with {DebugLineCount} diagnostic lines.",
                        debugLineCount);
                }
            }

            return stdout;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            _logger.LogError("Python prediction timed out after {TimeoutSeconds} seconds", timeoutSeconds);

            throw new PredictionException(
                PredictionErrorType.Timeout,
                $"Python prediction timed out after {timeoutSeconds} seconds.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _logger.LogError(exception, "Python executable '{PythonExecutable}' was not found", _options.PythonExecutable);

            throw new PredictionException(
                PredictionErrorType.PythonNotFound,
                $"Python executable '{_options.PythonExecutable}' was not found.",
                exception);
        }
    }

    private static string ExtractActionableStderr(string stderr, out int debugLineCount)
    {
        debugLineCount = 0;
        var actionable = new List<string>();
        foreach (var rawLine in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("debug", out _))
                {
                    debugLineCount++;
                    continue;
                }
            }
            catch (JsonException)
            {
                // Non-JSON stderr is actionable and must remain visible.
            }

            actionable.Add(line);
        }

        var message = string.Join(Environment.NewLine, actionable);
        const int maximumLoggedCharacters = 4000;
        return message.Length <= maximumLoggedCharacters
            ? message
            : message[..maximumLoggedCharacters] + "…";
    }

    /// <summary>
    /// Resolves a relative path from the API content root, or returns an absolute path unchanged.
    /// </summary>
    private string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configuredPath));
    }

    /// <summary>
    /// Keeps command names such as "python3" untouched, but resolves relative executable paths.
    /// </summary>
    private string ResolveExecutablePath(string configuredPath)
    {
        if (!configuredPath.Contains(Path.DirectorySeparatorChar) &&
            !configuredPath.Contains(Path.AltDirectorySeparatorChar))
        {
            return configuredPath;
        }

        return ResolvePath(configuredPath);
    }

    /// <summary>
    /// Reads stdout from Python and converts {"predicted_total_corners": number} into the domain result.
    /// </summary>
    private static PredictionResult ParsePrediction(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                "Python prediction returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;

            if (root.TryGetProperty("predTotalDirect", out _))
            {
                var predTotalDirect = ReadRequiredDouble(root, "predTotalDirect");
                var predHomeCorners = ReadRequiredDouble(root, "predHomeCorners");
                var predAwayCorners = ReadRequiredDouble(root, "predAwayCorners");
                var predTotalCombined = ReadRequiredDouble(root, "predTotalCombined");
                var predFinal = ReadRequiredDouble(root, "predFinal");
                var predFinalRounded = ReadRequiredDouble(root, "predFinalRounded");
                var rangeLow = ReadRequiredDouble(root, "rangeLow");
                var rangeHigh = ReadRequiredDouble(root, "rangeHigh");
                var bettingLine = ReadOptionalDouble(root, "bettingLine");
                var distanceToLine = ReadOptionalDouble(root, "distanceToLine");
                var recommendedSide = ReadOptionalString(root, "recommendedSide") ?? "N/A";
                var confidence = ReadOptionalString(root, "confidence") ?? "N/A";
                var message = ReadOptionalString(root, "message") ?? string.Empty;

                return PredictionResult.CreateEnsemble(
                    predTotalDirect,
                    predHomeCorners,
                    predAwayCorners,
                    predTotalCombined,
                    predFinal,
                    predFinalRounded,
                    rangeLow,
                    rangeHigh,
                    bettingLine,
                    recommendedSide,
                    distanceToLine,
                    confidence,
                    message);
            }

            if (!root.TryGetProperty("predicted_total_corners", out var predictionElement) ||
                predictionElement.ValueKind is not JsonValueKind.Number ||
                !predictionElement.TryGetDouble(out var prediction))
            {
                throw new PredictionException(
                    PredictionErrorType.InvalidOutput,
                    "Python prediction did not include a numeric 'predicted_total_corners' value.");
            }

            return PredictionResult.Create(prediction);
        }
        catch (JsonException exception)
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                $"Python prediction returned invalid JSON: {stdout}",
                exception);
        }
    }

    private static OverUnderPredictionResult ParseOverUnderPrediction(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                "Python Over/Under prediction returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;

            var bettingLine = ReadRequiredDouble(root, "bettingLine");
            var predictedClass = ReadRequiredInt(root, "predictedClass");
            var distanceToLine = ReadRequiredDouble(root, "distanceToLine");
            var absDistanceToLine = ReadRequiredDouble(root, "absDistanceToLine");
            var overProbability = ReadOptionalDouble(root, "overProbability");
            var underProbability = ReadOptionalDouble(root, "underProbability");

            return new OverUnderPredictionResult(
                bettingLine,
                predictedClass,
                overProbability,
                underProbability,
                distanceToLine,
                absDistanceToLine);
        }
        catch (JsonException exception)
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                $"Python Over/Under prediction returned invalid JSON: {stdout}",
                exception);
        }
    }

    private static ShotsOnGoalPredictionResult ParseShotsOnGoalPrediction(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                "Python shots-on-goal prediction returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;

            if (root.TryGetProperty("markets", out var marketsElement))
            {
                var shots = marketsElement.TryGetProperty("shots", out var shotsElement)
                    ? ParseMarketPrediction(shotsElement)
                    : null;
                var sog = marketsElement.TryGetProperty("sog", out var sogElement)
                    ? ParseMarketPrediction(sogElement)
                    : throw new PredictionException(
                        PredictionErrorType.InvalidOutput,
                        "Python shots/SOG prediction did not include a 'markets.sog' object.");
                var goals = marketsElement.TryGetProperty("goals", out var goalsElement)
                    ? ParseMarketPrediction(goalsElement)
                    : null;
                var debug = root.TryGetProperty("debug", out var debugElement)
                    ? debugElement.Clone()
                    : (JsonElement?)null;

                return new ShotsOnGoalPredictionResult(shots, sog, goals, debug);
            }

            if (!root.TryGetProperty("predicted_shots_on_goal", out var predictionElement) ||
                predictionElement.ValueKind is not JsonValueKind.Number ||
                !predictionElement.TryGetDouble(out var prediction))
            {
                throw new PredictionException(
                    PredictionErrorType.InvalidOutput,
                    "Python shots-on-goal prediction did not include a numeric 'predicted_shots_on_goal' value.");
            }

            return new ShotsOnGoalPredictionResult(prediction);
        }
        catch (JsonException exception)
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                $"Python shots-on-goal prediction returned invalid JSON: {stdout}",
                exception);
        }
    }

    private static DebugModelPredictionResult ParseDebugModelPrediction(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                "Python debug model prediction returned an empty response.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<DebugModelPredictionResult>(stdout, JsonOptions);
            if (result is null || string.IsNullOrWhiteSpace(result.ModelKey))
            {
                throw new PredictionException(
                    PredictionErrorType.InvalidOutput,
                    $"Python debug model prediction returned invalid JSON: {stdout}");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                $"Python debug model prediction returned invalid JSON: {stdout}",
                exception);
        }
    }

    private static MarketPredictionResult ParseMarketPrediction(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                "Python market prediction value was not a JSON object.");
        }

        var prediction = ReadRequiredDouble(element, "prediction");
        var finalPrediction = ReadOptionalDouble(element, "finalPrediction") ?? prediction;

        return new MarketPredictionResult(
            ReadOptionalDouble(element, "line"),
            prediction,
            ReadOptionalString(element, "recommendation"),
            ReadOptionalString(element, "confidence"),
            ReadOptionalDouble(element, "distance"),
            ReadOptionalDouble(element, "historicalAccuracy"),
            ReadOptionalDouble(element, "homePrediction"),
            ReadOptionalDouble(element, "awayPrediction"),
            ReadOptionalDouble(element, "totalDirectPrediction"),
            ReadOptionalDouble(element, "combinedHomeAwayPrediction"),
            finalPrediction,
            ReadOptionalDouble(element, "rawPrediction"),
            ReadOptionalBool(element, "sanityAdjusted") ?? false,
            ReadOptionalString(element, "sanityReason"),
            ReadOptionalDouble(element, "featurePrior"));
    }

    private static double ReadRequiredDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is not JsonValueKind.Number ||
            !element.TryGetDouble(out var value))
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                $"Python Over/Under prediction did not include a numeric '{propertyName}' value.");
        }

        return value;
    }

    private static int ReadRequiredInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is not JsonValueKind.Number ||
            !element.TryGetInt32(out var value))
        {
            throw new PredictionException(
                PredictionErrorType.InvalidOutput,
                $"Python Over/Under prediction did not include an integer '{propertyName}' value.");
        }

        return value;
    }

    private static double? ReadOptionalDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind is JsonValueKind.Number && element.TryGetDouble(out var value))
        {
            return value;
        }

        throw new PredictionException(
            PredictionErrorType.InvalidOutput,
            $"Python Over/Under prediction included a non-numeric '{propertyName}' value.");
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind is JsonValueKind.String)
        {
            return element.GetString();
        }

        return element.ToString();
    }

    private static bool? ReadOptionalBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind is JsonValueKind.False)
        {
            return false;
        }

        throw new PredictionException(
            PredictionErrorType.InvalidOutput,
            $"Python Over/Under prediction included a non-boolean '{propertyName}' value.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Maps structured Python stderr errors into application error categories.
    /// </summary>
    private static PredictionErrorType ResolveProcessErrorType(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return PredictionErrorType.ProcessFailed;
        }

        try
        {
            using var document = JsonDocument.Parse(stderr);
            if (document.RootElement.TryGetProperty("error_type", out var errorTypeElement) &&
                errorTypeElement.GetString() == "missing_dependency")
            {
                return PredictionErrorType.MissingDependency;
            }
        }
        catch (JsonException)
        {
        }

        return PredictionErrorType.ProcessFailed;
    }

    /// <summary>
    /// Stops Python when the request is cancelled by timeout.
    /// </summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
