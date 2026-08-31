namespace CornersPrediction.Application.Automation.BotH;

/// <summary>
/// Read filters for the append-only Bot H shadow laboratory.  All timestamps are UTC
/// and <see cref="AsOfUtc"/> is also the latest outcome-availability timestamp the
/// dynamic settlement is allowed to observe.
/// </summary>
public sealed record BotHShadowEvaluationFilter(
    DateTime? PredictionFromUtc = null,
    DateTime? PredictionToUtc = null,
    DateTime? AsOfUtc = null,
    string? Decision = null,
    string? MarketType = null,
    string? Selection = null,
    string? ConfigurationVersion = null,
    string? SettlementState = null,
    int Page = 1,
    int PageSize = 100);

public sealed record BotHShadowScorecardFilter(
    DateTime? AsOfUtc = null,
    string? ConfigurationVersion = null);

/// <summary>
/// Replays stored, decision-time H evidence with alternate thresholds.  It is an
/// analysis filter only: it cannot rewrite a captured decision or publish a pick.
/// </summary>
public sealed record BotHThresholdAnalysisFilter(
    DateTime? AsOfUtc = null,
    string? ConfigurationVersion = null,
    string? MarketType = null,
    string? Selection = null,
    string AnalysisVersion = BotHShadowLab.ThresholdAnalysisVersion,
    decimal MinimumFinalProbability = 0.56m,
    decimal MinimumFinalEdge = 0.04m,
    decimal MinimumFinalExpectedValue = 0.03m,
    decimal MinimumDataQualityScore = 0.70m,
    decimal MinimumContextAgreementScore = 0.70m,
    decimal MinimumOdds = 1.60m,
    decimal MaximumOdds = 2.20m,
    decimal DevelopmentFraction = 0.70m);

public sealed record BotHShadowEvaluationPage(
    IReadOnlyList<BotHShadowEvaluationDto> Items,
    long TotalRows,
    int Page,
    int PageSize,
    DateTime AsOfUtc);

public sealed class BotHShadowEvaluationDto
{
    public long ShadowEvaluationId { get; init; }
    public long SourceEvaluationId { get; init; }
    public string CaptureKey { get; init; } = string.Empty;
    public Guid RunId { get; init; }
    public string BotKey { get; init; } = BotHShadowLab.BotKey;
    public string AutomationVersion { get; init; } = string.Empty;
    public long PartidoProximoCuotaId { get; init; }
    public long OddsSnapshotId { get; init; }
    public DateTime OddsCapturedAtUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public DateTime SourceMatchDate { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string SourceMarketType { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public decimal LineValue { get; init; }
    public string Selection { get; init; } = string.Empty;
    public decimal? OverOdds { get; init; }
    public decimal? UnderOdds { get; init; }
    public decimal SelectedOdds { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string DecisionEngineType { get; init; } = string.Empty;
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public string? BaseModelName { get; init; }
    public string? BaseModelVersion { get; init; }
    public DateTime? BaseModelTrainedThroughUtc { get; init; }
    public double? BaseRawProbability { get; init; }
    public double? BaseCalibratedProbability { get; init; }
    public double? RawImpliedProbability { get; init; }
    public double? MarketNoVigProbability { get; init; }
    public double? FinalProbability { get; init; }
    public double? FinalEdge { get; init; }
    public double? FinalExpectedValue { get; init; }
    public double? SelectionScore { get; init; }
    public double? ContextAgreementScore { get; init; }
    public double? DataQualityScore { get; init; }
    public decimal VirtualStakeUnits { get; init; }
    public string DecisionReasonsJson { get; init; } = "[]";
    public string RiskFlagsJson { get; init; } = "[]";
    public string Explanation { get; init; } = string.Empty;
    public string FeatureSnapshotJson { get; init; } = "{}";
    public string SnapshotLineageState { get; init; } = string.Empty;
    public int MatchCandidateCount { get; init; }
    public long? MatchHistoryId { get; init; }
    public string? MatchLinkMethod { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public int? ActualHomeCorners { get; init; }
    public int? ActualAwayCorners { get; init; }
    public int? ActualValue { get; init; }
    public string SettlementState { get; init; } = string.Empty;
    public decimal? SettlementFactor { get; init; }
    public string? Result { get; init; }
    public decimal? ProfitLoss { get; init; }
    public double? EconomicOutcome { get; init; }
    public DateTime CapturedAtUtc { get; init; }
    public long TotalRows { get; init; }
}

public sealed class BotHShadowScorecardDto
{
    public int WindowDays { get; init; }
    public DateTime DateFromUtc { get; init; }
    public DateTime DateToUtc { get; init; }
    public string Dimension { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public string? ConfigurationVersion { get; init; }
    public string? MarketType { get; init; }
    public string? Selection { get; init; }
    public long Evaluations { get; init; }
    public long FixturesEvaluated { get; init; }
    public long ApprovedSignals { get; init; }
    public long Approved { get; init; }
    public long Rejected { get; init; }
    public long SafelySettled { get; init; }
    public long UnsafeOrUnavailable { get; init; }
    public long Won { get; init; }
    public long HalfWon { get; init; }
    public long Pushes { get; init; }
    public long HalfLost { get; init; }
    public long Lost { get; init; }
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? AverageModelProbability { get; init; }
    public double? AverageMarketProbability { get; init; }
    public double? ObservedEconomicOutcome { get; init; }
    public double? CalibrationGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public double? AverageEdge { get; init; }
    public double? AverageExpectedValue { get; init; }
    public double? CoverageRate { get; init; }
    public bool Deployable { get; init; }
    public string PromotionState { get; init; } = "SHADOW_ONLY";
    public string UnitOfAnalysis { get; init; } = "FIRST_APPROVED_PER_FIXTURE_CONFIGURATION";
}

/// <summary>
/// One row per chronological split returned by the versioned H threshold replay.
/// Overall, Development and Holdout share the same immutable evidence boundary and
/// threshold metadata, making the result independently auditable.
/// </summary>
public sealed class BotHThresholdAnalysisDto
{
    public string AnalysisVersion { get; init; } = BotHShadowLab.ThresholdAnalysisVersion;
    public DateTime AsOfUtc { get; init; }
    public string? ConfigurationVersion { get; init; }
    public string? MarketType { get; init; }
    public string? Selection { get; init; }
    public decimal MinimumFinalProbability { get; init; }
    public decimal MinimumFinalEdge { get; init; }
    public decimal MinimumFinalExpectedValue { get; init; }
    public decimal MinimumDataQualityScore { get; init; }
    public decimal MinimumContextAgreementScore { get; init; }
    public decimal MinimumOdds { get; init; }
    public decimal MaximumOdds { get; init; }
    public decimal DevelopmentFraction { get; init; }
    public DateTime? SplitBoundaryUtc { get; init; }
    public string Split { get; init; } = string.Empty;
    public long AvailableSettledEvaluations { get; init; }
    public long EligibleEvaluations { get; init; }
    public long SelectedPicks { get; init; }
    public long Fixtures { get; init; }
    public long Won { get; init; }
    public long HalfWon { get; init; }
    public long Pushes { get; init; }
    public long HalfLost { get; init; }
    public long Lost { get; init; }
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? AverageOdds { get; init; }
    public double? AverageModelProbability { get; init; }
    public double? AverageMarketProbability { get; init; }
    public double? AverageEdge { get; init; }
    public double? AverageExpectedValue { get; init; }
    public double? ObservedEconomicOutcome { get; init; }
    public double? CalibrationGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public bool ReadOnly { get; init; } = true;
    public bool Deployable { get; init; }
    public string PromotionState { get; init; } = BotHShadowLab.PromotionState;
    public string UnitOfAnalysis { get; init; } = BotHShadowLab.ThresholdAnalysisUnitOfAnalysis;
}

public sealed class BotHShadowLabStatusDto
{
    public string BotKey { get; init; } = BotHShadowLab.BotKey;
    public bool SchemaReady { get; init; }
    public bool DefinitionExists { get; init; }
    public bool IsEnabled { get; init; }
    public bool PublishEnabled { get; init; }
    public bool ShadowOnly { get; init; }
    public bool CaptureTriggerEnabled { get; init; }
    public bool PublicationGuardsEnabled { get; init; }
    public long CapturedEvaluations { get; init; }
    public long UnsafePublicationRows { get; init; }
    public long UncapturedEligibleEvaluations { get; init; }
    public DateTime? FirstPredictionTimestampUtc { get; init; }
    public DateTime? LastPredictionTimestampUtc { get; init; }
    public string State { get; init; } = "NOT_READY";
}

public interface IBotHShadowLabReadRepository
{
    Task<BotHShadowLabStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    Task<BotHShadowEvaluationPage> GetEvaluationsAsync(
        BotHShadowEvaluationFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BotHShadowScorecardDto>> GetScorecardsAsync(
        BotHShadowScorecardFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BotHThresholdAnalysisDto>> GetThresholdAnalysisAsync(
        BotHThresholdAnalysisFilter filter,
        CancellationToken cancellationToken);
}

public static class BotHShadowLab
{
    public const string BotKey = "H2026";
    public const string PromotionState = "SHADOW_ONLY";
    public const string ScorecardUnitOfAnalysis = "FIRST_APPROVED_PER_FIXTURE_CONFIGURATION";
    public const string ThresholdAnalysisVersion = "bot-h-threshold-what-if-1.0.0";
    public const string ThresholdAnalysisUnitOfAnalysis = "FIRST_ELIGIBLE_PER_FIXTURE";
    public static readonly IReadOnlyList<int> ScorecardWindows = [7, 30, 90];

    public static DateTime NormalizeAsOfUtc(DateTime? value, DateTime utcNow)
    {
        var now = EnsureUtc(utcNow);
        var asOf = value.HasValue ? EnsureUtc(value.Value) : now;
        if (asOf > now.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(value), "AsOfUtc cannot be in the future.");
        return asOf;
    }

    public static void Validate(BotHShadowEvaluationFilter filter, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _ = NormalizeAsOfUtc(filter.AsOfUtc, utcNow);
        if (filter.PredictionFromUtc.HasValue && filter.PredictionToUtc.HasValue
            && EnsureUtc(filter.PredictionToUtc.Value) <= EnsureUtc(filter.PredictionFromUtc.Value))
            throw new ArgumentException("PredictionToUtc must be later than PredictionFromUtc.", nameof(filter));
        if (filter.Page is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(filter), "Page must be between 1 and 1,000,000.");
        if (filter.PageSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(filter), "PageSize must be between 1 and 1000.");
        ValidateToken(filter.Decision, nameof(filter.Decision), 20);
        ValidateToken(filter.MarketType, nameof(filter.MarketType), 50);
        ValidateToken(filter.Selection, nameof(filter.Selection), 10);
        ValidateToken(filter.ConfigurationVersion, nameof(filter.ConfigurationVersion), 80);
        ValidateToken(filter.SettlementState, nameof(filter.SettlementState), 40);
    }

    public static void Validate(BotHShadowScorecardFilter filter, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _ = NormalizeAsOfUtc(filter.AsOfUtc, utcNow);
        ValidateToken(filter.ConfigurationVersion, nameof(filter.ConfigurationVersion), 80);
    }

    public static void Validate(BotHThresholdAnalysisFilter filter, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _ = NormalizeAsOfUtc(filter.AsOfUtc, utcNow);
        ValidateToken(filter.ConfigurationVersion, nameof(filter.ConfigurationVersion), 80);
        ValidateToken(filter.MarketType, nameof(filter.MarketType), 50);
        ValidateToken(filter.Selection, nameof(filter.Selection), 10);
        if (filter.MarketType is not null
            && filter.MarketType is not ("TotalCorners" or "HomeTeamCorners" or "AwayTeamCorners"))
            throw new ArgumentException("MarketType is not supported by Bot H.", nameof(filter.MarketType));
        if (filter.Selection is not null && filter.Selection is not ("Over" or "Under"))
            throw new ArgumentException("Selection must be Over or Under.", nameof(filter.Selection));
        if (!string.Equals(filter.AnalysisVersion, ThresholdAnalysisVersion, StringComparison.Ordinal))
            throw new ArgumentException(
                $"AnalysisVersion must be {ThresholdAnalysisVersion}.",
                nameof(filter.AnalysisVersion));
        ValidateRange(filter.MinimumFinalProbability, 0m, 1m, nameof(filter.MinimumFinalProbability));
        ValidateRange(filter.MinimumFinalEdge, 0m, 1m, nameof(filter.MinimumFinalEdge));
        ValidateRange(filter.MinimumFinalExpectedValue, 0m, 10m, nameof(filter.MinimumFinalExpectedValue));
        ValidateRange(filter.MinimumDataQualityScore, 0m, 1m, nameof(filter.MinimumDataQualityScore));
        ValidateRange(filter.MinimumContextAgreementScore, 0m, 1m, nameof(filter.MinimumContextAgreementScore));
        ValidateRange(filter.MinimumOdds, 1.01m, 10m, nameof(filter.MinimumOdds));
        ValidateRange(filter.MaximumOdds, 1.01m, 10m, nameof(filter.MaximumOdds));
        if (filter.MaximumOdds < filter.MinimumOdds)
            throw new ArgumentException("MaximumOdds must be greater than or equal to MinimumOdds.", nameof(filter));
        ValidateRange(filter.DevelopmentFraction, 0.50m, 0.90m, nameof(filter.DevelopmentFraction));
    }

    public static BotHSettlementResult CalculateSettlement(
        int actualValue,
        decimal line,
        string selection,
        decimal odds,
        decimal stakeUnits = 1m)
    {
        if (actualValue < 0) throw new ArgumentOutOfRangeException(nameof(actualValue));
        if (line < 0m || line * 4m != decimal.Truncate(line * 4m))
            throw new ArgumentOutOfRangeException(nameof(line), "Line must be a non-negative Asian quarter line.");
        if (!selection.Equals("Over", StringComparison.Ordinal)
            && !selection.Equals("Under", StringComparison.Ordinal))
            throw new ArgumentException("Selection must be Over or Under.", nameof(selection));
        if (odds <= 1m) throw new ArgumentOutOfRangeException(nameof(odds));
        if (stakeUnits is <= 0m or > 10m) throw new ArgumentOutOfRangeException(nameof(stakeUnits));

        var fraction = line - decimal.Floor(line);
        var firstLine = fraction is 0.25m or 0.75m ? line - 0.25m : line;
        var secondLine = fraction is 0.25m or 0.75m ? line + 0.25m : line;
        var factor = (LegFactor(actualValue, firstLine, selection)
                      + LegFactor(actualValue, secondLine, selection)) / 2m;
        var result = factor switch
        {
            1m => "Win",
            0.5m => "HalfWin",
            0m => "Push",
            -0.5m => "HalfLoss",
            -1m => "Loss",
            _ => throw new InvalidOperationException("Unexpected Asian settlement factor.")
        };
        var profitLoss = factor switch
        {
            1m => stakeUnits * (odds - 1m),
            0.5m => stakeUnits * (odds - 1m) / 2m,
            0m => 0m,
            -0.5m => -stakeUnits / 2m,
            _ => -stakeUnits
        };
        var economicOutcome = ((profitLoss / stakeUnits) + 1m) / odds;
        return new BotHSettlementResult(result, factor, profitLoss, economicOutcome);
    }

    private static decimal LegFactor(int actualValue, decimal line, string selection) =>
        selection == "Over"
            ? actualValue > line ? 1m : actualValue == line ? 0m : -1m
            : actualValue < line ? 1m : actualValue == line ? 0m : -1m;

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void ValidateToken(string? value, string name, int maximumLength)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength))
            throw new ArgumentException($"{name} must be non-blank and at most {maximumLength} characters.", name);
    }

    private static void ValidateRange(decimal value, decimal minimum, decimal maximum, string name)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
    }
}

public sealed record BotHSettlementResult(
    string Result,
    decimal SettlementFactor,
    decimal ProfitLoss,
    decimal EconomicOutcome);
