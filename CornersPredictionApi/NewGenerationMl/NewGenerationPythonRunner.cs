using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.NewGenerationMl;

public sealed class NewGenerationPythonRunner : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NewGenerationMlOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<NewGenerationPythonRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _stderr = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private string? _registryKey;

    public NewGenerationPythonRunner(
        IOptions<NewGenerationMlOptions> options,
        IWebHostEnvironment environment,
        ILogger<NewGenerationPythonRunner> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<NewGenerationModelCatalogInfo> HealthAsync(
        IReadOnlyList<NewGenerationModelPackage.PackageSnapshot> packages,
        CancellationToken cancellationToken)
    {
        var json = await ExecuteAsync("health", packages, null, cancellationToken);
        return JsonSerializer.Deserialize<NewGenerationModelCatalogInfo>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Python health response was empty.");
    }

    internal async Task<IReadOnlyList<PythonPredictionEnvelope>> PredictManyAsync(
        IReadOnlyList<NewGenerationModelPackage.PackageSnapshot> packages,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> payloads,
        CancellationToken cancellationToken)
    {
        var json = await ExecuteAsync("predict_many", packages, payloads, cancellationToken);
        var envelope = JsonSerializer.Deserialize<PythonBatchPredictionEnvelope>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Python prediction response was empty.");
        return envelope.Predictions;
    }

    private async Task<string> ExecuteAsync(
        string action,
        IReadOnlyList<NewGenerationModelPackage.PackageSnapshot> packages,
        object? payload,
        CancellationToken cancellationToken)
    {
        if (packages.Count == 0)
        {
            throw new InvalidOperationException("At least one validated model package is required.");
        }
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 600));
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var lockTaken = false;
        var started = Stopwatch.GetTimestamp();
        try
        {
            await _gate.WaitAsync(linkedCts.Token);
            lockTaken = true;
            await EnsureWorkerAsync(packages, linkedCts.Token);

            var requestId = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(new { id = requestId, action, payload });
            await _stdin!.WriteLineAsync(request.AsMemory(), linkedCts.Token);
            await _stdin.FlushAsync(linkedCts.Token);
            var line = await _stdout!.ReadLineAsync(linkedCts.Token);
            if (line is null)
            {
                var error = ReadWorkerError();
                ResetWorker();
                throw new IOException(error);
            }

            var response = JsonSerializer.Deserialize<WorkerResponse>(line, SerializerOptions)
                ?? throw new JsonException("Python worker returned an empty response.");
            if (!string.Equals(response.Id, requestId, StringComparison.Ordinal))
            {
                throw new JsonException("Python worker response did not match the current request.");
            }
            if (!response.Ok)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(response.Error)
                        ? "New-generation Python worker rejected the request."
                        : response.Error);
            }
            if (response.Result.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Python worker response has no result object.");
            }

            _logger.LogInformation(
                "New-generation model action {Action} across {ModelCount} models completed in {DurationMs:F0} ms",
                action,
                packages.Count,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response.Result.GetRawText();
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ResetWorker();
            throw new TimeoutException($"New-generation model action timed out after {timeout.TotalSeconds:F0} seconds.");
        }
        catch (OperationCanceledException)
        {
            ResetWorker();
            throw;
        }
        catch (IOException)
        {
            ResetWorker();
            throw;
        }
        catch (JsonException)
        {
            ResetWorker();
            throw;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            ResetWorker();
            throw new InvalidOperationException(
                $"Python executable '{_options.PythonExecutable}' is unavailable. Install requirements-new-generation.txt.",
                exception);
        }
        finally
        {
            if (lockTaken)
            {
                _gate.Release();
            }
        }
    }

    private async Task EnsureWorkerAsync(
        IReadOnlyList<NewGenerationModelPackage.PackageSnapshot> packages,
        CancellationToken cancellationToken)
    {
        var manifestPaths = packages
            .Select(package => package.ManifestPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var registryKey = string.Join('\n', manifestPaths);
        if (_process is { HasExited: false } &&
            string.Equals(_registryKey, registryKey, StringComparison.Ordinal))
        {
            return;
        }

        ResetWorker();
        var scriptPath = ResolvePath(_options.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("New-generation Python inference script is missing.", scriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable(_options.PythonExecutable),
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? _environment.ContentRootPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--action");
        startInfo.ArgumentList.Add("serve");
        foreach (var manifestPath in manifestPaths)
        {
            startInfo.ArgumentList.Add("--manifest");
            startInfo.ArgumentList.Add(manifestPath);
        }

        lock (_stderr)
        {
            _stderr.Clear();
        }
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += OnWorkerError;
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("New-generation Python worker could not be started.");
        }
        process.BeginErrorReadLine();
        _process = process;
        _stdin = process.StandardInput;
        _stdout = process.StandardOutput;

        var readyLine = await _stdout.ReadLineAsync(cancellationToken);
        if (readyLine is null)
        {
            throw new InvalidOperationException(ReadWorkerError());
        }
        var ready = JsonSerializer.Deserialize<WorkerReady>(readyLine, SerializerOptions)
            ?? throw new JsonException("Python worker returned an empty startup response.");
        if (!string.Equals(ready.Event, "ready", StringComparison.Ordinal) ||
            ready.Result.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Python worker did not report a healthy startup.");
        }
        _registryKey = registryKey;
        _logger.LogInformation(
            "New-generation Python worker loaded {ModelCount} model versions",
            packages.Count);
    }

    private void OnWorkerError(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data))
        {
            return;
        }
        lock (_stderr)
        {
            if (_stderr.Length < 16_384)
            {
                _stderr.AppendLine(args.Data);
            }
        }
    }

    private string ReadWorkerError()
    {
        string stderr;
        lock (_stderr)
        {
            stderr = _stderr.ToString().Trim();
        }
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "New-generation Python worker stopped without an error message.";
        }
        try
        {
            using var document = JsonDocument.Parse(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? stderr;
            }
        }
        catch (JsonException)
        {
        }
        return stderr;
    }

    private string ResolvePath(string path) => Path.GetFullPath(
        Path.IsPathRooted(path) ? path : Path.Combine(_environment.ContentRootPath, path));

    private string ResolveExecutable(string path) =>
        path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar)
            ? ResolvePath(path)
            : path;

    private void ResetWorker()
    {
        var process = _process;
        _process = null;
        _stdin = null;
        _stdout = null;
        _registryKey = null;
        if (process is null)
        {
            return;
        }
        try
        {
            process.ErrorDataReceived -= OnWorkerError;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            ResetWorker();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private sealed class WorkerReady
    {
        public string? Event { get; init; }
        public JsonElement Result { get; init; }
    }

    private sealed class WorkerResponse
    {
        public string? Id { get; init; }
        public bool Ok { get; init; }
        public JsonElement Result { get; init; }
        public string? Error { get; init; }
    }
}
