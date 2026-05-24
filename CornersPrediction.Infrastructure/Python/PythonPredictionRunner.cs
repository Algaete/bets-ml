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

        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        var payloadJson = JsonSerializer.Serialize(features);

        // This is the exact point where .NET calls Python.
        // Arguments:
        // 1. scriptPath -> predict.py
        // 2. payloadJson -> the model features serialized as JSON
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
                _logger.LogWarning("Python prediction stderr: {StdErr}", stderr);
            }

            return ParsePrediction(stdout);
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

            if (!root.TryGetProperty("predicted_total_corners", out var predictionElement) ||
                predictionElement.ValueKind is not JsonValueKind.Number ||
                !predictionElement.TryGetDouble(out var prediction))
            {
                throw new PredictionException(
                    PredictionErrorType.InvalidOutput,
                    "Python prediction did not include a numeric 'predicted_total_corners' value.");
            }

            // TODO: Connect model_home_corners_v4 and model_away_corners_v4 here,
            // then pass their outputs into PredictionResult.Create as comparison values.
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
