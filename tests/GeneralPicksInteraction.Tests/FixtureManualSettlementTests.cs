using System.Data;
using System.Text.RegularExpressions;
using System.Transactions;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Infrastructure.SqlServer;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

internal static class FixtureManualSettlementTests
{
    public static async Task Run(IConfiguration configuration, string connectionString, string migration)
    {
        // Only the additive, idempotent schema remains. Every fixture, result and
        // correction in this integration test is synthetic and rolled back.
        foreach (var batch in Regex.Split(migration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch))
            {
                Console.WriteLine("Fixture test: applying schema batch.");
                await using var schemaConnection = new SqlConnection(connectionString);
                await schemaConnection.ExecuteAsync(new CommandDefinition(batch, commandTimeout: 300));
            }
        var writer = new SqlServerGeneralPickManualSettlementRepository(configuration);
        var reader = new SqlServerAutomatedBotResearchRepository(configuration);
        var tag = Guid.NewGuid().ToString("N")[..12];
        var league = "Manual test " + tag;
        var botA = "TEST_A_" + tag;
        var botC = "TEST_C_" + tag;
        var botF = "TEST_F_" + tag;
        var day = DateTime.Today.AddDays(-3).AddHours(12);
        var fixture = 8_000_000_000L + Random.Shared.Next(1_000_000_000);
        var request = new GeneralPickManualSettlementRequest(1, "Rollback test " + tag, Guid.NewGuid(), true);
        using (var scope = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
                Timeout = TimeSpan.FromMinutes(5) }, TransactionScopeAsyncFlowOption.Enabled))
        {
            var first = await SeedEvaluation(botA, "AwayTeamGoals", "Over", .75m, 1.9m, fixture, day);
            var second = await SeedEvaluation(botC, "AwayTeamGoals", "Under", 1.25m, 2.1m, fixture, day);
            var withoutId = await SeedEvaluation(botF, "AwayTeamGoals", "Over", 1.5m, 1.9m, null, day);
            var corners = await SeedEvaluation(botA, "AwayTeamCorners", "Over", .75m, 1.9m, fixture, day);
            var homeGoals = await SeedEvaluation(botA, "HomeTeamGoals", "Over", .75m, 1.9m, fixture, day);
            var nextDay = await SeedEvaluation(botA, "AwayTeamGoals", "Over", .75m, 1.9m, fixture + 1, day.AddDays(1));
            var published = await SeedPublished(botC, "Under", 1.25m, 2.1m, 2m, fixture, day);
            var publishedZero = await SeedPublished(botA, "Over", 1.5m, 1.9m, 0m, fixture, day);
            await Execute("UPDATE dbo.AutomatedBotPickEvaluations SET PublishedSelectionId=@Published WHERE AutomatedBotPickEvaluationId=@Evaluation",
                new { Published = published, Evaluation = second });
            Console.WriteLine("Fixture test: synthetic rows created; checking preview.");
            var preview = await writer.PreviewAsync(first, default);
            Check(preview.Evaluations == 3 && preview.PublishedPicks == 2 && preview.BotKeys.Length == 3,
                "Preview did not include all bots, sides and lines, or included another fixture/market.");
            var before = await Audit(first);
            Console.WriteLine("Fixture test: preview confirmed; applying shared result.");
            var saved = await writer.SettleAsync(first, request, "fixture-integration-test", default);
            var retry = await writer.SettleAsync(first, request, "fixture-integration-test", default);
            Check(saved == retry && saved.AppliedToFixture && saved.AffectedBots == 3, "Retry was not idempotent.");
            Check(await Scalar<int>("SELECT COUNT(*) FROM dbo.GeneralBotFixtureManualSettlements WHERE RequestId=@RequestId", request) == 1,
                "Duplicate operation audit.");
            Check(await Audit(first) == before, "Manual settlement modified immutable model evidence.");
            foreach (var (id, actual) in new[] { (first, (int?)1), (second, 1), (withoutId, 1), (corners, null), (homeGoals, null), (nextDay, null) })
                Check(await SharedActual(id) == actual, $"Incorrect shared result for evaluation {id}.");
            Check(await Scalar<decimal>("SELECT ProfitLoss FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id", new { Id = published }) == 1.1m,
                "Published quarter-line half-win did not use its own odds and stake.");
            Check(await Scalar<decimal>("SELECT ProfitLoss FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id", new { Id = publishedZero }) == 0,
                "Zero-stake pick gained economic exposure.");
            var query = new AutomatedBotResearchQuery(day.Date, day.Date.AddDays(1), "GOALS", "AwayTeamGoals", botA, null, null, 1, 100);
            var page = await reader.GetGeneralPicksAsync(query, default);
            var row = page.Items.Single(r => r.EvaluationId == first);
            Check(row.OutcomeSource == "Manual" && row.OutcomeStatus == "HalfWin" && row.ProfitLoss == .45m,
                "General Picks did not independently calculate the shared half-win.");
            var publishedRow = page.Items.Single(r => r.PublishedSelectionId == publishedZero);
            Check(publishedRow.ManualSettledBy == "fixture-integration-test", "Published row lost the shared audit actor.");
            var linkedPage = await reader.GetGeneralPicksAsync(query with { BotKey = botC }, default);
            var linked = linkedPage.Items.Single(r => r.EvaluationId == second);
            Check(linked.OutcomeSource == "Manual" && linked.ManualSettledBy == "fixture-integration-test"
                && linked.ProfitLoss == .55m, "Linked audit lost the actor or per-unit outcome through timestamp rounding.");
            Console.WriteLine("PASS shared results cross bots/lines/sides and preserve per-pick odds/stake; other markets and dates stay untouched.");

            var late = await SeedEvaluation(botF, "AwayTeamGoals", "Under", 1m, 2m, fixture, day);
            Check(await SharedActual(late) == 1, "Later evaluation did not inherit the result.");
            var latePublished = await SeedPublished(botF, "Under", 1m, 2m, 1m, fixture, day);
            await Execute("EXEC dbo.sp_ApplyGeneralBotFixtureManualSettlement @NowLocal=@Now;", new { Now = DateTime.Now });
            Check(await Scalar<string>("SELECT Status FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id", new { Id = latePublished }) == "Push",
                "Later published pick did not inherit the shared result before official reconciliation.");
            var correction = request with { RequestId = Guid.NewGuid(), ActualValue = 0, Reason = "Corrected source " + tag };
            await writer.SettleAsync(second, correction, "fixture-integration-test", default);
            Check(await SharedActual(first) == 0 && await SharedActual(late) == 0, "Correction/zero did not propagate.");
            Check(await Scalar<decimal>("SELECT ProfitLoss FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id", new { Id = published }) == 2.2m,
                "Correction did not recalculate published economic return.");

            var productionWriter = new SqlServerAutomatedCornerSelectionsRepository(configuration);
            var productionResult = await productionWriter.ResolveAsync(published, 2, "production-integration-test", default);
            Check(productionResult.Status == "Lost" && productionResult.ProfitLoss == -2m,
                "Productivo response lost its original per-pick odds/stake contract.");
            Check(await SharedActual(first) == 2 && await SharedActual(withoutId) == 2 && await SharedActual(late) == 2,
                "Productivo still settled only the selected bot instead of its fixture and market.");
            Check(await Scalar<string>("SELECT TOP(1) SettledBy FROM dbo.GeneralBotFixtureManualSettlements WHERE RecordId=@Id ORDER BY Id DESC", new { Id = -published }) == "production-integration-test",
                "Productivo did not preserve the authenticated operator in the shared audit.");

            // Regression for Sabadell–Oviedo: no bot has an API-Football id.
            // One published pick must settle the other published bot and the
            // unpublished model signals using the exact event identity.
            var missingIdDay = day.AddHours(1);
            var missingIdA = await SeedEvaluation(botA, "AwayTeamGoals", "Over", .5m, 1.65m, null, missingIdDay);
            var missingIdF = await SeedEvaluation(botF, "AwayTeamGoals", "Over", .5m, 1.67m, null, missingIdDay);
            var missingIdUnder = await SeedEvaluation(botC, "AwayTeamGoals", "Under", 1.5m, 1.21m, null, missingIdDay);
            var missingIdPubA = await SeedPublished(botA, "Over", .5m, 1.65m, 1m, null, missingIdDay);
            var missingIdPubF = await SeedPublished(botF, "Over", .5m, 1.67m, .5m, null, missingIdDay);
            await productionWriter.ResolveAsync(missingIdPubA, 1, "production-integration-test", default);
            foreach (var id in new[] { missingIdA, missingIdF, missingIdUnder })
                Check(await SharedActual(id) == 1, "A bot without a fixture id did not inherit the productive result.");
            Check(await Scalar<string>("SELECT Status FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id", new { Id = missingIdPubF }) == "Won",
                "F remained pending after another bot settled the same away-goals pick.");
            Check(await Scalar<decimal>("SELECT ProfitLoss FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id", new { Id = missingIdPubF }) == .34m,
                "F did not calculate its own rounded profit from the shared actual value.");
            Console.WriteLine("PASS Productivo shares manual results with General Picks and other published bots, including events without a fixture id.");
            await Reject(() => writer.SettleAsync(first, request with { ActualValue = 2 }, "fixture-integration-test", default));
            var future = await SeedEvaluation(botA, "AwayTeamGoals", "Over", .5m, 1.9m, fixture + 2, DateTime.Today.AddDays(2));
            await Reject(() => writer.SettleAsync(future, correction with { RequestId = Guid.NewGuid() }, "fixture-integration-test", default));
            // A second known id with identical names/kickoff must fail, never silently merge.
            await SeedEvaluation(botA, "AwayTeamGoals", "Over", .5m, 1.9m, fixture + 3, day);
            await Reject(() => writer.PreviewAsync(withoutId, default));
            Console.WriteLine("PASS retries, correction history, zero, future/ambiguous rejection and late-arriving picks.");
            // No Complete(): restore all test rows and updates.
        }
        Check(await Scalar<int>("SELECT COUNT(*) FROM dbo.GeneralBotFixtureManualSettlements WHERE RequestId=@RequestId", request) == 0,
            "Integration test left a manual result behind.");
        Console.WriteLine("PASS all synthetic fixture data and settlements rolled back.");

        async Task<long> SeedEvaluation(string bot, string market, string side, decimal line, decimal odds, long? id, DateTime date) => await Scalar<long>("""
            INSERT dbo.AutomatedBotPickEvaluations
                (IdempotencyKey,RunId,BotKey,AutomationVersion,PartidoProximoCuotaId,ApiFootballFixtureId,
                 MatchDate,League,HomeTeam,AwayTeam,Source,SourceMarketType,MarketType,LineValue,SelectedSide,SelectedOdds,
                 DecisionEngineType,Decision,FeatureSchemaVersion,ConfigurationVersion,DecisionReasonsJson,RiskFlagsJson,Explanation,FeatureSnapshotJson)
            VALUES (REPLACE(CONVERT(VARCHAR(36),NEWID()),'-','')+REPLACE(CONVERT(VARCHAR(36),NEWID()),'-',''),NEWID(),@Bot,'manual-test',0,@Id,
                @Date,@League,'Home test','Away test','Test','Test',@Market,@Line,@Side,@Odds,
                'MODELS_2026','Approved','test','test','[]','[]','synthetic rollback test','{}');
            SELECT CONVERT(BIGINT,SCOPE_IDENTITY());
            """, new { Bot = bot, Market = market, Side = side, Line = line, Odds = odds, Id = id, Date = date, League = league });
        async Task<long> SeedPublished(string bot, string side, decimal line, decimal odds, decimal stake, long? id, DateTime date) => await Scalar<long>("""
            INSERT dbo.AutomatedCornerBetSelections
                (RunId,BotKey,LogicalPickKey,AutomationVersion,Source,ApiFootballFixtureId,MatchDate,
                 League,HomeTeam,AwayTeam,StandardizedLeague,StandardizedHomeTeam,StandardizedAwayTeam,
                 MarketType,LineValue,SelectedSide,Odds,Stake)
            VALUES (NEWID(),@Bot,HASHBYTES('SHA2_256',CONVERT(VARCHAR(36),NEWID())),CONCAT('manual-test-',@Bot),'Test',@Id,@Date,
                @League,'Home test','Away test',@League,'Home test','Away test','AwayTeamGoals',@Line,@Side,@Odds,@Stake);
            SELECT CONVERT(BIGINT,SCOPE_IDENTITY());
            """, new { Bot = bot, Side = side, Line = line, Odds = odds, Stake = stake, Id = id, Date = date, League = league });
        async Task<int?> SharedActual(long id) => await Scalar<int?>("""
            SELECT m.ActualValue FROM dbo.AutomatedBotPickEvaluations AS e
            OUTER APPLY dbo.fn_GeneralBotFixtureManualOutcome(e.ApiFootballFixtureId,e.MatchDate,e.League,e.HomeTeam,e.AwayTeam,e.MarketType) AS m
            WHERE e.AutomatedBotPickEvaluationId=@Id;
            """, new { Id = id });
        async Task<string> Audit(long id) => await Scalar<string>("SELECT * FROM dbo.AutomatedBotPickEvaluations WHERE AutomatedBotPickEvaluationId=@Id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER", new { Id = id });
        async Task<T> Scalar<T>(string sql, object? args = null)
        {
            await using var connection = new SqlConnection(connectionString);
            return (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, args, commandTimeout: 90)))!;
        }
        async Task Execute(string sql, object? args = null)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(new CommandDefinition(sql, args, commandTimeout: 90));
        }
    }

    private static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid settlement to be rejected.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
