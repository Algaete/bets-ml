using System.Transactions;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Infrastructure.SqlServer;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

internal static class OfficialAliasSettlementTests
{
    public static async Task Run(IConfiguration configuration, string connectionString)
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var league = "Alias test " + tag;
        var bot = "TEST_ALIAS_" + tag;
        var home = "Home United " + tag;
        var away = "Away United " + tag;
        var shortHome = "Home " + tag;
        var shortAway = "Away " + tag;
        var ambiguousHome = "Ambiguous United " + tag;
        var shortAmbiguous = "Ambiguous " + tag;
        var outsideHome = "Outside United " + tag;
        var timedHome = "Timed United " + tag;
        var day = DateTime.UtcNow.Date.AddDays(-4).AddHours(12);
        var kickoffUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"));
        var fixture = 7_000_000_000L + Random.Shared.Next(1_000_000_000);
        var manualRequest = new GeneralPickManualSettlementRequest(0,
            "Synthetic rollback manual override " + tag, Guid.NewGuid());
        var reader = new SqlServerAutomatedBotResearchRepository(configuration);
        var writer = new SqlServerGeneralPickManualSettlementRepository(configuration);
        using (var scope = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted,
                Timeout = TimeSpan.FromMinutes(5) }, TransactionScopeAsyncFlowOption.Enabled))
        {
            await Alias(shortHome, home);
            await Alias(shortAway, away);
            await Alias(shortAmbiguous, ambiguousHome);
            await History(shortHome, shortAway, fixture, day);
            await History(shortAmbiguous, shortAway, fixture + 1, day);
            await History(ambiguousHome, away, fixture + 2, day);
            await History(outsideHome, shortAway, fixture + 3, day.AddDays(-10));
            await History(timedHome, shortAway, fixture + 4, day);

            var unique = await Evaluation(home, away, null);
            var ambiguous = await Evaluation(ambiguousHome, away, null);
            var outside = await Evaluation(outsideHome, away, null);
            var exact = await Evaluation(ambiguousHome, away, fixture + 1);
            var shortlyBefore = await Evaluation(timedHome, away, null, kickoffUtc.AddMinutes(-18));
            var afterKickoff = await Evaluation(timedHome, away, null, kickoffUtc.AddMinutes(5), 1.5m);
            var auditBefore = await Scalar<string>("SELECT * FROM dbo.AutomatedBotPickEvaluations WHERE AutomatedBotPickEvaluationId=@Id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER", new { Id = unique });
            var query = new AutomatedBotResearchQuery(day.Date, day.Date.AddDays(1), "GOALS",
                "AwayTeamGoals", bot, "Approved", null, 1, 100);

            foreach (var sort in new[] { "MatchDate", "OutcomeStatus" })
            {
                Console.WriteLine($"Alias test: reading General Picks by {sort}.");
                var page = await reader.GetGeneralPicksAsync(query with { SortBy = sort, SortDirection = "asc" }, default);
                Check(page.TotalCount == 6, "Unexpected synthetic fixture population.");
                foreach (var id in new[] { unique, exact, shortlyBefore })
                {
                    var row = page.Items.Single(row => row.EvaluationId == id);
                    Check(row.OutcomeSource == "ApiFootball" && row.OutcomeStatus == "Win"
                        && row.ActualValue == 1 && row.ProfitLoss == 1,
                        $"{sort}: canonical names or exact official id did not resolve.");
                }
                foreach (var id in new[] { ambiguous, outside, afterKickoff })
                {
                    var row = page.Items.Single(row => row.EvaluationId == id);
                    Check(row.OutcomeStatus == "Unavailable" && row.ActualValue is null,
                        $"{sort}: matched an ambiguous/out-of-date fixture or an after-kickoff prediction.");
                }
                if (sort == "OutcomeStatus")
                    Check(page.Items.Select(row => row.OutcomeStatus).SequenceEqual(
                        page.Items.Select(row => row.OutcomeStatus).Order(StringComparer.OrdinalIgnoreCase)),
                        "Global sorting disagrees with the displayed official outcomes.");
            }
            Console.WriteLine("Alias test: reading official outcomes in the lab.");
            var lab = await reader.GetLabAsync(query, default);
            Check(lab.Summary.IndependentSignals == 6 && lab.Summary.ResolvedSignals == 3
                && lab.Summary.UnavailableSignals == 3 && lab.Summary.ProfitLossUnits == 3,
                "The lab does not share the page's official alias/date/ambiguity resolver.");
            Console.WriteLine("PASS official alias fallback in General Picks, global outcome sorting and lab; exact ids win, ambiguous ids and other dates remain unavailable.");
            Console.WriteLine("PASS Santiago kickoff compared in UTC: legitimate prediction 18 minutes before kickoff resolves, after-kickoff prediction remains unavailable.");

            Console.WriteLine("Alias test: checking manual override and immutable evidence.");
            await writer.SettleAsync(unique, manualRequest, "alias-integration-test", default);
            var corrected = await reader.GetGeneralPicksAsync(query with { SortBy = "OutcomeStatus" }, default);
            var manual = corrected.Items.Single(row => row.EvaluationId == unique);
            Check(manual.OutcomeSource == "Manual" && manual.OutcomeStatus == "Loss" && manual.ActualValue == 0,
                "Official alias fallback replaced an explicit manual outcome.");
            var manualLab = await reader.GetLabAsync(query, default);
            Check(manualLab.Summary.ResolvedSignals == 3 && manualLab.Summary.ProfitLossUnits == 1,
                "The lab ignored manual outcome precedence.");
            Check(await Scalar<string>("SELECT * FROM dbo.AutomatedBotPickEvaluations WHERE AutomatedBotPickEvaluationId=@Id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER", new { Id = unique }) == auditBefore,
                "Official alias resolution or manual settlement changed immutable model evidence.");
            // Intentionally no Complete(): official rows, aliases and results
            // are synthetic; all writes are rolled back together.
        }
        Check(await Scalar<int>("SELECT COUNT(*) FROM dbo.MatchHistory WHERE League=@League", new { League = league }) == 0
            && await Scalar<int>("SELECT COUNT(*) FROM dbo.AutomatedBotPickEvaluations WHERE BotKey=@Bot", new { Bot = bot }) == 0
            && await Scalar<int>("SELECT COUNT(*) FROM dbo.TeamNameAlias WHERE CanonicalName=@Home", new { Home = home }) == 0
            && await Scalar<int>("SELECT COUNT(*) FROM dbo.GeneralBotPickManualSettlements WHERE RequestId=@RequestId", manualRequest) == 0,
            "Alias integration test left synthetic rows behind.");
        Console.WriteLine("PASS manual result precedence, immutable audit preservation and complete synthetic rollback.");

        async Task Alias(string alias, string canonical) => await Execute("""
            INSERT dbo.TeamNameAlias(AliasKey,CanonicalName)
            VALUES(dbo.fn_NormalizeNameKey(@Alias),@Canonical);
            """, new { Alias = alias, Canonical = canonical });

        async Task History(string historyHome, string historyAway, long id, DateTime date) => await Execute("""
            INSERT dbo.MatchHistory(League,Season,MatchDate,HomeTeam,AwayTeam,
                HomeGoals,AwayGoals,ApiFootballFixtureId,FixtureStatus,
                ApiFootballGoalsAvailable,ApiFootballUpdatedAtUtc)
            VALUES(@League,'test',@Date,@Home,@Away,2,1,@Id,'FT',1,@UpdatedAt);
            """, new { League = league, Date = date.Date, Home = historyHome, Away = historyAway,
                Id = id, UpdatedAt = kickoffUtc.AddHours(3) });

        async Task<long> Evaluation(string evaluationHome, string evaluationAway, long? id,
            DateTime? decisionAt = null, decimal line = .5m) => await Scalar<long>("""
            INSERT dbo.AutomatedBotPickEvaluations
                (IdempotencyKey,RunId,BotKey,AutomationVersion,PartidoProximoCuotaId,ApiFootballFixtureId,
                 MatchDate,League,HomeTeam,AwayTeam,Source,SourceMarketType,MarketType,LineValue,SelectedSide,SelectedOdds,
                 FinalProbability,PredictionTimestampUtc,EvaluatedAtUtc,
                 DecisionEngineType,Decision,FeatureSchemaVersion,ConfigurationVersion,DecisionReasonsJson,RiskFlagsJson,Explanation,FeatureSnapshotJson)
            VALUES (REPLACE(CONVERT(VARCHAR(36),NEWID()),'-','')+REPLACE(CONVERT(VARCHAR(36),NEWID()),'-',''),NEWID(),@Bot,'alias-test',0,@Id,
                @Date,@League,@Home,@Away,'Test','Test','AwayTeamGoals',@Line,'Over',2,
                0.6,@DecisionAt,@DecisionAt,'MODELS_2026','Approved','test','test','[]','[]','synthetic alias rollback test','{}');
            SELECT CONVERT(BIGINT,SCOPE_IDENTITY());
            """, new { Bot = bot, Id = id, Date = day, League = league, Home = evaluationHome,
                Away = evaluationAway, DecisionAt = decisionAt ?? day.AddHours(-1), Line = line });

        async Task<T> Scalar<T>(string sql, object parameters)
        {
            await using var connection = new SqlConnection(connectionString);
            return (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, commandTimeout: 120)))!;
        }

        async Task Execute(string sql, object parameters)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, commandTimeout: 120));
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
