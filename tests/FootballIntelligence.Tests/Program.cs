using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Domain.FootballIntelligence;
using CornersPrediction.Application.MatchHistory;
using CornersPrediction.Application.Teams;
using CornersPrediction.Infrastructure.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;

const int teamId = 123;
SquadPlayerDto[] squad =
[
    new(teamId, 5450, "", null, null, null),
    new(teamId, 5450, "Known player", "Attacker", 27, "photo"),
    new(teamId, 5450, "Known player", "Attacker", 27, "photo"),
    new(999, 5450, "Other team", "Goalkeeper", null, null),
    new(teamId, 0, "Invalid identity", "Defender", null, null)
];
var players = SquadPlayerLookup.ForTeam(squad, teamId);
Require(players.Count == 1, "Only one valid player from the requested team should remain.");
Require(players[5450] == squad[1], "A partial duplicate must retain the complete player's fields.");
Console.WriteLine("PASS duplicate squad rows retain complete player metadata and team isolation");

var cutoff = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
var home = new IntelligenceTeamDto(teamId, "Home");
var fixture = new IntelligenceFixtureDto(1498781, cutoff.AddHours(2), "NS", "League", 1, 2026,
    home, new IntelligenceTeamDto(456, "Away"));
var fact = new FootballNewsFact
{
    FactHash = "injury-5450",
    TeamNameExtracted = home.Name,
    EvidenceSnippet = "Player confirmed unavailable",
    ExtractionModel = "structured",
    PromptVersion = "test",
    TeamId = teamId,
    PlayerId = 5450,
    PlayerNameExtracted = "Known player",
    EventType = FootballNewsEventType.Injury,
    AvailabilityStatus = AvailabilityStatus.ConfirmedOut,
    EffectiveConfidence = 1m,
    FirstSeenAtUtc = cutoff.AddMinutes(-10),
    IsCurrent = true
};
var builder = new FootballIntelligenceSnapshotBuilder(Options.Create(new FootballIntelligenceOptions()));
var snapshot = builder.Build(fixture, home, true, cutoff, [], [fact], squad, null);
var reference = builder.Build(fixture, home, true, cutoff, [], [fact], [squad[1]], null);
Require(snapshot.ActionableFactCount == 1, "Repeated squad entries must not multiply an injury.");
Require(snapshot.AttackAvailabilityImpact == reference.AttackAvailabilityImpact
    && snapshot.AttackAvailabilityImpact > 0m,
    "Duplicate-player snapshot must preserve the same attack impact as a unique complete row.");
Require(snapshot.GoalkeeperAvailabilityImpact == 0m, "A foreign squad must not change the player's position.");
Console.WriteLine("PASS fixture 1498781 snapshot accepts duplicate player 5450 with unchanged injury impact");

var future = builder.Build(fixture, home, true, cutoff, [],
    [fact with { FirstSeenAtUtc = cutoff.AddMinutes(1) }], squad, null);
Require(future.ActionableFactCount == 0 && future.AttackAvailabilityImpact == 0m,
    "Duplicate normalization must retain the pre-match evidence cutoff.");
Console.WriteLine("PASS pre-match cutoff still excludes future evidence");

foreach (var (input, historicalName) in new[]
{
    ("Hearts", "Heart of Midlothian"),
    ("Dundee FC", "Dundee"),
    ("Vicenza", "L.R. Vicenza"),
    ("Vicenza", "Vicenza Virtus"),
    ("Boyaca Chico", "Chico"),
    ("Jaguares de Cordoba", "Jaguares")
})
{
    Require(TeamNameMatcher.AreEquivalent(input, historicalName), $"Missing exact provider alias: {input}.");
    Require(TeamNameMatcher.GetKnownNameVariants(input).Contains(historicalName),
        $"SQL lookup must include the literal stored name {historicalName}.");
}
Require(!TeamNameMatcher.AreEquivalent("Dundee FC", "Dundee United"), "Dundee clubs must stay separate.");
Require(!TeamNameMatcher.AreEquivalent("Hearts", "Hearts of Oak"), "Different Hearts clubs must stay separate.");
Require(!TeamNameMatcher.AreEquivalent("Vicenza", "Vicenza U19"), "Youth teams must stay separate.");
Require(TeamNameMatcher.GetKnownNameVariants("Unlisted FC").SequenceEqual(new[] { "Unlisted FC" }),
    "Unknown names must not gain heuristic SQL aliases.");
Console.WriteLine("PASS exact provider aliases preserve club and youth-team isolation");

if (args.Contains("--sql"))
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, ".env"))) root = root.Parent;
    var connectionString = File.ReadLines(Path.Combine(root!.FullName, ".env"))
        .First(line => line.StartsWith("AZURE_SQL_CONNECTION_STRING=", StringComparison.Ordinal))
        .Split('=', 2)[1].Trim().Trim('"', '\'');
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = connectionString
    }).Build();
    var repository = new SqlServerMatchHistoryRepository(configuration, NullLogger<SqlServerMatchHistoryRepository>.Instance);
    var context = new GetPredictionContextUseCase(repository);
    var date = new DateOnly(2026, 9, 6);
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var scotland = await context.GetAsync("Hearts", "Dundee FC", "Scotland - Premiership", "M", null, date, deadline.Token);
    var italy = await context.GetAsync("Virtus Entella", "Vicenza", "Italy - Serie B", "M", null, date, deadline.Token);
    Require(scotland.HomeGeneralMatches.Count >= 5 && scotland.AwayGeneralMatches.Count >= 5,
        "Both Scottish provider aliases must resolve real pre-match history.");
    Require(italy.AwayGeneralMatches.Count >= 5,
        "A sparse direct Vicenza match must not hide the existing L.R. Vicenza history.");
    foreach (var match in scotland.HomeGeneralMatches.Concat(scotland.AwayGeneralMatches).Concat(italy.AwayGeneralMatches))
        Require(match.MatchDate < date, "Resolved alias history must not include the prediction date or future matches.");
    Console.WriteLine($"PASS live SQL contexts: Hearts={scotland.HomeGeneralMatches.Count}, Dundee={scotland.AwayGeneralMatches.Count}, Vicenza={italy.AwayGeneralMatches.Count}; all before {date:yyyy-MM-dd}");
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync(deadline.Token);
    await VerifyBotGCandidateWrite(connection);
    await VerifyTeamCandidateCache(connection, repository);
}

static async Task VerifyBotGCandidateWrite(SqlConnection connection)
{
    await connection.ExecuteAsync("""
        CREATE TABLE #Candidate (
            CandidateId BIGINT IDENTITY PRIMARY KEY,
            IdempotencyKey NVARCHAR(64) NOT NULL UNIQUE,
            SelectedOdds DECIMAL(10,4) NOT NULL,
            FeatureSnapshotJson NVARCHAR(MAX) NOT NULL,
            PublicationStatus NVARCHAR(20) NOT NULL);
        """);
    await connection.ExecuteAsync("""
        CREATE PROCEDURE #UpsertCandidate
            @IdempotencyKey NVARCHAR(64), @SelectedOdds DECIMAL(10,4),
            @FeatureSnapshotJson NVARCHAR(MAX), @PublicationStatus NVARCHAR(20)
        AS
        BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS(SELECT 1 FROM #Candidate WHERE IdempotencyKey=@IdempotencyKey)
                INSERT #Candidate(IdempotencyKey,SelectedOdds,FeatureSnapshotJson,PublicationStatus)
                VALUES(@IdempotencyKey,@SelectedOdds,@FeatureSnapshotJson,@PublicationStatus);
            ELSE
                UPDATE #Candidate SET PublicationStatus=@PublicationStatus WHERE IdempotencyKey=@IdempotencyKey;
            SELECT CandidateId FROM #Candidate WHERE IdempotencyKey=@IdempotencyKey;
        END;
        """);
    var build = typeof(SqlServerBotGRepository).GetMethod("BuildUpsertAndReadSql", BindingFlags.NonPublic | BindingFlags.Static)!;
    const string snapshot = "{\"evidence\":\"literal ' ; SELECT 123; --\"}";
    var parameters = new DynamicParameters();
    parameters.Add("IdempotencyKey", "same-evidence");
    parameters.Add("SelectedOdds", 2.1345m);
    parameters.Add("FeatureSnapshotJson", snapshot);
    parameters.Add("PublicationStatus", "Shadow");
    var sql = ((string)build.Invoke(null, [parameters.ParameterNames])!)
        .Replace("dbo.sp_UpsertBotG2026Candidate", "#UpsertCandidate", StringComparison.Ordinal)
        .Replace("dbo.vw_BotG2026Candidates", "#Candidate", StringComparison.Ordinal);
    var inserted = await connection.QuerySingleAsync<CandidateWriteResult>(sql, parameters);
    Require(inserted.CandidateId > 0 && inserted.SelectedOdds == 2.1345m
        && inserted.FeatureSnapshotJson == snapshot && inserted.PublicationStatus == "Shadow",
        "Combined write/read must return every stored field and preserve literal JSON and decimal precision.");
    parameters.Add("SelectedOdds", 9.9999m);
    parameters.Add("FeatureSnapshotJson", "{\"changed\":true}");
    parameters.Add("PublicationStatus", "Published");
    var retried = await connection.QuerySingleAsync<CandidateWriteResult>(sql, parameters);
    Require(retried.CandidateId == inserted.CandidateId && retried.SelectedOdds == inserted.SelectedOdds
        && retried.FeatureSnapshotJson == snapshot && retried.PublicationStatus == "Published",
        "Idempotent retries must return existing immutable evidence and updated publication metadata.");
    Console.WriteLine("PASS Bot G combines write/read in one request and returns persisted evidence on retries");

    var before = System.Diagnostics.Stopwatch.StartNew();
    for (var index = 0; index < 5; index++)
    {
        var id = await connection.QuerySingleAsync<long>("#UpsertCandidate", parameters,
            commandType: System.Data.CommandType.StoredProcedure);
        _ = await connection.QuerySingleAsync<CandidateWriteResult>(
            "SELECT * FROM #Candidate WHERE CandidateId=@Id", new { Id = id });
    }
    before.Stop();
    var after = System.Diagnostics.Stopwatch.StartNew();
    for (var index = 0; index < 5; index++)
        _ = await connection.QuerySingleAsync<CandidateWriteResult>(sql, parameters);
    after.Stop();
    Console.WriteLine($"Bot G temporary-row round-trip comparison: 5 writes/readbacks before={before.ElapsedMilliseconds}ms, combined={after.ElapsedMilliseconds}ms");
}

static async Task VerifyTeamCandidateCache(SqlConnection connection, SqlServerMatchHistoryRepository repository)
{
    var type = typeof(SqlServerMatchHistoryRepository);
    var get = type.GetMethod("GetCachedTeamNameCandidatesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var cache = (IDictionary)type.GetField("TeamNameCandidateCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    // Populate the same all-league cache used by unresolved provider names once.
    var load = (Task)get.Invoke(repository, [connection, null, "M", CancellationToken.None])!;
    await load;
    var loaded = load.GetType().GetProperty("Result")!.GetValue(load);
    Require(loaded is Array { Length: > 0 }, "The SQL fixture must supply candidate team names.");
    await using var unavailable = new SqlConnection();
    var cached = (Task)get.Invoke(repository, [unavailable, null, "M", CancellationToken.None])!;
    await cached;
    Require(ReferenceEquals(loaded, cached.GetType().GetProperty("Result")!.GetValue(cached)),
        "The fallback must reuse its cached result without needing a SQL connection.");
    await ExpectCacheMiss("F", null);
    await ExpectCacheMiss("M", "Uncached test league");
    var entry = cache["M|*"]!;
    var expired = Activator.CreateInstance(entry.GetType(), loaded, DateTime.UtcNow.AddSeconds(-1))!;
    cache["M|*"] = expired;
    try { await ExpectCacheMiss("M", null); }
    finally { cache["M|*"] = entry; }
    Console.WriteLine("PASS team fallback cache avoids a repeated SQL query and isolates gender, league and expiry");

    async Task ExpectCacheMiss(string gender, string? league)
    {
        try
        {
            await (Task)get.Invoke(repository, [unavailable, league, gender, CancellationToken.None])!;
        }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A different or expired cache key must require SQL, which is unavailable in this assertion.");
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class CandidateWriteResult
{
    public long CandidateId { get; set; }
    public decimal SelectedOdds { get; set; }
    public string FeatureSnapshotJson { get; set; } = "";
    public string PublicationStatus { get; set; } = "";
}
