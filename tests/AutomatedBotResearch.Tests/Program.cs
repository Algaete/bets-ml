using CornersPrediction.Application.AutomatedCorners;

var repository = new CapturingRepository();
var useCase = new GetAutomatedBotResearchEvaluationsUseCase(repository);

var page = await useCase.GetAsync(
    new AutomatedBotResearchFilterRequest(
        new DateTime(2026, 9, 1, 18, 30, 0, DateTimeKind.Local),
        new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Local),
        " corners ",
        " awayteamcorners ",
        " h ",
        " approved ",
        " productionblocked ",
        2,
        25),
    CancellationToken.None);

var query = repository.LastQuery
    ?? throw new InvalidOperationException("The normalized query was not forwarded.");
Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), query.DateFromUtc, "dateFrom");
Equal(new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), query.DateToExclusiveUtc, "inclusive dateTo");
Equal("CORNERS", query.MarketFamily, "market family");
Equal("AwayTeamCorners", query.MarketType, "market type");
Equal("H2026", query.BotKey, "bot alias");
Equal("Approved", query.ModelDecision, "model decision");
Equal("ProductionBlocked", query.PublicationStatus, "publication status");
Equal(2, page.Page, "page");
Equal(25, page.PageSize, "page size");

Throws(() => GetAutomatedBotResearchEvaluationsUseCase.Normalize(
    new AutomatedBotResearchFilterRequest(
        null, null, "GOALS", "TotalCorners", null, null, null)),
    "cross-family market must be rejected");
Throws(() => GetAutomatedBotResearchEvaluationsUseCase.Normalize(
    new AutomatedBotResearchFilterRequest(
        null, null, null, null, "G2026", null, null)),
    "G is not part of the C/D/E/F/H research universe");
Throws(() => GetAutomatedBotResearchEvaluationsUseCase.Normalize(
    new AutomatedBotResearchFilterRequest(
        null, null, null, null, null, null, null, Page: 0)),
    "zero page must be rejected");
Throws(() => GetAutomatedBotResearchEvaluationsUseCase.Normalize(
    new AutomatedBotResearchFilterRequest(
        null, null, null, null, null, null, null,
        PageSize: GetAutomatedBotResearchEvaluationsUseCase.MaximumPageSize + 1)),
    "oversized page must be rejected");

var reasons = GetAutomatedBotResearchEvaluationsUseCase.ParseDecisionReasons(
    "[\"edge below threshold\",{\"code\":\"OOD\"}]");
Equal(2, reasons.Count, "decision reason count");
Equal("edge below threshold", reasons[0], "string decision reason");
Equal("{\"code\":\"OOD\"}", reasons[1], "structured legacy decision reason");
Equal(0, GetAutomatedBotResearchEvaluationsUseCase.ParseDecisionReasons("not-json").Count,
    "malformed legacy reasons remain page-safe");

var pendingPublication = GetAutomatedBotResearchEvaluationsUseCase.Normalize(
    new AutomatedBotResearchFilterRequest(
        null, null, null, null, null, null, "pendingdata"));
Equal("PendingData", pendingPublication.PublicationStatus, "pending-data publication state");

await GeneralCandidatesKeepEveryScopeAndDecision();
await GeneralPicksPreservePublishedSelectionsAndCustomBots();
LegacyResearchCompatibilityIsReadOnly();

Console.WriteLine("PASS research filters normalize and remain bounded");
Console.WriteLine("PASS bot/market families are fail-closed to C/D/E/F/H");
Console.WriteLine("PASS decision reasons tolerate legacy malformed JSON");
Console.WriteLine("PASS legacy decision compatibility is derived without ledger backfills");
Console.WriteLine("PASS general candidates retain every market scope and production state unless explicitly filtered");
Console.WriteLine("PASS general picks preserve legacy/custom records and distinguish missing model audits");

static async Task GeneralPicksPreservePublishedSelectionsAndCustomBots()
{
    foreach (var bot in new[] { "A", "B", "MY_CLONE_2", "C2026", "H2026", "AUTOMATEDCORNERSBOTV1.0" })
    {
        var query = GetAutomatedBotGeneralPicksUseCase.Normalize(
            new AutomatedBotResearchFilterRequest(null, null, "GOALS", null, bot, null, null));
        Equal(bot, query.BotKey, "general bot visibility includes legacy, retired, clones and shadow");
        Equal<string?>(null, query.ModelDecision, "custom bot does not impose approval");
        Equal<string?>(null, query.PublicationStatus, "custom bot does not impose publication");
    }

    var legacyFilter = GetAutomatedBotGeneralPicksUseCase.Normalize(
        new AutomatedBotResearchFilterRequest(null, null, null, null,
            " a ", " notrecorded ", " published "));
    Equal("NotRecorded", legacyFilter.ModelDecision, "legacy missing model audit has an explicit state");
    Equal("Published", legacyFilter.PublicationStatus, "legacy publication is separately recorded");
    Equal("A", legacyFilter.BotKey, "legacy bot key canonicalizes");
    Throws(() => GetAutomatedBotGeneralPicksUseCase.Normalize(
        new AutomatedBotResearchFilterRequest(null, null, null, null, "G2026", null, null)),
        "general retains the independent G laboratory boundary");
    Throws(() => GetAutomatedBotGeneralPicksUseCase.Normalize(
        new AutomatedBotResearchFilterRequest(null, null, null, null, "I2026", null, null)),
        "general retains the independent I laboratory boundary");

    var expected = new AutomatedBotResearchPage(
        [new() { EvaluationId = 1, ModelDecision = "Approved", PublicationStatus = "ProductionBlocked" },
         new() { EvaluationId = 2, ModelDecision = "Rejected", PublicationStatus = "ModelRejected" },
         new() { EvaluationId = -1, ModelDecision = "NotRecorded", PublicationStatus = "Published",
             PublishedSelectionId = 1, RecordKind = "PublishedSelection", BotKey = "A" }],
        3, 1, 100, 1);
    var repository = new GeneralRepository(expected);
    var actual = await new GetAutomatedBotGeneralPicksUseCase(repository).GetAsync(
        new AutomatedBotResearchFilterRequest(null, null, null, null, null, null, null),
        CancellationToken.None);
    Equal(expected, actual, "general use case does not remove blocked/rejected/legacy rows");

    var sqlRoot = Path.Combine(FindRepositoryRoot(),
        "CornersPrediction.Infrastructure", "SqlServer");
    var sql = File.ReadAllText(Path.Combine(sqlRoot,
        "SqlServerAutomatedBotGeneralPicksRepository.cs"))
        + File.ReadAllText(Path.Combine(sqlRoot,
            "SqlServerAutomatedBotGeneralPicksRepository.Sorting.cs"));
    Contains(sql, "AND NOT EXISTS", "published fallback excludes linked audit rows");
    Contains(sql, "linked.PublishedSelectionId = selection.AutomatedCornerBetSelectionId",
        "deduplication uses the authoritative selection id");
    Contains(sql, "INTO #ResearchPageKeys", "general freezes the union page before detail lookup");
    Contains(sql, ") + (SELECT COUNT_BIG(*) FROM #GeneralPublishedKeys)",
        "general count includes the same unmatched published keys as pagination");
    NotContains(sql, "SELECT @AuditCount =",
        "count must not disable parameter embedding through variable assignment");
    Contains(sql, "selection.Status = N'Won' AND selection.SettlementFactor = 0.5000",
        "legacy half wins keep the settled Asian result");
    Contains(sql, "selection.Status = N'Lost' AND selection.SettlementFactor = -0.5000",
        "legacy half losses keep the settled Asian result");
    Contains(sql, "INTO #GeneralAuditCandidates",
        "filtered reads freeze narrow metadata before resolving legacy JSON");
    Contains(sql, "evaluation.PerformanceLegacyPublicationRejection = 1",
        "filtered reads use the persisted compatibility flag without excluding any bot universe");
    Contains(sql, "WHERE candidate.IsLegacyRejection = 1",
        "only legacy publication rejections fetch the reason JSON");
    Contains(sql, "AND effective.PublicationStatus <> N'Published'",
        "not-published filter includes every nonpublished state");
}

static async Task GeneralCandidatesKeepEveryScopeAndDecision()
{
    foreach (var family in new[] { "CORNERS", "GOALS", "SHOTS", "SOG" })
    {
        foreach (var optional in new string?[] { null, "", "  " })
        {
            var repository = new CapturingRepository();
            await new GetAutomatedBotResearchEvaluationsUseCase(repository).GetAsync(
                new AutomatedBotResearchFilterRequest(
                    null, null, family, optional, optional, optional, optional),
                CancellationToken.None);
            var query = repository.LastQuery!;
            Equal(family, query.MarketFamily, "selected general family");
            Equal<string?>(null, query.MarketType, "general includes local, away and totals");
            Equal<string?>(null, query.BotKey, "general includes every supported bot");
            Equal<string?>(null, query.ModelDecision, "general retains rejected and approved candidates");
            Equal<string?>(null, query.PublicationStatus, "general retains blocked and published candidates");
        }
    }

    foreach (var status in new[] { "Published", "ProductionBlocked", "ModelRejected", "NotPublished" })
    {
        var query = GetAutomatedBotResearchEvaluationsUseCase.Normalize(
            new AutomatedBotResearchFilterRequest(
                null, null, "GOALS", null, null, null, status));
        Equal(status, query.PublicationStatus, "explicit production-state filter");
        Equal<string?>(null, query.ModelDecision, "production filter does not impose model approval");
        Equal<string?>(null, query.MarketType, "production filter does not impose market scope");
    }

    var repositorySource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "CornersPrediction.Infrastructure", "SqlServer",
        "SqlServerAutomatedBotResearchRepository.cs"));
    Contains(repositorySource, "@MarketType IS NULL OR evaluation.MarketType = @MarketType",
        "unspecified scope retains local, away and totals in the database read");
    Contains(repositorySource, "@PublicationStatus IS NULL",
        "unspecified production state retains blocked and published candidates in the database read");
}

static void LegacyResearchCompatibilityIsReadOnly()
{
    var root = FindRepositoryRoot();
    var migration = File.ReadAllText(Path.Combine(
        root, "CornersPredictionApi", "sql", "20260904_automated_bot_research.sql"));
    var procedureStart = migration.IndexOf(
        "CREATE OR ALTER PROCEDURE dbo.sp_UpsertAutomatedBotPickEvaluation",
        StringComparison.Ordinal);
    if (procedureStart <= 0)
        throw new InvalidOperationException("append-only research delta was not found");

    var compatibilitySection = migration[..procedureStart];
    NotContains(compatibilitySection, "UPDATE dbo.AutomatedBotPickEvaluations",
        "bootstrap must not rewrite the evaluation ledger");
    NotContains(compatibilitySection, "UPDATE evaluation",
        "bootstrap must not rank legacy rows by mutation");

    var researchRepository = File.ReadAllText(Path.Combine(
        root,
        "CornersPrediction.Infrastructure",
        "SqlServer",
        "SqlServerAutomatedBotResearchRepository.cs"));
    Contains(researchRepository, "REJECTED_PRODUCTION_GATE",
        "research reads production-gated legacy approvals");
    Contains(researchRepository, "REJECTED_LOWER_RANKED_CANDIDATE",
        "research reads lower-ranked legacy approvals");
    Contains(researchRepository,
        "@ModelDecision IS NULL OR effective.ModelDecision = @ModelDecision",
        "model-decision filter uses the effective decision");
    Contains(researchRepository,
        "ModelDecision = effective.ModelDecision",
        "model-decision projection uses the filtered decision");
    Contains(researchRepository,
        "PublicationStatus = publication.PublicationStatus",
        "publication projection uses the filtered status");

    var performanceRepository = File.ReadAllText(Path.Combine(
        root,
        "CornersPrediction.Infrastructure",
        "SqlServer",
        "SqlServerAutomatedBotPerformanceEvidenceRepository.cs"));
    Contains(performanceRepository, "evaluation.PerformanceLegacyPublicationRejection = 1",
        "performance reads the indexed legacy compatibility predicate");
    var readIndexes = File.ReadAllText(Path.Combine(
        root, "CornersPredictionApi", "SqlScripts", "BotAutomationReadIndexes.sql"));
    Contains(readIndexes, "REJECTED_PRODUCTION_GATE",
        "the persisted predicate retains production-gated legacy approvals");
    Contains(readIndexes, "REJECTED_LOWER_RANKED_CANDIDATE",
        "the persisted predicate retains lower-ranked legacy approvals");
    Contains(performanceRepository, "AND evaluation.Decision = N'Approved'",
        "performance reads current approvals through its sargable branch");
    Contains(performanceRepository, "AND evaluation.Decision = N'Rejected'",
        "performance keeps a separate legacy-approval branch");
    Contains(performanceRepository, "CREATE TABLE #Candidates",
        "performance materializes the union of current and legacy approvals before ranking");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected '{expected}', received '{actual}'.");
}

static void Throws(Action action, string label)
{
    try
    {
        action();
    }
    catch (ArgumentException)
    {
        return;
    }

    throw new InvalidOperationException($"{label}: expected ArgumentException.");
}

static void Contains(string value, string expected, string label)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"{label}: expected source fragment was not found");
}

static void NotContains(string value, string unexpected, string label)
{
    if (value.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidOperationException($"{label}: unexpected source fragment was found");
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(
                directory.FullName,
                "CornersPrediction.Application",
                "CornersPrediction.Application.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Repository root was not found.");
}

sealed class CapturingRepository : IAutomatedBotResearchRepository
{
    public AutomatedBotResearchQuery? LastQuery { get; private set; }

    public Task<AutomatedBotResearchPage> GetEvaluationsAsync(
        AutomatedBotResearchQuery query,
        CancellationToken cancellationToken)
    {
        LastQuery = query;
        return Task.FromResult(new AutomatedBotResearchPage(
            [], 0, query.Page, query.PageSize, 0));
    }
}

sealed class GeneralRepository(AutomatedBotResearchPage page) : IAutomatedBotGeneralPicksRepository
{
    public Task<AutomatedBotResearchPage> GetGeneralPicksAsync(
        AutomatedBotResearchQuery query, CancellationToken cancellationToken) => Task.FromResult(page);
}
