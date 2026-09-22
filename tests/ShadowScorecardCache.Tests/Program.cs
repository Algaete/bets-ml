using CornersPrediction.Application.Automation.BotH;
using CornersPredictionApi.Controllers;
using CornersPredictionApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;

var tests = new (string Name, Func<Task> Run)[]
{
    ("concurrent readers share one fill and the original cutoff", ConcurrentReads),
    ("a cancelled browser does not cancel or duplicate the fill", CancelledBrowser),
    ("a failed fill is shared but never cached", FailedFill),
    ("application shutdown cancels shared work without caching", Shutdown),
    ("bot, configuration, live and explicit cutoffs remain isolated", DistinctKeys),
    ("two-minute expiry preserves the data cutoff until a fresh read", Expiry),
    ("an already cancelled caller cannot start a read", AlreadyCancelled),
    ("controller fills retain their own DI scope after disconnect", ControllerScope)
};
foreach (var (name, run) in tests)
{
    await run().WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} shadow scorecard cache tests passed.");

static ShadowScorecardKey Key(string bot = "H2026", string version = "v1", DateTime? cutoff = null) => new(bot, version, cutoff);
static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static async Task Throws<T>(Task task) where T : Exception
{
    try { await task; }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static async Task ConcurrentReads()
{
    using var cache = new ShadowScorecardCache<Snapshot>();
    var release = Gate<Snapshot>();
    var calls = 0;
    Task<Snapshot> Read(CancellationToken _) { Interlocked.Increment(ref calls); return release.Task; }
    var readers = Enumerable.Range(0, 20).Select(_ => cache.GetAsync(Key(), Read, default)).ToArray();
    Check(calls == 1, "Concurrent tabs must run only one SQL aggregate.");
    var expected = new Snapshot(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));
    release.SetResult(expected);
    var values = await Task.WhenAll(readers);
    Check(values.All(value => ReferenceEquals(value, expected)), "All callers must receive the unchanged DTO and cutoff.");
    Check(ReferenceEquals(await cache.GetAsync(Key(), Read, default), expected) && calls == 1, "A cache hit must avoid SQL.");
}

static async Task CancelledBrowser()
{
    using var cache = new ShadowScorecardCache<Snapshot>();
    using var browser = new CancellationTokenSource();
    var release = Gate<Snapshot>();
    var calls = 0;
    CancellationToken fillToken = default;
    Task<Snapshot> Read(CancellationToken token) { calls++; fillToken = token; return release.Task; }
    var first = cache.GetAsync(Key(), Read, browser.Token);
    browser.Cancel();
    await Throws<OperationCanceledException>(first);
    Check(!fillToken.IsCancellationRequested, "Browser cancellation must not reach the repository.");
    var retry = cache.GetAsync(Key(), Read, default);
    Check(calls == 1, "A retry must join the original fill even if every earlier tab disconnected.");
    release.SetResult(new Snapshot(DateTime.UtcNow));
    await retry;
    await cache.GetAsync(Key(), Read, default);
    Check(calls == 1, "The detached successful fill must populate the cache.");
}

static async Task FailedFill()
{
    using var cache = new ShadowScorecardCache<Snapshot>();
    var release = Gate<Snapshot>();
    var calls = 0;
    Task<Snapshot> Read(CancellationToken _) => ++calls == 1 ? release.Task : Task.FromResult(new Snapshot(DateTime.UtcNow));
    var first = cache.GetAsync(Key(), Read, default);
    var second = cache.GetAsync(Key(), Read, default);
    release.SetException(new InvalidOperationException("SQL unavailable"));
    await Throws<InvalidOperationException>(first);
    await Throws<InvalidOperationException>(second);
    Check(calls == 1, "Both callers must observe the same failure.");
    await cache.GetAsync(Key(), Read, default);
    Check(calls == 2, "A failure must be retried, never replaced with a cached empty or zero DTO.");
}

static async Task Shutdown()
{
    using var cache = new ShadowScorecardCache<Snapshot>();
    using var stopping = new CancellationTokenSource();
    var abandoned = Gate<Snapshot>();
    var first = cache.GetAsync(Key(), _ => abandoned.Task, default, stopping.Token);
    stopping.Cancel();
    await Throws<OperationCanceledException>(first);
    var expected = new Snapshot(DateTime.UtcNow);
    Check(ReferenceEquals(await cache.GetAsync(Key(), _ => Task.FromResult(expected), default), expected),
        "Shutdown cancellation must remove pending work and leave no cached value.");
    abandoned.SetResult(new Snapshot(DateTime.UtcNow.AddDays(-1)));
}

static async Task DistinctKeys()
{
    using var cache = new ShadowScorecardCache<Snapshot>();
    var calls = 0;
    Task<Snapshot> Read(CancellationToken _) { calls++; return Task.FromResult(new Snapshot(DateTime.UtcNow)); }
    var cutoff = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    foreach (var key in new[] { Key(), Key("I2026"), Key(version: "v2"), Key(cutoff: cutoff), Key(cutoff: cutoff.AddSeconds(1)) })
        await cache.GetAsync(key, Read, default);
    Check(calls == 5, "Different bots, configurations and exact as-of instants must never share results.");
}

static async Task Expiry()
{
    var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero) };
    using var memory = new MemoryCache(new MemoryCacheOptions { Clock = clock, SizeLimit = 128 });
    using var cache = new ShadowScorecardCache<Snapshot>(memory);
    var calls = 0;
    Task<Snapshot> Read(CancellationToken _) { calls++; return Task.FromResult(new Snapshot(clock.UtcNow.UtcDateTime)); }
    var first = await cache.GetAsync(Key(), Read, default);
    clock.UtcNow = clock.UtcNow.AddSeconds(119);
    var cached = await cache.GetAsync(Key(), Read, default);
    Check(calls == 1 && cached.AsOfUtc == first.AsOfUtc, "A cached DTO must keep its original cutoff, not appear current.");
    clock.UtcNow = clock.UtcNow.AddSeconds(2);
    var fresh = await cache.GetAsync(Key(), Read, default);
    Check(calls == 2 && fresh.AsOfUtc > first.AsOfUtc, "Expired live results must reload after two minutes.");
}

static async Task AlreadyCancelled()
{
    using var cache = new ShadowScorecardCache<Snapshot>();
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    var calls = 0;
    try { await cache.GetAsync(Key(), _ => { calls++; return Task.FromResult(new Snapshot(DateTime.UtcNow)); }, cancelled.Token); }
    catch (OperationCanceledException) { }
    Check(calls == 0, "An already cancelled request must not enqueue work.");
}

static async Task ControllerScope()
{
    var repository = new ScopedRepository();
    var services = new ServiceCollection().AddScoped<IBotHShadowLabReadRepository>(_ => repository);
    await using var provider = services.BuildServiceProvider();
    using var cache = new ShadowScorecardCache<IReadOnlyList<BotHShadowScorecardDto>>();
    var controller = new BotH2026Controller(repository, NullLogger<BotH2026Controller>.Instance,
        cache, provider.GetRequiredService<IServiceScopeFactory>());
    using var browser = new CancellationTokenSource();
    var first = controller.GetScorecards(null, "v1", browser.Token);
    await repository.Started.Task;
    var cutoff = repository.Cutoff;
    browser.Cancel();
    Check(await first is StatusCodeResult { StatusCode: 499 }, "Disconnected caller should finish without marking shared fill as a failure.");
    Check(!repository.Disposed, "The independent read scope must survive the original request.");
    var second = controller.GetScorecards(null, "v1", default);
    repository.Release.SetResult([new BotHShadowScorecardDto { DateToUtc = cutoff!.Value }]);
    var response = await second as OkObjectResult;
    var rows = response?.Value as IReadOnlyList<BotHShadowScorecardDto>;
    Check(repository.Calls == 1 && repository.Disposed, "The fill must have one repository read and dispose its scope on completion.");
    Check(rows?.Single().DateToUtc == cutoff, "The actual query cutoff must remain in the returned DTO.");
}

sealed record Snapshot(DateTime AsOfUtc);
sealed class TestClock : ISystemClock { public DateTimeOffset UtcNow { get; set; } }
sealed class ScopedRepository : IBotHShadowLabReadRepository, IDisposable
{
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<IReadOnlyList<BotHShadowScorecardDto>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Disposed { get; private set; }
    public int Calls { get; private set; }
    public DateTime? Cutoff { get; private set; }
    public Task<IReadOnlyList<BotHShadowScorecardDto>> GetScorecardsAsync(BotHShadowScorecardFilter filter, CancellationToken token)
    {
        Calls++;
        Cutoff = filter.AsOfUtc;
        Started.SetResult(true);
        return Release.Task.WaitAsync(token);
    }
    public void Dispose() => Disposed = true;
    public Task<BotHShadowLabStatusDto> GetStatusAsync(CancellationToken token) => throw new NotSupportedException();
    public Task<BotHShadowEvaluationPage> GetEvaluationsAsync(BotHShadowEvaluationFilter filter, CancellationToken token) => throw new NotSupportedException();
    public Task<IReadOnlyList<BotHThresholdAnalysisDto>> GetThresholdAnalysisAsync(BotHThresholdAnalysisFilter filter, CancellationToken token) => throw new NotSupportedException();
}
