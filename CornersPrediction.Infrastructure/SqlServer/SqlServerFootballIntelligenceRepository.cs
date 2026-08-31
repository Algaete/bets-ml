using System.Data;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Domain.FootballIntelligence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerFootballIntelligenceRepository :
    INewsDocumentRepository,
    INewsFactRepository,
    IIntelligenceSnapshotRepository,
    IFootballSourceRepository,
    ITeamAliasRepository,
    IPlayerAliasRepository,
    IMatchIntelligenceRunRepository,
    IUpcomingIntelligenceFixtureRepository
{
    private readonly string _connectionString;

    public SqlServerFootballIntelligenceRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    async Task<long> INewsDocumentRepository.UpsertAsync(
        FootballNewsDocument document,
        CancellationToken cancellationToken)
    {
        const string sql = """
SET XACT_ABORT ON;
DECLARE @PersistedId BIGINT;

UPDATE dbo.FootballNewsDocument WITH (UPDLOCK, SERIALIZABLE)
SET TeamId = COALESCE(@TeamId, TeamId),
    Url = @Url,
    CanonicalUrl = COALESCE(@CanonicalUrl, CanonicalUrl),
    ContentHash = COALESCE(@ContentHash, ContentHash),
    SourceDomain = @SourceDomain,
    SourceTier = @SourceTier,
    Title = @Title,
    Author = @Author,
    LanguageCode = @LanguageCode,
    PublishedAtUtc = COALESCE(@PublishedAtUtc, PublishedAtUtc),
    UpdatedAtUtc = COALESCE(@UpdatedAtUtc, UpdatedAtUtc),
    FirstSeenAtUtc = CASE WHEN FirstSeenAtUtc < @FirstSeenAtUtc THEN FirstSeenAtUtc ELSE @FirstSeenAtUtc END,
    RetrievedAtUtc = @RetrievedAtUtc,
    NormalizedText = COALESCE(@NormalizedText, NormalizedText),
    ExtractionStatus = @ExtractionStatus,
    HttpStatusCode = @HttpStatusCode,
    ErrorMessage = @ErrorMessage,
    RowUpdatedAtUtc = SYSUTCDATETIME()
WHERE FixtureId = @FixtureId
  AND ((TeamId = @TeamId) OR (TeamId IS NULL AND @TeamId IS NULL))
  AND UrlHash = @UrlHash;

IF @@ROWCOUNT = 0
BEGIN
    INSERT dbo.FootballNewsDocument
    (
        FixtureId, TeamId, Url, CanonicalUrl, UrlHash, ContentHash, SourceDomain, SourceTier,
        Title, Author, LanguageCode, PublishedAtUtc, UpdatedAtUtc, FirstSeenAtUtc,
        RetrievedAtUtc, NormalizedText, ExtractionStatus, HttpStatusCode, ErrorMessage
    )
    VALUES
    (
        @FixtureId, @TeamId, @Url, @CanonicalUrl, @UrlHash, @ContentHash, @SourceDomain, @SourceTier,
        @Title, @Author, @LanguageCode, @PublishedAtUtc, @UpdatedAtUtc, @FirstSeenAtUtc,
        @RetrievedAtUtc, @NormalizedText, @ExtractionStatus, @HttpStatusCode, @ErrorMessage
    );
    SET @PersistedId = SCOPE_IDENTITY();
END
ELSE
    SELECT @PersistedId = Id
    FROM dbo.FootballNewsDocument
    WHERE FixtureId = @FixtureId
      AND ((TeamId = @TeamId) OR (TeamId IS NULL AND @TeamId IS NULL))
      AND UrlHash = @UrlHash;

SELECT @PersistedId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            new
            {
                document.FixtureId,
                document.TeamId,
                document.Url,
                document.CanonicalUrl,
                document.UrlHash,
                document.ContentHash,
                document.SourceDomain,
                SourceTier = document.SourceTier.ToString(),
                document.Title,
                document.Author,
                document.LanguageCode,
                document.PublishedAtUtc,
                document.UpdatedAtUtc,
                document.FirstSeenAtUtc,
                document.RetrievedAtUtc,
                document.NormalizedText,
                ExtractionStatus = document.ExtractionStatus.ToString(),
                document.HttpStatusCode,
                document.ErrorMessage
            },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    async Task<IReadOnlyList<FootballNewsDocument>> INewsDocumentRepository.GetByFixtureAsync(
        long fixtureId,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT *
FROM dbo.FootballNewsDocument
WHERE FixtureId = @FixtureId
  AND FirstSeenAtUtc <= @CutoffAtUtc
  AND (PublishedAtUtc IS NULL OR PublishedAtUtc <= @CutoffAtUtc)
ORDER BY COALESCE(PublishedAtUtc, FirstSeenAtUtc), Id;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<NewsDocumentRow>(new CommandDefinition(
            sql,
            new { FixtureId = fixtureId, CutoffAtUtc = EnsureUtc(cutoffAtUtc) },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return rows.Select(MapDocument).ToArray();
    }

    async Task<IReadOnlyList<long>> INewsFactRepository.InsertAsync(
        IReadOnlyCollection<FootballNewsFact> facts,
        CancellationToken cancellationToken)
    {
        if (facts.Count == 0)
            return [];

        const string sql = """
DECLARE @PersistedId BIGINT;

SELECT @PersistedId = Id
FROM dbo.FootballNewsFact WITH (UPDLOCK, SERIALIZABLE)
WHERE NewsDocumentId = @NewsDocumentId
  AND FactHash = @FactHash;

IF @PersistedId IS NULL
BEGIN
INSERT dbo.FootballNewsFact
(
    NewsDocumentId, FactHash, FixtureId, TeamId, PlayerId, TeamNameExtracted, PlayerNameExtracted,
    PositionCode, EventType, AvailabilityStatus, Certainty, ProbabilityAvailable,
    ExpectedMinutesDelta, Reason, EvidenceSnippet, EventEffectiveAtUtc, ExpectedReturnAtUtc,
    FixtureRelevance, ExtractionConfidence, SourceConfidence, EffectiveConfidence,
    ResolutionStatus, IsCurrent, SupersededByFactId, ExtractionModel, PromptVersion,
    IsCurrentExtraction, FirstSeenAtUtc
)
VALUES
(
    @NewsDocumentId, @FactHash, @FixtureId, @TeamId, @PlayerId, @TeamNameExtracted, @PlayerNameExtracted,
    @PositionCode, @EventType, @AvailabilityStatus, @Certainty, @ProbabilityAvailable,
    @ExpectedMinutesDelta, @Reason, @EvidenceSnippet, @EventEffectiveAtUtc, @ExpectedReturnAtUtc,
    @FixtureRelevance, @ExtractionConfidence, @SourceConfidence, @EffectiveConfidence,
    @ResolutionStatus, @IsCurrent, @SupersededByFactId, @ExtractionModel, @PromptVersion,
    @IsCurrentExtraction, @FirstSeenAtUtc
);
SET @PersistedId = SCOPE_IDENTITY();
END;

SELECT @PersistedId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var ids = new List<long>(facts.Count);
        try
        {
            foreach (var fact in facts)
            {
                ids.Add(await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    sql,
                    FactParameters(fact),
                    transaction,
                    commandTimeout: 60,
                    cancellationToken: cancellationToken)));
            }
            await transaction.CommitAsync(cancellationToken);
            return ids;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    async Task<IReadOnlyList<FootballNewsFact>> INewsFactRepository.GetByFixtureAsync(
        long fixtureId,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT f.*
FROM dbo.FootballNewsFact f
INNER JOIN dbo.FootballNewsDocument d ON d.Id = f.NewsDocumentId
WHERE f.FixtureId = @FixtureId
  AND f.FirstSeenAtUtc <= @CutoffAtUtc
  AND d.FirstSeenAtUtc <= @CutoffAtUtc
  AND (d.PublishedAtUtc IS NULL OR d.PublishedAtUtc <= @CutoffAtUtc)
  AND f.IsCurrentExtraction = 1
ORDER BY f.CreatedAtUtc, f.Id;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<NewsFactRow>(new CommandDefinition(
            sql,
            new { FixtureId = fixtureId, CutoffAtUtc = EnsureUtc(cutoffAtUtc) },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return rows.Select(MapFact).ToArray();
    }

    async Task<long> IIntelligenceSnapshotRepository.UpsertAsync(
        MatchTeamIntelligenceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        const string sql = """
SET XACT_ABORT ON;
DECLARE @PersistedId BIGINT;

UPDATE dbo.MatchTeamIntelligenceSnapshot WITH (UPDLOCK, SERIALIZABLE)
SET IsHomeTeam = @IsHomeTeam,
    KickoffAtUtc = @KickoffAtUtc,
    DocumentCount = @DocumentCount,
    IndependentSourceCount = @IndependentSourceCount,
    ActionableFactCount = @ActionableFactCount,
    StructuredEvidenceCount = @StructuredEvidenceCount,
    ConfirmedOutCount = @ConfirmedOutCount,
    DoubtfulCount = @DoubtfulCount,
    SuspendedCount = @SuspendedCount,
    MissingStarterMinutesPct = @MissingStarterMinutesPct,
    MissingAttackMinutesPct = @MissingAttackMinutesPct,
    MissingMidfieldMinutesPct = @MissingMidfieldMinutesPct,
    MissingDefenceMinutesPct = @MissingDefenceMinutesPct,
    AttackAvailabilityImpact = @AttackAvailabilityImpact,
    MidfieldAvailabilityImpact = @MidfieldAvailabilityImpact,
    DefenceAvailabilityImpact = @DefenceAvailabilityImpact,
    GoalkeeperAvailabilityImpact = @GoalkeeperAvailabilityImpact,
    WidthAvailabilityImpact = @WidthAvailabilityImpact,
    SetPieceAvailabilityImpact = @SetPieceAvailabilityImpact,
    RotationRisk = @RotationRisk,
    FatigueRisk = @FatigueRisk,
    MoraleSignal = @MoraleSignal,
    CoachChangeDays = @CoachChangeDays,
    ExpectedFormation = @ExpectedFormation,
    FormationChangeExpected = @FormationChangeExpected,
    OfficialLineupAvailable = @OfficialLineupAvailable,
    ExpectedXiChanges = @ExpectedXiChanges,
    OverallNewsConfidence = @OverallNewsConfidence,
    ConflictCount = @ConflictCount,
    SnapshotAgeMinutes = @SnapshotAgeMinutes,
    MissingWingerMinutesPct = @MissingWingerMinutesPct,
    MissingFullBackMinutesPct = @MissingFullBackMinutesPct,
    MissingCornerTakerShare = @MissingCornerTakerShare,
    MissingCrossShare = @MissingCrossShare,
    CornerCreationImpact = @CornerCreationImpact,
    MissingShotShare = @MissingShotShare,
    MissingCreatorShare = @MissingCreatorShare,
    ShotGenerationImpact = @ShotGenerationImpact,
    MissingSotShare = @MissingSotShare,
    FinishingAvailabilityImpact = @FinishingAvailabilityImpact,
    MissingGoalShare = @MissingGoalShare,
    PenaltyTakerMissing = @PenaltyTakerMissing,
    GoalScoringAvailabilityImpact = @GoalScoringAvailabilityImpact,
    RiskFlagsJson = @RiskFlagsJson,
    DetailJson = @DetailJson,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FixtureId = @FixtureId AND TeamId = @TeamId AND CutoffAtUtc = @CutoffAtUtc;

IF @@ROWCOUNT = 0
BEGIN
    INSERT dbo.MatchTeamIntelligenceSnapshot
    (
        FixtureId, TeamId, IsHomeTeam, CutoffAtUtc, KickoffAtUtc, DocumentCount,
        IndependentSourceCount, ActionableFactCount, StructuredEvidenceCount,
        ConfirmedOutCount, DoubtfulCount, SuspendedCount, MissingStarterMinutesPct,
        MissingAttackMinutesPct, MissingMidfieldMinutesPct, MissingDefenceMinutesPct,
        AttackAvailabilityImpact, MidfieldAvailabilityImpact, DefenceAvailabilityImpact,
        GoalkeeperAvailabilityImpact, WidthAvailabilityImpact, SetPieceAvailabilityImpact,
        RotationRisk, FatigueRisk, MoraleSignal, CoachChangeDays, ExpectedFormation,
        FormationChangeExpected, OfficialLineupAvailable, ExpectedXiChanges,
        OverallNewsConfidence, ConflictCount, SnapshotAgeMinutes, MissingWingerMinutesPct,
        MissingFullBackMinutesPct, MissingCornerTakerShare, MissingCrossShare,
        CornerCreationImpact, MissingShotShare, MissingCreatorShare, ShotGenerationImpact,
        MissingSotShare, FinishingAvailabilityImpact, MissingGoalShare, PenaltyTakerMissing,
        GoalScoringAvailabilityImpact, RiskFlagsJson, DetailJson
    )
    VALUES
    (
        @FixtureId, @TeamId, @IsHomeTeam, @CutoffAtUtc, @KickoffAtUtc, @DocumentCount,
        @IndependentSourceCount, @ActionableFactCount, @StructuredEvidenceCount,
        @ConfirmedOutCount, @DoubtfulCount, @SuspendedCount, @MissingStarterMinutesPct,
        @MissingAttackMinutesPct, @MissingMidfieldMinutesPct, @MissingDefenceMinutesPct,
        @AttackAvailabilityImpact, @MidfieldAvailabilityImpact, @DefenceAvailabilityImpact,
        @GoalkeeperAvailabilityImpact, @WidthAvailabilityImpact, @SetPieceAvailabilityImpact,
        @RotationRisk, @FatigueRisk, @MoraleSignal, @CoachChangeDays, @ExpectedFormation,
        @FormationChangeExpected, @OfficialLineupAvailable, @ExpectedXiChanges,
        @OverallNewsConfidence, @ConflictCount, @SnapshotAgeMinutes, @MissingWingerMinutesPct,
        @MissingFullBackMinutesPct, @MissingCornerTakerShare, @MissingCrossShare,
        @CornerCreationImpact, @MissingShotShare, @MissingCreatorShare, @ShotGenerationImpact,
        @MissingSotShare, @FinishingAvailabilityImpact, @MissingGoalShare, @PenaltyTakerMissing,
        @GoalScoringAvailabilityImpact, @RiskFlagsJson, @DetailJson
    );
    SET @PersistedId = SCOPE_IDENTITY();
END
ELSE
    SELECT @PersistedId = Id FROM dbo.MatchTeamIntelligenceSnapshot
    WHERE FixtureId = @FixtureId AND TeamId = @TeamId AND CutoffAtUtc = @CutoffAtUtc;

SELECT @PersistedId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            snapshot,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    async Task<MatchIntelligenceSnapshotPair?> IIntelligenceSnapshotRepository.GetLatestPairAsync(
        long fixtureId,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH Latest AS
(
    SELECT MAX(CutoffAtUtc) AS CutoffAtUtc
    FROM dbo.MatchTeamIntelligenceSnapshot
    WHERE FixtureId = @FixtureId
      AND CutoffAtUtc <= @CutoffAtUtc
      AND CutoffAtUtc <= KickoffAtUtc
)
SELECT s.*
FROM dbo.MatchTeamIntelligenceSnapshot s
INNER JOIN Latest l ON l.CutoffAtUtc = s.CutoffAtUtc
WHERE s.FixtureId = @FixtureId
ORDER BY s.IsHomeTeam DESC, s.TeamId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<MatchTeamIntelligenceSnapshot>(new CommandDefinition(
            sql,
            new { FixtureId = fixtureId, CutoffAtUtc = EnsureUtc(cutoffAtUtc) },
            commandTimeout: 60,
            cancellationToken: cancellationToken))).ToArray();
        if (rows.Length == 0)
            return null;
        return new MatchIntelligenceSnapshotPair(
            fixtureId,
            rows.SingleOrDefault(row => row.IsHomeTeam),
            rows.SingleOrDefault(row => !row.IsHomeTeam));
    }

    async Task<IReadOnlyDictionary<long, MatchIntelligenceSnapshotPair>> IIntelligenceSnapshotRepository.GetLatestPairsAsync(
        IReadOnlyCollection<long> fixtureIds,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken)
    {
        if (fixtureIds.Count == 0)
            return new Dictionary<long, MatchIntelligenceSnapshotPair>();
        const string sql = """
WITH Ranked AS
(
    SELECT
        s.*,
        DENSE_RANK() OVER (PARTITION BY s.FixtureId ORDER BY s.CutoffAtUtc DESC) AS CutoffRank
    FROM dbo.MatchTeamIntelligenceSnapshot s
    WHERE s.FixtureId IN @FixtureIds
      AND s.CutoffAtUtc <= @CutoffAtUtc
      AND s.CutoffAtUtc <= s.KickoffAtUtc
)
SELECT *
FROM Ranked
WHERE CutoffRank = 1
ORDER BY FixtureId, IsHomeTeam DESC, TeamId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<MatchTeamIntelligenceSnapshot>(new CommandDefinition(
            sql,
            new
            {
                FixtureIds = fixtureIds.Distinct().ToArray(),
                CutoffAtUtc = EnsureUtc(cutoffAtUtc)
            },
            commandTimeout: 60,
            cancellationToken: cancellationToken))).ToArray();
        return rows
            .GroupBy(row => row.FixtureId)
            .ToDictionary(
                group => group.Key,
                group => new MatchIntelligenceSnapshotPair(
                    group.Key,
                    group.SingleOrDefault(row => row.IsHomeTeam),
                    group.SingleOrDefault(row => !row.IsHomeTeam)));
    }

    async Task<IReadOnlyList<MatchTeamIntelligenceSnapshot>> IIntelligenceSnapshotRepository.GetHistoryAsync(
        long fixtureId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT * FROM dbo.MatchTeamIntelligenceSnapshot
WHERE FixtureId = @FixtureId
ORDER BY CutoffAtUtc, IsHomeTeam DESC;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<MatchTeamIntelligenceSnapshot>(new CommandDefinition(
            sql,
            new { FixtureId = fixtureId },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    async Task<IReadOnlyList<UpcomingIntelligenceFixture>> IUpcomingIntelligenceFixtureRepository.GetAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    FixtureId = ExternalFixtureId,
    KickoffUtc = FechaPartido,
    HomeTeam = EquipoLocal,
    AwayTeam = EquipoVisita,
    League = Liga
FROM dbo.PartidosProximos
WHERE ExternalFixtureId IS NOT NULL
  AND DataSource = 'API-Football'
  AND FechaPartido >= @FromUtc
  AND FechaPartido <= @ToUtc
  AND ISNULL(FixtureStatus, 'NS') NOT IN ('FT', 'AET', 'PEN', 'CANC', 'PST', 'ABD')
ORDER BY FechaPartido, ExternalFixtureId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UpcomingIntelligenceFixture>(new CommandDefinition(
            sql,
            new
            {
                FromUtc = EnsureUtc(fromUtc),
                ToUtc = EnsureUtc(toUtc)
            },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    async Task<FootballSourceConfiguration?> IFootballSourceRepository.GetAsync(
        string domain,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT TOP (1) Domain, SourceTier, ConfidenceWeight, IsEnabled, UpdatedAtUtc
FROM dbo.FootballSourceConfiguration
WHERE Domain = @Domain OR @Domain LIKE N'%.' + Domain
ORDER BY LEN(Domain) DESC;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SourceRow>(new CommandDefinition(
            sql,
            new { Domain = NormalizeDomain(domain) },
            cancellationToken: cancellationToken));
        return row is null ? null : MapSource(row);
    }

    async Task<IReadOnlyList<FootballSourceConfiguration>> IFootballSourceRepository.GetAllAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT Domain, SourceTier, ConfidenceWeight, IsEnabled, UpdatedAtUtc
FROM dbo.FootballSourceConfiguration
ORDER BY Domain;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<SourceRow>(new CommandDefinition(
            sql,
            cancellationToken: cancellationToken));
        return rows.Select(MapSource).ToArray();
    }

    async Task<IReadOnlyList<string>> ITeamAliasRepository.GetAliasesAsync(
        int teamId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT Alias FROM dbo.FootballTeamAlias WHERE TeamId = @TeamId AND IsEnabled = 1 ORDER BY Alias;";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            sql,
            new { TeamId = teamId },
            cancellationToken: cancellationToken))).ToArray();
    }

    async Task<IReadOnlyList<int>> ITeamAliasRepository.FindTeamIdsAsync(
        string normalizedAlias,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT DISTINCT TeamId FROM dbo.FootballTeamAlias WHERE NormalizedAlias = @Alias AND IsEnabled = 1 ORDER BY TeamId;";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<int>(new CommandDefinition(
            sql,
            new { Alias = normalizedAlias },
            cancellationToken: cancellationToken))).ToArray();
    }

    async Task<IReadOnlyList<string>> IPlayerAliasRepository.GetAliasesAsync(
        int playerId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT Alias FROM dbo.FootballPlayerAlias WHERE PlayerId = @PlayerId AND IsEnabled = 1 ORDER BY Alias;";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            sql,
            new { PlayerId = playerId },
            cancellationToken: cancellationToken))).ToArray();
    }

    async Task<IReadOnlyList<int>> IPlayerAliasRepository.FindPlayerIdsAsync(
        string normalizedAlias,
        int? teamId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT DISTINCT PlayerId
FROM dbo.FootballPlayerAlias
WHERE NormalizedAlias = @Alias
  AND IsEnabled = 1
  AND (@TeamId IS NULL OR TeamId IS NULL OR TeamId = @TeamId)
ORDER BY PlayerId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<int>(new CommandDefinition(
            sql,
            new { Alias = normalizedAlias, TeamId = teamId },
            cancellationToken: cancellationToken))).ToArray();
    }

    async Task<long> IMatchIntelligenceRunRepository.StartAsync(
        MatchIntelligenceRun run,
        CancellationToken cancellationToken)
    {
        const string sql = """
DECLARE @PersistedId BIGINT;
UPDATE dbo.MatchIntelligenceRun WITH (UPDLOCK, SERIALIZABLE)
SET StartedAtUtc = @StartedAtUtc,
    FinishedAtUtc = NULL,
    Status = @Status,
    QueriesGenerated = 0,
    SearchResults = 0,
    DocumentsDownloaded = 0,
    DocumentsProcessed = 0,
    FactsExtracted = 0,
    ResolvedFacts = 0,
    UnresolvedFacts = 0,
    ConflictCount = 0,
    ApiCost = 0,
    LlmTokensInput = 0,
    LlmTokensOutput = 0,
    ErrorMessage = NULL,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FixtureId = @FixtureId AND CutoffAtUtc = @CutoffAtUtc;

IF @@ROWCOUNT = 0
BEGIN
    INSERT dbo.MatchIntelligenceRun(FixtureId, CutoffAtUtc, StartedAtUtc, Status)
    VALUES(@FixtureId, @CutoffAtUtc, @StartedAtUtc, @Status);
    SET @PersistedId = SCOPE_IDENTITY();
END
ELSE
    SELECT @PersistedId = Id FROM dbo.MatchIntelligenceRun WHERE FixtureId = @FixtureId AND CutoffAtUtc = @CutoffAtUtc;
SELECT @PersistedId;
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            new
            {
                run.FixtureId,
                CutoffAtUtc = EnsureUtc(run.CutoffAtUtc),
                StartedAtUtc = EnsureUtc(run.StartedAtUtc),
                Status = MatchIntelligenceRunStatus.Running.ToString()
            },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    async Task IMatchIntelligenceRunRepository.CompleteAsync(
        MatchIntelligenceRun run,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.MatchIntelligenceRun
SET FinishedAtUtc = @FinishedAtUtc,
    Status = @Status,
    QueriesGenerated = @QueriesGenerated,
    SearchResults = @SearchResults,
    DocumentsDownloaded = @DocumentsDownloaded,
    DocumentsProcessed = @DocumentsProcessed,
    FactsExtracted = @FactsExtracted,
    ResolvedFacts = @ResolvedFacts,
    UnresolvedFacts = @UnresolvedFacts,
    ConflictCount = @ConflictCount,
    ApiCost = @ApiCost,
    LlmTokensInput = @LlmTokensInput,
    LlmTokensOutput = @LlmTokensOutput,
    ErrorMessage = @ErrorMessage,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE (@Id > 0 AND Id = @Id)
   OR (@Id = 0 AND FixtureId = @FixtureId AND CutoffAtUtc = @CutoffAtUtc);
""";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                run.Id,
                run.FixtureId,
                CutoffAtUtc = EnsureUtc(run.CutoffAtUtc),
                FinishedAtUtc = run.FinishedAtUtc.HasValue ? EnsureUtc(run.FinishedAtUtc.Value) : DateTime.UtcNow,
                Status = run.Status.ToString(),
                run.QueriesGenerated,
                run.SearchResults,
                run.DocumentsDownloaded,
                run.DocumentsProcessed,
                run.FactsExtracted,
                run.ResolvedFacts,
                run.UnresolvedFacts,
                run.ConflictCount,
                run.ApiCost,
                run.LlmTokensInput,
                run.LlmTokensOutput,
                run.ErrorMessage
            },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    async Task<MatchIntelligenceRun?> IMatchIntelligenceRunRepository.GetAsync(
        long fixtureId,
        DateTime cutoffAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM dbo.MatchIntelligenceRun WHERE FixtureId = @FixtureId AND CutoffAtUtc = @CutoffAtUtc;";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
            sql,
            new { FixtureId = fixtureId, CutoffAtUtc = EnsureUtc(cutoffAtUtc) },
            cancellationToken: cancellationToken));
        return row is null ? null : MapRun(row);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static object FactParameters(FootballNewsFact fact) => new
    {
        fact.NewsDocumentId,
        fact.FactHash,
        fact.FixtureId,
        fact.TeamId,
        fact.PlayerId,
        fact.TeamNameExtracted,
        fact.PlayerNameExtracted,
        fact.PositionCode,
        EventType = fact.EventType.ToString(),
        AvailabilityStatus = fact.AvailabilityStatus.ToString(),
        Certainty = fact.Certainty.ToString(),
        fact.ProbabilityAvailable,
        fact.ExpectedMinutesDelta,
        fact.Reason,
        fact.EvidenceSnippet,
        fact.EventEffectiveAtUtc,
        fact.ExpectedReturnAtUtc,
        fact.FixtureRelevance,
        fact.ExtractionConfidence,
        fact.SourceConfidence,
        fact.EffectiveConfidence,
        ResolutionStatus = fact.ResolutionStatus.ToString(),
        fact.IsCurrent,
        fact.SupersededByFactId,
        fact.ExtractionModel,
        fact.PromptVersion,
        fact.IsCurrentExtraction,
        fact.FirstSeenAtUtc
    };

    private static FootballNewsDocument MapDocument(NewsDocumentRow row) => new()
    {
        Id = row.Id,
        FixtureId = row.FixtureId,
        TeamId = row.TeamId,
        Url = row.Url,
        CanonicalUrl = row.CanonicalUrl,
        UrlHash = row.UrlHash,
        ContentHash = row.ContentHash,
        SourceDomain = row.SourceDomain,
        SourceTier = ParseEnum<NewsSourceTier>(row.SourceTier),
        Title = row.Title,
        Author = row.Author,
        LanguageCode = row.LanguageCode,
        PublishedAtUtc = row.PublishedAtUtc,
        UpdatedAtUtc = row.UpdatedAtUtc,
        FirstSeenAtUtc = row.FirstSeenAtUtc,
        RetrievedAtUtc = row.RetrievedAtUtc,
        NormalizedText = row.NormalizedText,
        ExtractionStatus = ParseEnum<NewsExtractionStatus>(row.ExtractionStatus),
        HttpStatusCode = row.HttpStatusCode,
        ErrorMessage = row.ErrorMessage,
        CreatedAtUtc = row.CreatedAtUtc
    };

    private static FootballNewsFact MapFact(NewsFactRow row) => new()
    {
        Id = row.Id,
        NewsDocumentId = row.NewsDocumentId,
        FactHash = row.FactHash,
        FixtureId = row.FixtureId,
        TeamId = row.TeamId,
        PlayerId = row.PlayerId,
        TeamNameExtracted = row.TeamNameExtracted,
        PlayerNameExtracted = row.PlayerNameExtracted,
        PositionCode = row.PositionCode,
        EventType = ParseEnum<FootballNewsEventType>(row.EventType),
        AvailabilityStatus = ParseEnum<AvailabilityStatus>(row.AvailabilityStatus),
        Certainty = ParseEnum<FactCertainty>(row.Certainty),
        ProbabilityAvailable = row.ProbabilityAvailable,
        ExpectedMinutesDelta = row.ExpectedMinutesDelta,
        Reason = row.Reason,
        EvidenceSnippet = row.EvidenceSnippet,
        EventEffectiveAtUtc = row.EventEffectiveAtUtc,
        ExpectedReturnAtUtc = row.ExpectedReturnAtUtc,
        FixtureRelevance = row.FixtureRelevance,
        ExtractionConfidence = row.ExtractionConfidence,
        SourceConfidence = row.SourceConfidence,
        EffectiveConfidence = row.EffectiveConfidence,
        ResolutionStatus = ParseEnum<EntityResolutionStatus>(row.ResolutionStatus),
        IsCurrent = row.IsCurrent,
        SupersededByFactId = row.SupersededByFactId,
        ExtractionModel = row.ExtractionModel,
        PromptVersion = row.PromptVersion,
        IsCurrentExtraction = row.IsCurrentExtraction,
        FirstSeenAtUtc = row.FirstSeenAtUtc,
        CreatedAtUtc = row.CreatedAtUtc
    };

    private static FootballSourceConfiguration MapSource(SourceRow row) =>
        new(row.Domain, ParseEnum<NewsSourceTier>(row.SourceTier), row.ConfidenceWeight, row.IsEnabled, row.UpdatedAtUtc);

    private static MatchIntelligenceRun MapRun(RunRow row) => new()
    {
        Id = row.Id,
        FixtureId = row.FixtureId,
        CutoffAtUtc = row.CutoffAtUtc,
        StartedAtUtc = row.StartedAtUtc,
        FinishedAtUtc = row.FinishedAtUtc,
        Status = ParseEnum<MatchIntelligenceRunStatus>(row.Status),
        QueriesGenerated = row.QueriesGenerated,
        SearchResults = row.SearchResults,
        DocumentsDownloaded = row.DocumentsDownloaded,
        DocumentsProcessed = row.DocumentsProcessed,
        FactsExtracted = row.FactsExtracted,
        ResolvedFacts = row.ResolvedFacts,
        UnresolvedFacts = row.UnresolvedFacts,
        ConflictCount = row.ConflictCount,
        ApiCost = row.ApiCost,
        LlmTokensInput = row.LlmTokensInput,
        LlmTokensOutput = row.LlmTokensOutput,
        ErrorMessage = row.ErrorMessage,
        CreatedAtUtc = row.CreatedAtUtc
    };

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed)
            ? parsed
            : throw new DataException($"Database value '{value}' is not valid for {typeof(T).Name}.");

    private static string NormalizeDomain(string domain)
    {
        var value = domain.Trim().TrimStart('.').ToLowerInvariant();
        return value.StartsWith("www.", StringComparison.Ordinal) ? value[4..] : value;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class NewsDocumentRow
    {
        public long Id { get; init; }
        public long FixtureId { get; init; }
        public int? TeamId { get; init; }
        public string Url { get; init; } = string.Empty;
        public string? CanonicalUrl { get; init; }
        public string UrlHash { get; init; } = string.Empty;
        public string? ContentHash { get; init; }
        public string SourceDomain { get; init; } = string.Empty;
        public string SourceTier { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Author { get; init; }
        public string? LanguageCode { get; init; }
        public DateTime? PublishedAtUtc { get; init; }
        public DateTime? UpdatedAtUtc { get; init; }
        public DateTime FirstSeenAtUtc { get; init; }
        public DateTime RetrievedAtUtc { get; init; }
        public string? NormalizedText { get; init; }
        public string ExtractionStatus { get; init; } = string.Empty;
        public int? HttpStatusCode { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class NewsFactRow
    {
        public long Id { get; init; }
        public long NewsDocumentId { get; init; }
        public string FactHash { get; init; } = string.Empty;
        public long FixtureId { get; init; }
        public int? TeamId { get; init; }
        public int? PlayerId { get; init; }
        public string TeamNameExtracted { get; init; } = string.Empty;
        public string? PlayerNameExtracted { get; init; }
        public string? PositionCode { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string AvailabilityStatus { get; init; } = string.Empty;
        public string Certainty { get; init; } = string.Empty;
        public decimal? ProbabilityAvailable { get; init; }
        public decimal? ExpectedMinutesDelta { get; init; }
        public string? Reason { get; init; }
        public string EvidenceSnippet { get; init; } = string.Empty;
        public DateTime? EventEffectiveAtUtc { get; init; }
        public DateTime? ExpectedReturnAtUtc { get; init; }
        public decimal FixtureRelevance { get; init; }
        public decimal ExtractionConfidence { get; init; }
        public decimal SourceConfidence { get; init; }
        public decimal EffectiveConfidence { get; init; }
        public string ResolutionStatus { get; init; } = string.Empty;
        public bool IsCurrent { get; init; }
        public long? SupersededByFactId { get; init; }
        public string ExtractionModel { get; init; } = string.Empty;
        public string PromptVersion { get; init; } = string.Empty;
        public bool IsCurrentExtraction { get; init; }
        public DateTime FirstSeenAtUtc { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class SourceRow
    {
        public string Domain { get; init; } = string.Empty;
        public string SourceTier { get; init; } = string.Empty;
        public decimal ConfidenceWeight { get; init; }
        public bool IsEnabled { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    private sealed class RunRow
    {
        public long Id { get; init; }
        public long FixtureId { get; init; }
        public DateTime CutoffAtUtc { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public DateTime? FinishedAtUtc { get; init; }
        public string Status { get; init; } = string.Empty;
        public int QueriesGenerated { get; init; }
        public int SearchResults { get; init; }
        public int DocumentsDownloaded { get; init; }
        public int DocumentsProcessed { get; init; }
        public int FactsExtracted { get; init; }
        public int ResolvedFacts { get; init; }
        public int UnresolvedFacts { get; init; }
        public int ConflictCount { get; init; }
        public decimal ApiCost { get; init; }
        public int LlmTokensInput { get; init; }
        public int LlmTokensOutput { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }
}
