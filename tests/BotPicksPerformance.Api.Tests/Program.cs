using CornersPrediction.Application.AutomatedCorners;
using CornersPredictionApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Concurrent scorecards share one computation across controllers", ConcurrentReadersShareComputation),
    ("Canceling a queued reader preserves the running computation", CanceledWaiterPreservesGate),
    ("A canceled owner releases the gate and allows a fresh retry", CanceledOwnerAllowsRetry),
    ("A failed computation is not cached and releases the gate", FailedOwnerAllowsRetry)
};
foreach (var (name, run) in tests)
{
    await run().WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"Bot Picks performance API tests: {tests.Length}/{tests.Length} passed.");

static AutomatedCornersController Controller(IAutomatedBotPerformanceService service, IMemoryCache cache) =>
    new(null!, null!, null!, null!, null!, null!, service, cache, null!, null!,
        NullLogger<AutomatedCornersController>.Instance);

static async Task ConcurrentReadersShareComputation()
{
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var completion = new TaskCompletionSource<IReadOnlyList<AutomatedBotPerformanceScorecard>>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var service = new StubPerformanceService((_, _) => completion.Task);
    var requests = Enumerable.Range(0, 25).Select(_ => Controller(service, cache).GetPerformanceScorecards()).ToArray();
    Require(service.Calls == 1, "Concurrent requests duplicated the expensive computation.");
    IReadOnlyList<AutomatedBotPerformanceScorecard> cards = [new() { WindowDays = 30, Segment = "Exact cohort" }];
    completion.SetResult(cards);
    foreach (var result in await Task.WhenAll(requests))
        Require(result is OkObjectResult ok && ReferenceEquals(ok.Value, cards), "Readers did not receive the shared result.");
    await Controller(service, cache).GetPerformanceScorecards();
    Require(service.Calls == 1, "A cached request recomputed scorecards.");
}

static async Task CanceledWaiterPreservesGate()
{
    using var cache = new MemoryCache(new MemoryCacheOptions());
    using var cancellation = new CancellationTokenSource();
    var completion = new TaskCompletionSource<IReadOnlyList<AutomatedBotPerformanceScorecard>>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var service = new StubPerformanceService((_, _) => completion.Task);
    var owner = Controller(service, cache).GetPerformanceScorecards();
    var waiter = Controller(service, cache).GetPerformanceScorecards(cancellation.Token);
    cancellation.Cancel();
    await MustCancel(waiter);
    var survivor = Controller(service, cache).GetPerformanceScorecards();
    Require(service.Calls == 1 && !survivor.IsCompleted, "Canceling a waiter incorrectly released the owner's gate.");
    completion.SetResult([]);
    await Task.WhenAll(owner, survivor);
    Require(service.Calls == 1, "The surviving reader recomputed the result.");
}

static async Task CanceledOwnerAllowsRetry()
{
    using var cache = new MemoryCache(new MemoryCacheOptions());
    using var cancellation = new CancellationTokenSource();
    var service = new StubPerformanceService(async (call, token) =>
    {
        if (call == 1) await Task.Delay(Timeout.Infinite, token);
        return [];
    });
    var owner = Controller(service, cache).GetPerformanceScorecards(cancellation.Token);
    var survivor = Controller(service, cache).GetPerformanceScorecards();
    cancellation.Cancel();
    await MustCancel(owner);
    Require(await survivor is OkObjectResult && service.Calls == 2, "Owner cancellation blocked a healthy retry.");
}

static async Task FailedOwnerAllowsRetry()
{
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var service = new StubPerformanceService((call, _) => call == 1
        ? Task.FromException<IReadOnlyList<AutomatedBotPerformanceScorecard>>(new InvalidOperationException("Expected backend failure"))
        : Task.FromResult<IReadOnlyList<AutomatedBotPerformanceScorecard>>([]));
    Require(await Controller(service, cache).GetPerformanceScorecards() is ObjectResult { StatusCode: 500 },
        "A backend failure must remain an error.");
    Require(await Controller(service, cache).GetPerformanceScorecards() is OkObjectResult && service.Calls == 2,
        "A failed calculation was cached or left the gate held.");
}

static async Task MustCancel(Task task)
{
    try { await task; }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Request cancellation was not honored.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class StubPerformanceService(Func<int, CancellationToken, Task<IReadOnlyList<AutomatedBotPerformanceScorecard>>> load)
    : IAutomatedBotPerformanceService
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public Task<IReadOnlyList<AutomatedBotPerformanceScorecard>> GetScorecardsAsync(CancellationToken cancellationToken) =>
        load(Interlocked.Increment(ref _calls), cancellationToken);
}
