using System.Globalization;
using System.Text;
using System.Text.Json;
using RobustPickBacktest;

Console.OutputEncoding = Encoding.UTF8;

try
{
    if (args.Any(argument => argument.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || argument.Equals("-h", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine(Cli.Help);
        return 0;
    }

    if (args.Any(argument => argument.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
    {
        BacktestSelfTest.Run();
        return 0;
    }

    var command = Cli.Parse(args);
    var (rows, sha256) = await BacktestInputLoader.LoadAsync(command.InputPath);
    var report = new WalkForwardBacktestEngine().Run(rows, command.Configuration, sha256);
    var json = JsonSerializer.Serialize(report, Cli.ReportJsonOptions) + "\n";
    if (string.IsNullOrWhiteSpace(command.OutputPath))
    {
        Console.Write(json);
    }
    else
    {
        var fullPath = Path.GetFullPath(command.OutputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(fullPath, json, new UTF8Encoding(false));
        Console.Error.WriteLine($"Report written to {fullPath}");
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ERROR: {exception.Message}");
    return 1;
}

internal sealed record CliCommand(
    string InputPath,
    string? OutputPath,
    BacktestConfiguration Configuration);

internal static class Cli
{
    public static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public const string Help = """
RobustPickBacktest

Usage:
  dotnet run --project tools/RobustPickBacktest -- --input evaluations.json [options]
  dotnet run --project tools/RobustPickBacktest -- --self-test

Required:
  --input PATH                       JSON, envelope { evaluations: [] }, or JSONL.

Options:
  --output PATH                      Write JSON report; stdout when omitted.
  --train-days N                     Rolling training window (default 90).
  --validation-days N                Validation window before untouched test (default 30).
  --test-days N                      Non-overlapping test window (default 30).
  --step-days N                      Walk step; must be >= test-days (default 30).
  --embargo-hours N                  Gap before each test cutoff (default 0).
  --outcome-lag-hours N              Fallback result availability lag (default 8).
  --min-train N                      Minimum temporally available training rows (default 30).
  --min-validation N                 Minimum available validation rows (default 15).
  --test-start-utc ISO-8601          Explicit first test boundary.
  --from-utc ISO-8601                Inclusive evaluation filter.
  --to-utc ISO-8601                  Exclusive evaluation filter.
  --latest-per-selection true|false  Keep latest pre-match snapshot (default true).
  --bootstrap-replicates N           Cluster-bootstrap repetitions (default 1000; 0 disables).
  --bootstrap-confidence N           Percentile confidence level (default 0.95).
  --bootstrap-cluster MODE           fixture, day, or hierarchical fixture-day.
  --odds-band-width N                Grouping width for odds (default 0.25).
  --line-band-width N                Grouping width for lines (default 0.50).
  --calibration-band-width N         Reliability grouping width (default 0.10).
  --grid true|false                  Enable validation-only threshold selection (default false).
  --grid-min-edge CSV                Candidate minimum robust edges.
  --grid-min-ev CSV                  Candidate minimum robust EVs.
  --grid-min-ev-stability CSV        Candidate minimum positive-EV stability.
  --grid-min-scenario-stability CSV  Candidate minimum scenario-side stability.
  --grid-min-worst-distance CSV      Candidate normalized worst-case distances.
  --grid-max-consensus-range CSV     Candidate maximum normalized consensus ranges.
  --grid-max-coherence-gap CSV       Candidate maximum normalized coherence gaps.
  --grid-min-calibration CSV         Candidate minimum calibration reliability.
  --grid-min-picks N                 Minimum approved train picks per policy (default 30).
  --grid-min-validation-picks N      Minimum approved validation picks (default 15).
  --grid-weight-pl N                 Validation P/L objective weight (default 1).
  --grid-weight-yield N              Validation yield weight (default 0.75).
  --grid-weight-drawdown N           Low-drawdown weight (default 1).
  --grid-weight-volume N             Validation volume weight (default 0.5).
  --grid-weight-calibration N        Calibration quality weight (default 0.75).
  --grid-weight-clv N                CLV weight (default 0.5).
  --grid-max-combinations N          Cartesian grid safety cap (default 10000).
""";

    public static CliCommand Parse(IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{argument}'.");
            }

            var equals = argument.IndexOf('=');
            string key;
            string value;
            if (equals > 2)
            {
                key = argument[..equals];
                value = argument[(equals + 1)..];
            }
            else
            {
                key = argument;
                if (++index >= arguments.Count)
                {
                    throw new ArgumentException($"Missing value for '{key}'.");
                }
                value = arguments[index];
            }
            if (!values.TryAdd(key, value))
            {
                throw new ArgumentException($"Duplicate option '{key}'.");
            }
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--input", "--output", "--train-days", "--validation-days", "--test-days", "--step-days",
            "--embargo-hours", "--outcome-lag-hours", "--min-train", "--test-start-utc",
            "--min-validation", "--from-utc", "--to-utc", "--latest-per-selection", "--bootstrap-replicates",
            "--bootstrap-confidence", "--bootstrap-cluster", "--grid", "--grid-min-edge",
            "--odds-band-width", "--line-band-width", "--calibration-band-width",
            "--grid-min-ev", "--grid-min-ev-stability", "--grid-min-scenario-stability",
            "--grid-min-worst-distance", "--grid-max-consensus-range",
            "--grid-max-coherence-gap", "--grid-min-calibration", "--grid-min-picks",
            "--grid-min-validation-picks", "--grid-weight-pl", "--grid-weight-yield",
            "--grid-weight-drawdown", "--grid-weight-volume", "--grid-weight-calibration",
            "--grid-weight-clv", "--grid-max-combinations"
        };
        var unknown = values.Keys.FirstOrDefault(key => !known.Contains(key));
        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown option '{unknown}'.");
        }
        if (!values.TryGetValue("--input", out var input) || string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("--input is required. Use --help for the schema and options.");
        }

        var configuration = new BacktestConfiguration
        {
            TrainingWindowDays = Decimal(values, "--train-days", 90m),
            ValidationWindowDays = Decimal(values, "--validation-days", 30m),
            TestWindowDays = Decimal(values, "--test-days", 30m),
            StepDays = Decimal(values, "--step-days", 30m),
            EmbargoHours = Decimal(values, "--embargo-hours", 0m),
            OutcomeAvailabilityLagHours = Decimal(values, "--outcome-lag-hours", 8m),
            MinimumTrainingObservations = Integer(values, "--min-train", 30),
            MinimumValidationObservations = Integer(values, "--min-validation", 15),
            FirstTestStartUtc = Timestamp(values, "--test-start-utc"),
            FromUtc = Timestamp(values, "--from-utc"),
            ToUtc = Timestamp(values, "--to-utc"),
            LatestEvaluationPerSelection = Boolean(values, "--latest-per-selection", true),
            Bootstrap = new ClusterBootstrapConfiguration
            {
                Replicates = Integer(values, "--bootstrap-replicates", 1000),
                ConfidenceLevel = Decimal(values, "--bootstrap-confidence", 0.95m),
                ClusterBy = Text(values, "--bootstrap-cluster", "Fixture")
            },
            Grouping = new GroupingConfiguration
            {
                OddsBandWidth = Decimal(values, "--odds-band-width", 0.25m),
                LineBandWidth = Decimal(values, "--line-band-width", 0.50m),
                CalibrationReliabilityBandWidth = Decimal(
                    values, "--calibration-band-width", 0.10m)
            },
            ThresholdGrid = new ThresholdGridConfiguration
            {
                Enabled = Boolean(values, "--grid", false),
                MinRobustEdge = DecimalList(values, "--grid-min-edge", [0.005m]),
                MinRobustExpectedValue = DecimalList(values, "--grid-min-ev", [0m]),
                MinPositiveEvStability = DecimalList(
                    values, "--grid-min-ev-stability", [0.75m]),
                MinScenarioSideStability = DecimalList(
                    values, "--grid-min-scenario-stability", [0.75m]),
                MinNormalizedWorstCaseDistance = DecimalList(
                    values, "--grid-min-worst-distance", [0.25m]),
                MaxNormalizedConsensusRange = DecimalList(
                    values, "--grid-max-consensus-range", [0.75m]),
                MaxNormalizedCoherenceGap = DecimalList(
                    values, "--grid-max-coherence-gap", [0.75m]),
                MinCalibrationReliability = DecimalList(
                    values, "--grid-min-calibration", [0.50m]),
                MinimumApprovedTrainingPicks = Integer(values, "--grid-min-picks", 30),
                MinimumApprovedValidationPicks = Integer(
                    values, "--grid-min-validation-picks", 15),
                ObjectiveWeights = new ThresholdObjectiveWeights
                {
                    ProfitLoss = Decimal(values, "--grid-weight-pl", 1m),
                    Yield = Decimal(values, "--grid-weight-yield", 0.75m),
                    Drawdown = Decimal(values, "--grid-weight-drawdown", 1m),
                    Volume = Decimal(values, "--grid-weight-volume", 0.50m),
                    Calibration = Decimal(values, "--grid-weight-calibration", 0.75m),
                    Clv = Decimal(values, "--grid-weight-clv", 0.50m)
                },
                MaximumGridCombinations = Integer(values, "--grid-max-combinations", 10_000)
            }
        };
        values.TryGetValue("--output", out var output);
        return new CliCommand(input, output, configuration);
    }

    private static decimal Decimal(
        IReadOnlyDictionary<string, string> values,
        string key,
        decimal fallback) => values.TryGetValue(key, out var value)
        && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : values.ContainsKey(key)
                ? throw new ArgumentException($"'{values[key]}' is not a decimal for {key}.")
                : fallback;

    private static int Integer(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback) => values.TryGetValue(key, out var value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : values.ContainsKey(key)
                ? throw new ArgumentException($"'{values[key]}' is not an integer for {key}.")
                : fallback;

    private static IReadOnlyList<decimal> DecimalList(
        IReadOnlyDictionary<string, string> values,
        string key,
        IReadOnlyList<decimal> fallback)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }
        var parsed = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => decimal.TryParse(
                    item,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var number)
                ? number
                : throw new ArgumentException($"'{item}' is not a decimal in {key}."))
            .Distinct()
            .OrderBy(item => item)
            .ToArray();
        if (parsed.Length == 0)
        {
            throw new ArgumentException($"{key} requires at least one decimal.");
        }
        return parsed;
    }

    private static string Text(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback) => values.TryGetValue(key, out var value) ? value : fallback;

    private static bool Boolean(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool fallback) => values.TryGetValue(key, out var value)
        && bool.TryParse(value, out var parsed)
            ? parsed
            : values.ContainsKey(key)
                ? throw new ArgumentException($"'{values[key]}' is not true or false for {key}.")
                : fallback;

    private static DateTimeOffset? Timestamp(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }
        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            throw new ArgumentException($"'{value}' is not an ISO-8601 timestamp for {key}.");
        }
        return parsed;
    }
}
