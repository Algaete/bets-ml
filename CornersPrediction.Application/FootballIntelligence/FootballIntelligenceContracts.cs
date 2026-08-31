using CornersPrediction.Domain.FootballIntelligence;

namespace CornersPrediction.Application.FootballIntelligence;

public sealed record IntelligenceTeamDto(int TeamId, string Name, string? Country = null);

public sealed record IntelligenceFixtureDto(
    long FixtureId,
    DateTime KickoffUtc,
    string Status,
    string League,
    int LeagueId,
    int Season,
    IntelligenceTeamDto Home,
    IntelligenceTeamDto Away);

public sealed record InjuryDto(
    long FixtureId,
    int TeamId,
    int PlayerId,
    string PlayerName,
    string? Type,
    string? Reason);

public sealed record SquadPlayerDto(
    int TeamId,
    int PlayerId,
    string Name,
    string? Position,
    int? Age,
    string? PhotoUrl);

public sealed record LineupPlayerDto(
    int PlayerId,
    string Name,
    string? Position,
    bool IsStarter,
    int? GridPosition);

public sealed record FixtureLineupDto(
    long FixtureId,
    int TeamId,
    string? Formation,
    IReadOnlyCollection<LineupPlayerDto> Players);

public interface IStructuredFootballDataProvider
{
    Task<IntelligenceFixtureDto?> GetFixtureAsync(long fixtureId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<InjuryDto>> GetFixtureInjuriesAsync(long fixtureId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SquadPlayerDto>> GetSquadAsync(int teamId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FixtureLineupDto>> GetFixtureLineupsAsync(long fixtureId, CancellationToken cancellationToken);
}

public sealed record NewsSearchRequest(
    long FixtureId,
    int TeamId,
    string TeamName,
    string OpponentName,
    string Query,
    DateTime CutoffAtUtc,
    string? LanguageCode = null,
    int MaximumResults = 10);

public sealed record NewsSearchResult(
    Uri Url,
    string Title,
    string? Description,
    string? SourceDomain,
    DateTime? PublishedAtUtc,
    string? LanguageCode);

public interface INewsSearchProvider
{
    Task<IReadOnlyCollection<NewsSearchResult>> SearchAsync(
        NewsSearchRequest request,
        CancellationToken cancellationToken);
}

public interface INewsQueryBuilder
{
    IReadOnlyCollection<string> Build(
        string teamName,
        string opponentName,
        IReadOnlyCollection<string>? aliases = null,
        IReadOnlyCollection<string>? languages = null);
}

public sealed record ExtractedArticle(
    Uri Url,
    Uri? CanonicalUrl,
    string Domain,
    string Title,
    string? Author,
    DateTime? PublishedAtUtc,
    DateTime? UpdatedAtUtc,
    string? Language,
    string NormalizedText,
    string ContentHash,
    int HttpStatusCode);

public interface IArticleContentExtractor
{
    Task<ExtractedArticle?> ExtractAsync(Uri uri, CancellationToken cancellationToken);
}

public interface IArticleDeduplicator
{
    IReadOnlyCollection<ExtractedArticle> Deduplicate(IReadOnlyCollection<ExtractedArticle> articles);
}

public interface IRelevantTextSelector
{
    string Select(
        string normalizedText,
        string teamName,
        string opponentName,
        IReadOnlyCollection<string> playerNames,
        int maximumCharacters);
}

public sealed record ExtractedNewsFact(
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

public sealed record TeamNewsSignals(
    decimal RotationRisk,
    decimal FatigueRisk,
    decimal MoraleSignal,
    bool CoachChangeDetected,
    bool TacticalChangeExpected,
    bool FormationChangeExpected);

public sealed record NewsExtractionResult(
    decimal FixtureRelevance,
    IReadOnlyCollection<ExtractedNewsFact> Facts,
    TeamNewsSignals TeamSignals,
    string ExtractionModel,
    string PromptVersion,
    int InputTokens = 0,
    int OutputTokens = 0);

public sealed record NewsExtractionRequest(
    long FixtureId,
    DateTime KickoffUtc,
    DateTime CutoffAtUtc,
    string TeamName,
    string OpponentName,
    string ArticleTitle,
    DateTime? PublishedAtUtc,
    string ArticleText,
    string? LanguageCode,
    IReadOnlyCollection<string>? KnownPlayerNames = null);

public interface INewsFactExtractor
{
    Task<NewsExtractionResult> ExtractAsync(
        NewsExtractionRequest request,
        CancellationToken cancellationToken);
}

public interface ILlmFactExtractionClient
{
    Task<NewsExtractionResult> ExtractStructuredAsync(
        NewsExtractionRequest request,
        CancellationToken cancellationToken);
}

public sealed record EntityResolutionResult(
    EntityResolutionStatus Status,
    int? TeamId,
    int? PlayerId,
    decimal Confidence,
    string? MatchedName);

public interface IEntityResolver
{
    Task<EntityResolutionResult> ResolveAsync(
        long fixtureId,
        string teamName,
        string? playerName,
        CancellationToken cancellationToken);
}

public interface INewsFactConsolidator
{
    IReadOnlyCollection<FootballNewsFact> Consolidate(
        IReadOnlyCollection<FootballNewsFact> facts,
        DateTime cutoffAtUtc);
}

public sealed record PlayerMarketImportance(
    int PlayerId,
    int TeamId,
    string MarketType,
    decimal StartRate,
    decimal MinutesShare,
    decimal RecentMinutesShare,
    decimal MarketContribution,
    decimal SetPieceShare,
    decimal Importance,
    int SampleSize);

public interface IPlayerImportanceService
{
    Task<PlayerMarketImportance?> CalculateAsync(
        int playerId,
        int teamId,
        string marketType,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken);
}

public sealed record PlayerAvailabilityImpact(
    int PlayerId,
    string MarketType,
    decimal Importance,
    decimal ProbabilityAvailable,
    decimal EffectiveConfidence,
    decimal ReplacementGap,
    decimal AbsenceImpact);

public interface IPlayerImpactCalculator
{
    PlayerAvailabilityImpact Calculate(
        PlayerMarketImportance importance,
        decimal probabilityAvailable,
        decimal effectiveConfidence,
        decimal replacementGap);
}

public interface ITeamFatigueCalculator
{
    decimal Calculate(FatigueInput input);
}

public sealed record FatigueInput(
    int RestDays,
    int GamesLast7Days,
    int GamesLast14Days,
    int MinutesPlayedLast7Days,
    decimal TravelDistanceKm,
    bool InternationalFixture,
    bool ExtraTimeRecently);

public interface IRotationRiskCalculator
{
    decimal Calculate(RotationRiskInput input);
}

public sealed record RotationRiskInput(
    int RestDays,
    int StartersWithRecentNinetyMinutes,
    bool ImportantNextFixture,
    bool InternationalFixture,
    bool KnockoutFixture,
    decimal OpponentStrengthGap,
    decimal NewsRotationSignal);

public sealed record MatchIntelligenceSnapshotPair(
    long FixtureId,
    MatchTeamIntelligenceSnapshot? Home,
    MatchTeamIntelligenceSnapshot? Away);

public interface INewsDocumentRepository
{
    Task<long> UpsertAsync(FootballNewsDocument document, CancellationToken cancellationToken);
    Task<IReadOnlyList<FootballNewsDocument>> GetByFixtureAsync(
        long fixtureId,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken);
}

public interface INewsFactRepository
{
    Task<IReadOnlyList<long>> InsertAsync(
        IReadOnlyCollection<FootballNewsFact> facts,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<FootballNewsFact>> GetByFixtureAsync(
        long fixtureId,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken);
}

public interface IIntelligenceSnapshotRepository
{
    Task<long> UpsertAsync(MatchTeamIntelligenceSnapshot snapshot, CancellationToken cancellationToken);
    Task<MatchIntelligenceSnapshotPair?> GetLatestPairAsync(
        long fixtureId,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<long, MatchIntelligenceSnapshotPair>> GetLatestPairsAsync(
        IReadOnlyCollection<long> fixtureIds,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MatchTeamIntelligenceSnapshot>> GetHistoryAsync(
        long fixtureId,
        CancellationToken cancellationToken);
}

public sealed record FootballSourceConfiguration(
    string Domain,
    NewsSourceTier Tier,
    decimal ConfidenceWeight,
    bool IsEnabled,
    DateTime UpdatedAtUtc);

public interface IFootballSourceRepository
{
    Task<FootballSourceConfiguration?> GetAsync(string domain, CancellationToken cancellationToken);
    Task<IReadOnlyList<FootballSourceConfiguration>> GetAllAsync(CancellationToken cancellationToken);
}

public interface ITeamAliasRepository
{
    Task<IReadOnlyList<string>> GetAliasesAsync(int teamId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> FindTeamIdsAsync(string normalizedAlias, CancellationToken cancellationToken);
}

public interface IPlayerAliasRepository
{
    Task<IReadOnlyList<string>> GetAliasesAsync(int playerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> FindPlayerIdsAsync(
        string normalizedAlias,
        int? teamId,
        CancellationToken cancellationToken);
}

public interface IMatchIntelligenceRunRepository
{
    Task<long> StartAsync(MatchIntelligenceRun run, CancellationToken cancellationToken);
    Task CompleteAsync(MatchIntelligenceRun run, CancellationToken cancellationToken);
    Task<MatchIntelligenceRun?> GetAsync(long fixtureId, DateTime cutoffAtUtc, CancellationToken cancellationToken);
}

public sealed record UpcomingIntelligenceFixture(
    long FixtureId,
    DateTime KickoffUtc,
    string HomeTeam,
    string AwayTeam,
    string League);

public interface IUpcomingIntelligenceFixtureRepository
{
    Task<IReadOnlyList<UpcomingIntelligenceFixture>> GetAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);
}

public sealed record RunMatchIntelligenceCommand(
    long FixtureId,
    DateTime CutoffUtc,
    bool ForceRefresh = false);

public sealed record MatchIntelligenceResult(
    long FixtureId,
    DateTime CutoffUtc,
    MatchIntelligenceSnapshotPair? Snapshot,
    PreMatchDecision Recommendation,
    IReadOnlyList<string> Reasons,
    int Documents,
    int Facts,
    int Conflicts,
    string Status);

public interface IMatchIntelligenceService
{
    Task<MatchIntelligenceResult> RunAsync(
        RunMatchIntelligenceCommand command,
        CancellationToken cancellationToken);
    Task<MatchIntelligenceResult?> GetLatestAsync(
        long fixtureId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken);
}

public interface IIntelligenceSnapshotBuilder
{
    MatchTeamIntelligenceSnapshot Build(
        IntelligenceFixtureDto fixture,
        IntelligenceTeamDto team,
        bool isHomeTeam,
        DateTime cutoffAtUtc,
        IReadOnlyCollection<FootballNewsDocument> documents,
        IReadOnlyCollection<FootballNewsFact> facts,
        IReadOnlyCollection<SquadPlayerDto> squad,
        FixtureLineupDto? officialLineup);
}
