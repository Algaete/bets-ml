using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPrediction.Application.Automation.BotI;

/// <summary>
/// I2026 is an isolated, append-only shadow experiment.  An Approved decision is
/// only an interesting market-movement observation; it is never a productive bet.
/// </summary>
public static class BotIShadowLab
{
    public const string BotKey = "I2026";
    public const string ConfigurationVersion = "bot-i-market-movement-shadow-1.0.0";
    public const string FeatureSchemaVersion = "bot-i-market-movement-features-1.0.0";
    public const string PromotionState = "SHADOW_ONLY";
    public static readonly IReadOnlyList<int> ScorecardWindows = [7, 30, 90];

    private static readonly TimeZoneInfo SantiagoTimeZone = ResolveSantiagoTimeZone();

    public static long FixtureIdentity(
        DateTime sourceMatchDate,
        string league,
        string homeTeam,
        string awayTeam)
    {
        var fixtureDateUtc = ToUtcFromSantiago(sourceMatchDate);
        var identity = string.Join("|",
            fixtureDateUtc.ToString("O"),
            NormalizeIdentityPart(league),
            NormalizeIdentityPart(homeTeam),
            NormalizeIdentityPart(awayTeam));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        // Explicit big-endian decoding keeps the canonical identity identical on
        // every runtime architecture.  BitConverter would make it host-endian.
        var value = BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long))) & long.MaxValue;
        return value == 0 ? 1 : value;
    }

    public static DateTime ToUtcFromSantiago(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc) return value;
        if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            SantiagoTimeZone);
    }

    public static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static string BuildIdempotencyKey(BotIShadowEvaluationDraft evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var value = string.Join("|",
            BotKey,
            evaluation.ConfigurationVersion,
            evaluation.FixtureIdentity.ToString(CultureInfo.InvariantCulture),
            evaluation.Source.Trim().ToUpperInvariant(),
            evaluation.MarketType,
            evaluation.CurrentSnapshotId.ToString(CultureInfo.InvariantCulture));
        // SQL Server hashes NVARCHAR as UTF-16LE. Matching that representation lets
        // the append procedure independently verify the key supplied by .NET.
        return Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(value)))
            .ToLowerInvariant();
    }

    public static void Validate(BotICollectCommand command, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = EnsureUtc(utcNow);
        var asOf = command.AsOfUtc.HasValue ? EnsureUtc(command.AsOfUtc.Value) : now;
        if (asOf > now.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(command), "AsOfUtc cannot be in the future.");
        if (command.DateTo <= command.DateFrom)
            throw new ArgumentException("DateTo must be later than DateFrom.", nameof(command));
        if (command.DateTo.DayNumber - command.DateFrom.DayNumber > 14)
            throw new ArgumentOutOfRangeException(nameof(command), "A collection window cannot exceed 14 fixture days.");
        if (command.MaximumFixtures is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(command), "MaximumFixtures must be between 1 and 1000.");
    }

    public static void Validate(BotIEvaluationFilter filter, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.PredictionFromUtc.HasValue && filter.PredictionToUtc.HasValue
            && EnsureUtc(filter.PredictionToUtc.Value) <= EnsureUtc(filter.PredictionFromUtc.Value))
            throw new ArgumentException("PredictionToUtc must be later than PredictionFromUtc.", nameof(filter));
        if (filter.Page is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(filter), "Page must be between 1 and 1,000,000.");
        if (filter.PageSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(filter), "PageSize must be between 1 and 1000.");
        ValidateAsOf(filter.AsOfUtc, utcNow);
        ValidateToken(filter.Decision, nameof(filter.Decision), 20);
        ValidateToken(filter.MarketType, nameof(filter.MarketType), 50);
        ValidateToken(filter.Selection, nameof(filter.Selection), 10);
        ValidateToken(filter.Source, nameof(filter.Source), 50);
        ValidateToken(filter.ConfigurationVersion, nameof(filter.ConfigurationVersion), 80);
    }

    public static DateTime ValidateAsOf(DateTime? asOfUtc, DateTime utcNow)
    {
        var now = EnsureUtc(utcNow);
        var asOf = asOfUtc.HasValue ? EnsureUtc(asOfUtc.Value) : now;
        if (asOf > now.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(asOfUtc), "AsOfUtc cannot be in the future.");
        return asOf;
    }

    private static string NormalizeIdentityPart(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private static void ValidateToken(string? value, string name, int maximumLength)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength))
            throw new ArgumentException($"{name} must be non-blank and at most {maximumLength} characters.", name);
    }

    private static TimeZoneInfo ResolveSantiagoTimeZone()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}

public enum BotIShadowDecision
{
    Approved,
    Rejected,
    Abstain
}

public sealed record BotIMarketMovementConfiguration
{
    public string ConfigurationVersion { get; init; } = BotIShadowLab.ConfigurationVersion;
    public string FeatureSchemaVersion { get; init; } = BotIShadowLab.FeatureSchemaVersion;
    public int MinimumSnapshots { get; init; } = 3;
    public int MinimumObservationMinutes { get; init; } = 30;
    public int MaximumOddsAgeMinutes { get; init; } = 120;
    public int MaximumPeerAgeMinutes { get; init; } = 45;
    public decimal MinimumProbabilityMovement { get; init; } = 0.015m;
    public decimal MinimumLineMovement { get; init; } = 0.50m;
    public decimal MinimumCompositeSignal { get; init; } = 0.018m;
    public decimal MaximumCrossBookContradiction { get; init; } = 0.025m;
    public decimal LineProbabilityEquivalentPerUnit { get; init; } = 0.035m;
    public decimal TemporalWeight { get; init; } = 0.75m;
    public decimal CrossBookWeight { get; init; } = 0.25m;
    public decimal MinimumOdds { get; init; } = 1.50m;
    public decimal MaximumOdds { get; init; } = 2.60m;
    public bool RequireCrossBookEvidence { get; init; }

    public static BotIMarketMovementConfiguration Validate(BotIMarketMovementConfiguration value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.ConfigurationVersion)
            || string.IsNullOrWhiteSpace(value.FeatureSchemaVersion))
            throw new ArgumentException("Bot I versions are required.", nameof(value));
        if (value.MinimumSnapshots is < 2 or > 1000
            || value.MinimumObservationMinutes is < 1 or > 10_080
            || value.MaximumOddsAgeMinutes is < 1 or > 10_080
            || value.MaximumPeerAgeMinutes is < 1 or > 10_080)
            throw new ArgumentException("Bot I temporal limits are invalid.", nameof(value));
        if (value.MinimumProbabilityMovement is <= 0m or > 0.50m
            || value.MinimumLineMovement is <= 0m or > 20m
            || value.MinimumCompositeSignal is <= 0m or > 1m
            || value.MaximumCrossBookContradiction is <= 0m or > 1m
            || value.LineProbabilityEquivalentPerUnit is <= 0m or > 1m)
            throw new ArgumentException("Bot I movement thresholds are invalid.", nameof(value));
        if (value.TemporalWeight is < 0m or > 1m
            || value.CrossBookWeight is < 0m or > 1m
            || Math.Abs(value.TemporalWeight + value.CrossBookWeight - 1m) > 0.000001m)
            throw new ArgumentException("Bot I signal weights must add to 1.0.", nameof(value));
        if (value.MinimumOdds <= 1m || value.MaximumOdds <= value.MinimumOdds)
            throw new ArgumentException("Bot I odds range is invalid.", nameof(value));
        return value;
    }
}

/// <summary>
/// A repository projection containing only information visible by the requested
/// decision cutoff. Opening/current are the most balanced half-line in their
/// capture batch; peer is the latest other-book snapshot at or before the cutoff.
/// </summary>
public sealed class BotIMarketTimelineCandidate
{
    public DateTime SourceMatchDate { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public string SourceMarketType { get; init; } = string.Empty;
    public long? ApiFootballFixtureId { get; init; }
    public long OpeningSnapshotId { get; init; }
    public DateTime OpeningCapturedAtUtc { get; init; }
    public decimal OpeningLine { get; init; }
    public decimal OpeningOverOdds { get; init; }
    public decimal OpeningUnderOdds { get; init; }
    public long CurrentSnapshotId { get; init; }
    public DateTime CurrentCapturedAtUtc { get; init; }
    public decimal CurrentLine { get; init; }
    public decimal CurrentOverOdds { get; init; }
    public decimal CurrentUnderOdds { get; init; }
    public int SnapshotCount { get; init; }
    public long? PeerSnapshotId { get; init; }
    public string? PeerSource { get; init; }
    public DateTime? PeerCapturedAtUtc { get; init; }
    public decimal? PeerLine { get; init; }
    public decimal? PeerOverOdds { get; init; }
    public decimal? PeerUnderOdds { get; init; }
}

public sealed record BotIMarketMovementInput(
    long FixtureIdentity,
    long? ApiFootballFixtureId,
    DateTime FixtureDateUtc,
    DateTime PredictionTimestampUtc,
    string League,
    string HomeTeam,
    string AwayTeam,
    string Source,
    string? SourceMatchId,
    string MarketType,
    long OpeningSnapshotId,
    DateTime OpeningCapturedAtUtc,
    decimal OpeningLine,
    decimal OpeningOverOdds,
    decimal OpeningUnderOdds,
    long CurrentSnapshotId,
    DateTime CurrentCapturedAtUtc,
    decimal CurrentLine,
    decimal CurrentOverOdds,
    decimal CurrentUnderOdds,
    int SnapshotCount,
    long? PeerSnapshotId = null,
    string? PeerSource = null,
    DateTime? PeerCapturedAtUtc = null,
    decimal? PeerLine = null,
    decimal? PeerOverOdds = null,
    decimal? PeerUnderOdds = null);

public sealed record BotIShadowEvaluationDraft
{
    public string BotKey { get; init; } = BotIShadowLab.BotKey;
    public string ConfigurationVersion { get; init; } = BotIShadowLab.ConfigurationVersion;
    public string FeatureSchemaVersion { get; init; } = BotIShadowLab.FeatureSchemaVersion;
    public long FixtureIdentity { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public string MarketType { get; init; } = string.Empty;
    public string Selection { get; init; } = string.Empty;
    public BotIShadowDecision Decision { get; init; }
    public decimal SignalScore { get; init; }
    public decimal SelectedOdds { get; init; }
    public long OpeningSnapshotId { get; init; }
    public long CurrentSnapshotId { get; init; }
    public long? PeerSnapshotId { get; init; }
    public DateTime OpeningCapturedAtUtc { get; init; }
    public DateTime CurrentCapturedAtUtc { get; init; }
    public DateTime? PeerCapturedAtUtc { get; init; }
    public decimal OpeningLine { get; init; }
    public decimal CurrentLine { get; init; }
    public decimal? PeerLine { get; init; }
    public decimal OpeningOverNoVigProbability { get; init; }
    public decimal CurrentOverNoVigProbability { get; init; }
    public decimal? PeerOverNoVigProbability { get; init; }
    public decimal SelectedProbabilityMovement { get; init; }
    public decimal SelectedLineMovement { get; init; }
    public decimal MovementVelocityPerHour { get; init; }
    public decimal ObservationHours { get; init; }
    public decimal OddsAgeMinutes { get; init; }
    public int SnapshotCount { get; init; }
    public string? PeerSource { get; init; }
    public decimal? PinnacleOverNoVigProbability { get; init; }
    public decimal? BetanoOverNoVigProbability { get; init; }
    public decimal? CrossBookProbabilityDispersion { get; init; }
    public decimal? CrossBookLineDispersion { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
    public IReadOnlyList<string> RiskFlags { get; init; } = [];
    public string Explanation { get; init; } = string.Empty;
    public string FeatureSnapshotJson { get; init; } = "{}";
}

public interface IBotIMarketMovementEvaluator
{
    BotIShadowEvaluationDraft Evaluate(
        BotIMarketMovementInput input,
        BotIMarketMovementConfiguration configuration);
}

public sealed class BotIMarketMovementEvaluator : IBotIMarketMovementEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public BotIShadowEvaluationDraft Evaluate(
        BotIMarketMovementInput input,
        BotIMarketMovementConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        configuration = BotIMarketMovementConfiguration.Validate(configuration);

        var reasons = new List<string>();
        var risks = new List<string>();
        var fixtureUtc = BotIShadowLab.EnsureUtc(input.FixtureDateUtc);
        var predictionUtc = BotIShadowLab.EnsureUtc(input.PredictionTimestampUtc);
        var openingUtc = BotIShadowLab.EnsureUtc(input.OpeningCapturedAtUtc);
        var currentUtc = BotIShadowLab.EnsureUtc(input.CurrentCapturedAtUtc);
        var peerUtc = input.PeerCapturedAtUtc.HasValue
            ? BotIShadowLab.EnsureUtc(input.PeerCapturedAtUtc.Value)
            : (DateTime?)null;

        var supportedMarket = input.MarketType is "TotalGoals" or "TotalCorners";
        var halfLines = IsHalfLine(input.OpeningLine) && IsHalfLine(input.CurrentLine);
        var bilateral = ValidOdds(input.OpeningOverOdds, input.OpeningUnderOdds)
            && ValidOdds(input.CurrentOverOdds, input.CurrentUnderOdds);
        var temporal = openingUtc <= currentUtc
            && currentUtc <= predictionUtc
            && predictionUtc < fixtureUtc
            && (!peerUtc.HasValue || peerUtc.Value <= predictionUtc);

        var openingOver = bilateral ? NoVigOver(input.OpeningOverOdds, input.OpeningUnderOdds) : 0.5m;
        var currentOver = bilateral ? NoVigOver(input.CurrentOverOdds, input.CurrentUnderOdds) : 0.5m;
        var observationHours = Math.Max(0m, Convert.ToDecimal((currentUtc - openingUtc).TotalHours));
        var oddsAgeMinutes = Math.Max(0m, Convert.ToDecimal((predictionUtc - currentUtc).TotalMinutes));
        var probabilityMovementOver = currentOver - openingOver;
        var lineMovementOver = input.CurrentLine - input.OpeningLine;
        var temporalOverSignal = probabilityMovementOver
            + lineMovementOver * configuration.LineProbabilityEquivalentPerUnit;

        var peerUsable = input.PeerSnapshotId is > 0
            && peerUtc.HasValue
            && input.PeerLine.HasValue
            && input.PeerOverOdds.HasValue
            && input.PeerUnderOdds.HasValue
            && ValidOdds(input.PeerOverOdds.Value, input.PeerUnderOdds.Value)
            && (predictionUtc - peerUtc.Value).TotalMinutes <= configuration.MaximumPeerAgeMinutes;
        decimal? peerOver = peerUsable
            ? NoVigOver(input.PeerOverOdds!.Value, input.PeerUnderOdds!.Value)
            : null;
        decimal? pinnacleOver = null;
        decimal? betanoOver = null;
        decimal? crossProbability = null;
        decimal? crossLine = null;
        decimal crossOverSignal = 0m;
        if (peerUsable)
        {
            if (IsSource(input.Source, "Pinnacle"))
            {
                pinnacleOver = currentOver;
                if (IsSource(input.PeerSource, "Betano")) betanoOver = peerOver;
            }
            else if (IsSource(input.Source, "Betano"))
            {
                betanoOver = currentOver;
                if (IsSource(input.PeerSource, "Pinnacle")) pinnacleOver = peerOver;
            }

            if (pinnacleOver.HasValue && betanoOver.HasValue)
            {
                crossLine = IsSource(input.Source, "Pinnacle")
                    ? input.CurrentLine - input.PeerLine!.Value
                    : input.PeerLine!.Value - input.CurrentLine;
                if (input.CurrentLine == input.PeerLine.Value)
                    crossProbability = pinnacleOver.Value - betanoOver.Value;
                crossOverSignal = crossProbability.GetValueOrDefault()
                    + crossLine.GetValueOrDefault() * configuration.LineProbabilityEquivalentPerUnit;
            }
            else
            {
                risks.Add("UNSUPPORTED_PEER_SOURCE");
            }
        }
        else
        {
            risks.Add("CROSS_BOOK_EVIDENCE_UNAVAILABLE");
        }

        var compositeOverSignal = peerUsable
            ? configuration.TemporalWeight * temporalOverSignal
                + configuration.CrossBookWeight * crossOverSignal
            : temporalOverSignal;
        var selection = compositeOverSignal >= 0m ? "Over" : "Under";
        var side = selection == "Over" ? 1m : -1m;
        var selectedProbabilityMovement = side * probabilityMovementOver;
        var selectedLineMovement = side * lineMovementOver;
        var selectedCrossBookSupport = side * crossOverSignal;
        var score = Math.Abs(compositeOverSignal);
        var selectedOdds = selection == "Over" ? input.CurrentOverOdds : input.CurrentUnderOdds;
        var velocity = observationHours > 0m
            ? selectedProbabilityMovement / observationHours
            : 0m;

        BotIShadowDecision decision;
        if (!supportedMarket)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("UNSUPPORTED_MARKET");
        }
        else if (!halfLines)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("NON_HALF_LINE");
        }
        else if (!bilateral)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("BILATERAL_ODDS_UNAVAILABLE");
        }
        else if (!temporal)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("TEMPORAL_EVIDENCE_INVALID");
        }
        else if (input.SnapshotCount < configuration.MinimumSnapshots)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("SNAPSHOT_SAMPLE_TOO_SMALL");
        }
        else if (observationHours * 60m < configuration.MinimumObservationMinutes)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("OBSERVATION_WINDOW_TOO_SHORT");
        }
        else if (oddsAgeMinutes > configuration.MaximumOddsAgeMinutes)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("CURRENT_ODDS_STALE");
        }
        else if (configuration.RequireCrossBookEvidence && !peerUsable)
        {
            decision = BotIShadowDecision.Abstain;
            reasons.Add("CROSS_BOOK_EVIDENCE_REQUIRED");
        }
        else if (selectedOdds < configuration.MinimumOdds || selectedOdds > configuration.MaximumOdds)
        {
            decision = BotIShadowDecision.Rejected;
            reasons.Add("SELECTED_ODDS_OUTSIDE_RANGE");
        }
        else if (Math.Abs(probabilityMovementOver) < configuration.MinimumProbabilityMovement
            && Math.Abs(lineMovementOver) < configuration.MinimumLineMovement)
        {
            decision = BotIShadowDecision.Rejected;
            reasons.Add("MOVEMENT_BELOW_MINIMUM");
        }
        else if (peerUsable && selectedCrossBookSupport < -configuration.MaximumCrossBookContradiction)
        {
            decision = BotIShadowDecision.Rejected;
            reasons.Add("CROSS_BOOK_CONTRADICTION");
        }
        else if (score < configuration.MinimumCompositeSignal)
        {
            decision = BotIShadowDecision.Rejected;
            reasons.Add("COMPOSITE_SIGNAL_BELOW_MINIMUM");
        }
        else
        {
            decision = BotIShadowDecision.Approved;
            reasons.Add("SHADOW_MOVEMENT_SIGNAL_APPROVED");
        }

        if (input.OpeningSnapshotId == input.CurrentSnapshotId)
            risks.Add("OPENING_EQUALS_CURRENT");
        if (peerUsable && crossProbability is null)
            risks.Add("CROSS_BOOK_LINES_DIFFER");

        var explanation = decision switch
        {
            BotIShadowDecision.Approved =>
                $"Shadow {selection}: movimiento compuesto {score:P2}; no crea una apuesta.",
            BotIShadowDecision.Rejected =>
                $"Shadow rechazado: {string.Join(", ", reasons)}.",
            _ => $"Shadow se abstuvo: {string.Join(", ", reasons)}."
        };

        var snapshot = JsonSerializer.Serialize(new
        {
            botKey = BotIShadowLab.BotKey,
            configuration.ConfigurationVersion,
            configuration.FeatureSchemaVersion,
            shadowOnly = true,
            publicationBlocked = true,
            fixture = new
            {
                input.FixtureIdentity,
                input.ApiFootballFixtureId,
                fixtureDateUtc = fixtureUtc,
                input.League,
                input.HomeTeam,
                input.AwayTeam
            },
            cutoff = new
            {
                predictionTimestampUtc = predictionUtc,
                strictPointInTime = true,
                noOutcomeDataRead = true
            },
            market = new
            {
                input.Source,
                input.SourceMatchId,
                input.MarketType,
                selection,
                selectedOdds,
                opening = new
                {
                    input.OpeningSnapshotId,
                    capturedAtUtc = openingUtc,
                    line = input.OpeningLine,
                    overOdds = input.OpeningOverOdds,
                    underOdds = input.OpeningUnderOdds,
                    overNoVigProbability = openingOver
                },
                current = new
                {
                    input.CurrentSnapshotId,
                    capturedAtUtc = currentUtc,
                    line = input.CurrentLine,
                    overOdds = input.CurrentOverOdds,
                    underOdds = input.CurrentUnderOdds,
                    overNoVigProbability = currentOver,
                    oddsAgeMinutes
                },
                peer = peerUsable ? new
                {
                    input.PeerSnapshotId,
                    input.PeerSource,
                    capturedAtUtc = peerUtc,
                    line = input.PeerLine,
                    overOdds = input.PeerOverOdds,
                    underOdds = input.PeerUnderOdds,
                    overNoVigProbability = peerOver
                } : null
            },
            features = new
            {
                input.SnapshotCount,
                observationHours,
                probabilityMovementOver,
                lineMovementOver,
                temporalOverSignal,
                velocitySelectedProbabilityPerHour = velocity,
                pinnacleOver,
                betanoOver,
                crossBookProbabilityDispersion = crossProbability,
                crossBookLineDispersion = crossLine,
                crossOverSignal,
                compositeOverSignal,
                signalScore = score
            },
            decision,
            reasons,
            risks,
            configuration
        }, JsonOptions);

        return new BotIShadowEvaluationDraft
        {
            ConfigurationVersion = configuration.ConfigurationVersion,
            FeatureSchemaVersion = configuration.FeatureSchemaVersion,
            FixtureIdentity = input.FixtureIdentity,
            ApiFootballFixtureId = input.ApiFootballFixtureId,
            FixtureDateUtc = fixtureUtc,
            PredictionTimestampUtc = predictionUtc,
            League = input.League,
            HomeTeam = input.HomeTeam,
            AwayTeam = input.AwayTeam,
            Source = input.Source,
            SourceMatchId = input.SourceMatchId,
            MarketType = input.MarketType,
            Selection = selection,
            Decision = decision,
            SignalScore = score,
            SelectedOdds = selectedOdds,
            OpeningSnapshotId = input.OpeningSnapshotId,
            CurrentSnapshotId = input.CurrentSnapshotId,
            PeerSnapshotId = peerUsable ? input.PeerSnapshotId : null,
            OpeningCapturedAtUtc = openingUtc,
            CurrentCapturedAtUtc = currentUtc,
            PeerCapturedAtUtc = peerUsable ? peerUtc : null,
            OpeningLine = input.OpeningLine,
            CurrentLine = input.CurrentLine,
            PeerLine = peerUsable ? input.PeerLine : null,
            OpeningOverNoVigProbability = openingOver,
            CurrentOverNoVigProbability = currentOver,
            PeerOverNoVigProbability = peerOver,
            SelectedProbabilityMovement = selectedProbabilityMovement,
            SelectedLineMovement = selectedLineMovement,
            MovementVelocityPerHour = velocity,
            ObservationHours = observationHours,
            OddsAgeMinutes = oddsAgeMinutes,
            SnapshotCount = input.SnapshotCount,
            PeerSource = peerUsable ? input.PeerSource : null,
            PinnacleOverNoVigProbability = pinnacleOver,
            BetanoOverNoVigProbability = betanoOver,
            CrossBookProbabilityDispersion = crossProbability,
            CrossBookLineDispersion = crossLine,
            ReasonCodes = reasons,
            RiskFlags = risks,
            Explanation = explanation,
            FeatureSnapshotJson = snapshot
        };
    }

    public static decimal NoVigOver(decimal overOdds, decimal underOdds)
    {
        if (!ValidOdds(overOdds, underOdds))
            throw new ArgumentOutOfRangeException(nameof(overOdds), "Both decimal odds must be greater than one.");
        var over = 1m / overOdds;
        var under = 1m / underOdds;
        return over / (over + under);
    }

    private static bool ValidOdds(decimal overOdds, decimal underOdds) =>
        overOdds > 1m && underOdds > 1m;

    private static bool IsHalfLine(decimal line) =>
        line >= 0m && line * 2m == decimal.Truncate(line * 2m)
        && line != decimal.Truncate(line);

    private static bool IsSource(string? actual, string expected) =>
        string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}

public sealed record BotICollectCommand(
    DateOnly DateFrom,
    DateOnly DateTo,
    DateTime? AsOfUtc = null,
    int MaximumFixtures = 50);

public sealed record BotICollectResult(
    int TimelinesLoaded,
    int Inserted,
    int AlreadyCaptured,
    int Approved,
    int Rejected,
    int Abstained,
    DateTime AsOfUtc,
    bool ShadowOnly = true,
    bool PublicationBlocked = true);

public sealed record BotIEvaluationFilter(
    DateTime? PredictionFromUtc = null,
    DateTime? PredictionToUtc = null,
    DateTime? AsOfUtc = null,
    string? Decision = null,
    string? MarketType = null,
    string? Selection = null,
    string? Source = null,
    string? ConfigurationVersion = null,
    int Page = 1,
    int PageSize = 100);

public sealed record BotIEvaluationPage(
    IReadOnlyList<BotIShadowEvaluationDto> Items,
    long TotalRows,
    int Page,
    int PageSize,
    DateTime AsOfUtc);

public sealed class BotIShadowEvaluationDto
{
    public long ShadowEvaluationId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string BotKey { get; init; } = BotIShadowLab.BotKey;
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public long FixtureIdentity { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public string MarketType { get; init; } = string.Empty;
    public string Selection { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public decimal SignalScore { get; init; }
    public decimal SelectedOdds { get; init; }
    public long OpeningSnapshotId { get; init; }
    public long CurrentSnapshotId { get; init; }
    public long? PeerSnapshotId { get; init; }
    public DateTime OpeningCapturedAtUtc { get; init; }
    public DateTime CurrentCapturedAtUtc { get; init; }
    public DateTime? PeerCapturedAtUtc { get; init; }
    public decimal OpeningLine { get; init; }
    public decimal CurrentLine { get; init; }
    public decimal? PeerLine { get; init; }
    public decimal OpeningOverNoVigProbability { get; init; }
    public decimal CurrentOverNoVigProbability { get; init; }
    public decimal? PeerOverNoVigProbability { get; init; }
    public decimal SelectedProbabilityMovement { get; init; }
    public decimal SelectedLineMovement { get; init; }
    public decimal MovementVelocityPerHour { get; init; }
    public decimal ObservationHours { get; init; }
    public decimal OddsAgeMinutes { get; init; }
    public int SnapshotCount { get; init; }
    public string? PeerSource { get; init; }
    public decimal? PinnacleOverNoVigProbability { get; init; }
    public decimal? BetanoOverNoVigProbability { get; init; }
    public decimal? CrossBookProbabilityDispersion { get; init; }
    public decimal? CrossBookLineDispersion { get; init; }
    public string ReasonCodesJson { get; init; } = "[]";
    public string RiskFlagsJson { get; init; } = "[]";
    public string Explanation { get; init; } = string.Empty;
    public string FeatureSnapshotJson { get; init; } = "{}";
    public bool ShadowOnly { get; init; }
    public bool PublicationBlocked { get; init; }
    public long MatchCandidateCount { get; init; }
    public long? MatchHistoryId { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public int? ActualValue { get; init; }
    public string SettlementState { get; init; } = string.Empty;
    public decimal? SettlementFactor { get; init; }
    public string? Result { get; init; }
    public decimal? ProfitLoss { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public long TotalRows { get; init; }
}

public sealed class BotIShadowStatusDto
{
    public string BotKey { get; init; } = BotIShadowLab.BotKey;
    public string ConfigurationVersion { get; init; } = BotIShadowLab.ConfigurationVersion;
    public string FeatureSchemaVersion { get; init; } = BotIShadowLab.FeatureSchemaVersion;
    public bool SchemaReady { get; init; }
    public bool ShadowOnly { get; init; } = true;
    public bool PublicationBlocked { get; init; } = true;
    public long Evaluations { get; init; }
    public long Approved { get; init; }
    public long Rejected { get; init; }
    public long Abstained { get; init; }
    public long UnsafeRows { get; init; }
    public DateTime? FirstPredictionTimestampUtc { get; init; }
    public DateTime? LastPredictionTimestampUtc { get; init; }
    public string State { get; init; } = "SHADOW_ONLY";
}

public sealed class BotIShadowScorecardDto
{
    public int WindowDays { get; init; }
    public DateTime DateFromUtc { get; init; }
    public DateTime DateToUtc { get; init; }
    public string Dimension { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public long Evaluations { get; init; }
    public long FixturesEvaluated { get; init; }
    public long Approved { get; init; }
    public long Rejected { get; init; }
    public long Abstained { get; init; }
    public long Settled { get; init; }
    public long Won { get; init; }
    public long HalfWon { get; init; }
    public long Pushes { get; init; }
    public long HalfLost { get; init; }
    public long Lost { get; init; }
    public double? ApprovalRate { get; init; }
    public double? CrossBookCoverageRate { get; init; }
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? AverageSignalScore { get; init; }
    public double? AverageAbsoluteProbabilityMovement { get; init; }
    public double? AverageAbsoluteLineMovement { get; init; }
    public double? AverageOddsAgeMinutes { get; init; }
    public double? AverageObservationHours { get; init; }
    public bool Deployable { get; init; }
    public string PromotionState { get; init; } = BotIShadowLab.PromotionState;
    public string ScorecardType { get; init; } = "OUTCOME_AWARE_SHADOW_OFFICIAL_FIXTURE_ONLY";
}

public interface IBotIShadowRepository
{
    Task<IReadOnlyList<BotIMarketTimelineCandidate>> GetTimelinesAsync(
        BotICollectCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<long>> GetCapturedCurrentSnapshotIdsAsync(
        IReadOnlyCollection<long> currentSnapshotIds,
        CancellationToken cancellationToken);

    Task<bool> AppendAsync(
        BotIShadowEvaluationDraft evaluation,
        CancellationToken cancellationToken);

    Task<BotIShadowStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    Task<BotIEvaluationPage> GetEvaluationsAsync(
        BotIEvaluationFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BotIShadowScorecardDto>> GetScorecardsAsync(
        DateTime? asOfUtc,
        string? configurationVersion,
        CancellationToken cancellationToken);
}

public interface IBotIShadowCollectorService
{
    Task<BotICollectResult> CollectAsync(
        BotICollectCommand command,
        CancellationToken cancellationToken);
}

public sealed class BotIShadowCollectorService : IBotIShadowCollectorService
{
    private readonly IBotIShadowRepository _repository;
    private readonly IBotIMarketMovementEvaluator _evaluator;
    private readonly BotIMarketMovementConfiguration _configuration;

    public BotIShadowCollectorService(
        IBotIShadowRepository repository,
        IBotIMarketMovementEvaluator evaluator,
        BotIMarketMovementConfiguration configuration)
    {
        _repository = repository;
        _evaluator = evaluator;
        _configuration = BotIMarketMovementConfiguration.Validate(configuration);
    }

    public async Task<BotICollectResult> CollectAsync(
        BotICollectCommand command,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        BotIShadowLab.Validate(command, utcNow);
        var asOfUtc = command.AsOfUtc.HasValue
            ? BotIShadowLab.EnsureUtc(command.AsOfUtc.Value)
            : utcNow;
        var timelines = await _repository.GetTimelinesAsync(
            command with { AsOfUtc = asOfUtc },
            cancellationToken);
        var capturedSnapshotIds = await _repository.GetCapturedCurrentSnapshotIdsAsync(
            timelines.Select(row => row.CurrentSnapshotId).Distinct().ToArray(),
            cancellationToken);
        var inserted = 0;
        var existing = 0;
        var approved = 0;
        var rejected = 0;
        var abstained = 0;

        foreach (var row in timelines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (capturedSnapshotIds.Contains(row.CurrentSnapshotId))
            {
                existing++;
                continue;
            }
            var fixtureDateUtc = BotIShadowLab.ToUtcFromSantiago(row.SourceMatchDate);
            var fixtureIdentity = BotIShadowLab.FixtureIdentity(
                row.SourceMatchDate,
                row.League,
                row.HomeTeam,
                row.AwayTeam);
            var marketType = row.SourceMarketType switch
            {
                "GoalsTotal" => "TotalGoals",
                "CornersTotal" => "TotalCorners",
                _ => row.SourceMarketType
            };
            var evaluation = _evaluator.Evaluate(new BotIMarketMovementInput(
                fixtureIdentity,
                row.ApiFootballFixtureId,
                fixtureDateUtc,
                asOfUtc,
                row.League,
                row.HomeTeam,
                row.AwayTeam,
                row.Source,
                row.SourceMatchId,
                marketType,
                row.OpeningSnapshotId,
                row.OpeningCapturedAtUtc,
                row.OpeningLine,
                row.OpeningOverOdds,
                row.OpeningUnderOdds,
                row.CurrentSnapshotId,
                row.CurrentCapturedAtUtc,
                row.CurrentLine,
                row.CurrentOverOdds,
                row.CurrentUnderOdds,
                row.SnapshotCount,
                row.PeerSnapshotId,
                row.PeerSource,
                row.PeerCapturedAtUtc,
                row.PeerLine,
                row.PeerOverOdds,
                row.PeerUnderOdds), _configuration);

            switch (evaluation.Decision)
            {
                case BotIShadowDecision.Approved: approved++; break;
                case BotIShadowDecision.Rejected: rejected++; break;
                default: abstained++; break;
            }

            if (await _repository.AppendAsync(evaluation, cancellationToken)) inserted++;
            else existing++;
        }

        return new BotICollectResult(
            timelines.Count,
            inserted,
            existing,
            approved,
            rejected,
            abstained,
            asOfUtc);
    }
}
