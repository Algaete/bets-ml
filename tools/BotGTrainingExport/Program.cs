using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BotGTrainingExport;
using Microsoft.Data.SqlClient;

return await ProgramEntry.RunAsync(args);

internal static class ProgramEntry
{
    private const string ConnectionVariable = "BOT_G_SQL_CONNECTION_STRING";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
                return SelfTest();
            var options = ExportOptions.Parse(args);
            var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"Set {ConnectionVariable}; connection strings are intentionally not accepted on the command line.");

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            var report = await ExportAsync(connectionString, options, cancellation.Token);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Bot G export failed: {exception.Message}");
            return 2;
        }
    }

    private static async Task<object> ExportAsync(
        string connectionString,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var output = Path.GetFullPath(options.OutputPath);
        var directory = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException("Output directory could not be resolved.");
        Directory.CreateDirectory(directory);
        if (File.Exists(output) || File.Exists(output + ".manifest.json"))
            throw new IOException("Output or manifest already exists; immutable exports are never overwritten.");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        var marketCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var rowCount = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var file = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.sp_GetBotG2026TrainingExport", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = options.CommandTimeoutSeconds
            };
            command.Parameters.Add(new SqlParameter("@AsOfUtc", SqlDbType.DateTime2)
                { Value = options.AsOfUtc.UtcDateTime });
            command.Parameters.Add(new SqlParameter("@DateFromUtc", SqlDbType.DateTime2)
                { Value = options.DateFromUtc?.UtcDateTime ?? (object)DBNull.Value });
            command.Parameters.Add(new SqlParameter("@DateToUtc", SqlDbType.DateTime2)
                { Value = options.DateToUtc?.UtcDateTime ?? (object)DBNull.Value });
            command.Parameters.Add(new SqlParameter("@ConfigurationVersion", SqlDbType.NVarChar, 80)
                { Value = ExportContract.ConfigurationVersion });
            command.Parameters.Add(new SqlParameter("@OnlyOutcomeAvailable", SqlDbType.Bit) { Value = true });

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var source = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                    source[reader.GetName(ordinal)] = await reader.IsDBNullAsync(ordinal, cancellationToken)
                        ? null
                        : reader.GetValue(ordinal);
                var row = ExportContract.Normalize(source);
                var candidateId = Convert.ToString(row["CandidateId"], CultureInfo.InvariantCulture)!;
                if (!candidateIds.Add(candidateId))
                    throw new InvalidDataException($"Duplicate CandidateId '{candidateId}' in export.");
                var market = Convert.ToString(row["MarketType"], CultureInfo.InvariantCulture)!;
                marketCounts[market] = marketCounts.GetValueOrDefault(market) + 1;
                var payload = JsonSerializer.SerializeToUtf8Bytes(row, JsonOptions);
                await file.WriteAsync(payload, cancellationToken);
                await file.WriteAsync("\n"u8.ToArray(), cancellationToken);
                hash.AppendData(payload);
                hash.AppendData("\n"u8);
                rowCount++;
            }
            await file.FlushAsync(cancellationToken);
            file.Close();
            if (rowCount == 0)
                throw new InvalidDataException("Stored procedure returned no valid resolved Bot G v1.1 rows.");
            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            File.Move(temporary, output, overwrite: false);
            var manifest = new
            {
                contract = ExportContract.TrainingContractVersion,
                configurationVersion = ExportContract.ConfigurationVersion,
                featureSchemaVersion = ExportContract.FeatureSchemaVersion,
                footballIntelligenceVersion = ExportContract.FootballIntelligenceVersion,
                asOfUtc = options.AsOfUtc.ToString("O", CultureInfo.InvariantCulture),
                dateFromUtc = options.DateFromUtc?.ToString("O", CultureInfo.InvariantCulture),
                dateToUtc = options.DateToUtc?.ToString("O", CultureInfo.InvariantCulture),
                rows = rowCount,
                markets = marketCounts,
                sha256,
                outputFile = Path.GetFileName(output),
                storedProcedure = "dbo.sp_GetBotG2026TrainingExport",
                onlyOutcomeAvailable = true
            };
            File.WriteAllText(
                output + ".manifest.json",
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            return new { status = "COMPLETE", output, manifest = output + ".manifest.json", rows = rowCount, sha256 };
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static int SelfTest()
    {
        var row = SelfTestRow();
        var normalized = ExportContract.Normalize(row);
        Require((string)normalized["TrainingContractVersion"]! == ExportContract.TrainingContractVersion);
        Require((string)normalized["Model2026Version"]! == "targettotalgoals-2026-08-09-trial-53");
        Require((double)normalized["FootballIntelligenceProbabilityAdjustment"]! == 0.01d);
        var old = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
        {
            ["FeatureSnapshotJson"] = ((string)row["FeatureSnapshotJson"]!).Replace(
                ExportContract.TrainingContractVersion, "bot-g-training-export-1.0.0", StringComparison.Ordinal)
        };
        try
        {
            ExportContract.Normalize(old);
            throw new InvalidOperationException("Self-test accepted a v1.0 snapshot.");
        }
        catch (InvalidDataException exception)
        {
            Require(exception.Message.Contains("relabeling", StringComparison.OrdinalIgnoreCase));
        }
        Console.WriteLine("PASS BotGTrainingExport self-test (normalization, exact lineage, v1.0 rejection, no SQL)." );
        return 0;
    }

    private static Dictionary<string, object?> SelfTestRow()
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            lineage = new
            {
                trainingContractVersion = ExportContract.TrainingContractVersion,
                marketType = "TotalGoals",
                legacyModelVersion = "goals_v1",
                model2026Version = "targettotalgoals-2026-08-09-trial-53"
            },
            footballIntelligence = new
            {
                enabled = true,
                version = ExportContract.FootballIntelligenceVersion,
                homeCutoffAtUtc = "2026-08-01T10:00:00Z",
                awayCutoffAtUtc = "2026-08-01T10:00:00Z",
                result = new
                {
                    probabilityAdjustment = 0.01,
                    homeEvidenceStatus = "Available",
                    awayEvidenceStatus = "Available"
                }
            }
        });
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["CandidateId"] = 1L, ["QuoteId"] = "q", ["FixtureId"] = 7L,
            ["FixtureDateUtc"] = DateTime.SpecifyKind(new DateTime(2026, 8, 2, 12, 0, 0), DateTimeKind.Utc),
            ["PredictionTimestampUtc"] = DateTime.SpecifyKind(new DateTime(2026, 8, 1, 12, 0, 0), DateTimeKind.Utc),
            ["FeatureAsOfUtc"] = DateTime.SpecifyKind(new DateTime(2026, 8, 1, 12, 0, 0), DateTimeKind.Utc),
            ["OddsTimestampUtc"] = DateTime.SpecifyKind(new DateTime(2026, 8, 1, 11, 50, 0), DateTimeKind.Utc),
            ["OutcomeAvailableUtc"] = DateTime.SpecifyKind(new DateTime(2026, 8, 2, 18, 0, 0), DateTimeKind.Utc),
            ["League"] = "Test", ["HomeTeam"] = "Home", ["AwayTeam"] = "Away",
            ["Bookmaker"] = "Book", ["MarketType"] = "TotalGoals", ["Selection"] = "Over",
            ["Line"] = 2.5m, ["OverOdds"] = 1.9m, ["UnderOdds"] = 1.9m,
            ["SelectedOdds"] = 1.9m, ["LegacyPrediction"] = 2.6d,
            ["LegacyModelVersion"] = "goals_v1",
            ["LegacyModelTrainedThroughUtc"] = DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Utc),
            ["Prediction2026"] = 2.7d,
            ["Model2026Version"] = "targettotalgoals-2026-08-09-trial-53",
            ["Model2026TrainedThroughUtc"] = DateTime.SpecifyKind(new DateTime(2026, 1, 2), DateTimeKind.Utc),
            ["ContextPrediction"] = 2.65d, ["HistoricalMean"] = 2.5d, ["HistoricalStd"] = 1.1d,
            ["HistoryCount"] = 20, ["DataQualityScore"] = 0.9d, ["ActualValue"] = 3d,
            ["ConfigurationVersion"] = ExportContract.ConfigurationVersion,
            ["FeatureSchemaVersion"] = ExportContract.FeatureSchemaVersion,
            ["FeatureSnapshotJson"] = snapshot, ["IsSynthetic"] = false
        };
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Bot G export self-test assertion failed.");
    }
}

internal sealed record ExportOptions(
    string OutputPath,
    DateTimeOffset AsOfUtc,
    DateTimeOffset? DateFromUtc,
    DateTimeOffset? DateToUtc,
    int CommandTimeoutSeconds)
{
    public static ExportOptions Parse(string[] args)
    {
        string? output = null;
        DateTimeOffset? asOf = null;
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        var timeout = 600;
        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            string Next() => index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Missing value after {name}.");
            switch (name)
            {
                case "--output": output = Next(); break;
                case "--as-of": asOf = ParseUtc(Next(), name); break;
                case "--date-from": from = ParseUtc(Next(), name); break;
                case "--date-to": to = ParseUtc(Next(), name); break;
                case "--timeout-seconds": timeout = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unknown argument '{name}'. See README.md.");
            }
        }
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("--output is required.");
        if (!asOf.HasValue) throw new ArgumentException("--as-of with an explicit UTC offset is required.");
        if (timeout is < 30 or > 3_600) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (from.HasValue && to.HasValue && from >= to)
            throw new ArgumentException("--date-from must be earlier than --date-to.");
        if (to > asOf) throw new ArgumentException("--date-to cannot be after --as-of.");
        return new(output, asOf.Value.ToUniversalTime(), from?.ToUniversalTime(), to?.ToUniversalTime(), timeout);
    }

    private static DateTimeOffset ParseUtc(string value, string name)
    {
        var hasOffset = value.EndsWith('Z')
            || (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':');
        if (!hasOffset || !DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            throw new ArgumentException($"{name} must be ISO-8601 with Z or an explicit offset.");
        return parsed;
    }
}
