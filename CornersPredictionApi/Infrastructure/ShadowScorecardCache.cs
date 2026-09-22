using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace CornersPredictionApi.Infrastructure;

public readonly record struct ShadowScorecardKey(string BotKey, string? ConfigurationVersion, DateTime? ExplicitAsOfUtc);

/// <summary>Shares successful, bounded reads across tabs without tying SQL to one browser's lifetime.</summary>
public sealed class ShadowScorecardCache<T> : IDisposable where T : class
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<ShadowScorecardKey, Lazy<Task<T>>> _pending = new();
    private readonly TimeSpan _fillTimeout;
    private readonly bool _ownsCache;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public ShadowScorecardCache(IMemoryCache? cache = null, TimeSpan? fillTimeout = null)
    {
        _ownsCache = cache is null;
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 128 });
        _fillTimeout = fillTimeout ?? TimeSpan.FromSeconds(120);
    }

    public Task<T> GetAsync(
        ShadowScorecardKey key,
        Func<CancellationToken, Task<T>> read,
        CancellationToken callerCancellation,
        CancellationToken applicationStopping = default)
    {
        callerCancellation.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return Task.FromResult(cached);

        var pending = _pending.GetOrAdd(key, _ => new Lazy<Task<T>>(
            () => FillAsync(key, read, applicationStopping), LazyThreadSafetyMode.ExecutionAndPublication));
        return pending.Value.WaitAsync(callerCancellation);
    }

    private async Task<T> FillAsync(
        ShadowScorecardKey key,
        Func<CancellationToken, Task<T>> read,
        CancellationToken applicationStopping)
    {
        try
        {
            // A previous fill can finish between the caller's cache lookup and GetOrAdd.
            if (_cache.TryGetValue(key, out T? cached) && cached is not null)
                return cached;
            using var fillCancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
            fillCancellation.CancelAfter(_fillTimeout);
            var value = await read(fillCancellation.Token).WaitAsync(fillCancellation.Token).ConfigureAwait(false);
            fillCancellation.Token.ThrowIfCancellationRequested();
            _cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Lifetime,
                Size = 1
            });
            return value;
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        if (_ownsCache)
            _cache.Dispose();
    }
}
