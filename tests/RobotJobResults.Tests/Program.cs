using System.Reflection;
using System.Text.Json;
using AutomatedCornersBot.Api;
using CornersPredictionApi.RecommendationJobs;

var method = typeof(RecommendationJobWorker).GetMethod("BuildBatchErrorSummary", BindingFlags.Static | BindingFlags.NonPublic)!;
string? Summary(int errors, params string[] reasons)
{
    var response = JsonSerializer.Deserialize<AutomatedRunResponse>(JsonSerializer.Serialize(new {
        ErrorMatches = errors,
        Errors = reasons.Select(reason => new { League = "Test", HomeTeam = "Local", AwayTeam = "Visita", MatchDate = DateTime.UtcNow, Error = reason })
    }))!;
    return (string?)method.Invoke(null, [response]);
}
void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
Check(Summary(0, "old error") is null, "A successful batch must not manufacture errors.");
var summary = Summary(2, "SQL contract mismatch", "SQL contract mismatch")!;
Check(summary == "Local vs Visita: SQL contract mismatch", "Preserve the actionable error and fixture without repeating duplicate lines.");
Check(Summary(1, new string('x', 5000))!.Length == 1900, "Diagnostics must fit the database column.");
Check(Summary(3)!.Contains("3 errores"), "Older responses without details must still report the failed count.");
Console.WriteLine("PASS job diagnostics preserve fixture and reason, deduplicate messages, bound storage and distinguish success");
SqlBatchSplitterTests.Run();
if (args.Contains("--sql")) await JobErrorSqlTests.RunAsync();
