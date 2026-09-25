using Microsoft.Extensions.Caching.Hybrid;
using Serilog.Context;

namespace CantinaApi.Common.Caching;

public static class HybridCacheExtensions
{
    // HybridCache runs the factory outside the caller's async context, so the caller's log properties, such as the correlation id, are carried in explicitly.
    public static ValueTask<T> GetOrLoadAsync<T>(
        this HybridCache cache,
        string key,
        Func<CancellationToken, Task<T>> load,
        HybridCacheEntryOptions options,
        IEnumerable<string> tags,
        CancellationToken cancellationToken)
    {
        var callerLogContext = LogContext.Clone();

        return cache.GetOrCreateAsync(
            key,
            async token =>
            {
                using (LogContext.Push(callerLogContext))
                {
                    return await load(token);
                }
            },
            options,
            tags,
            cancellationToken);
    }

    // HybridCache stores whatever the factory returns, including null, and has no flag to skip a result once it is known.
    // A factory that throws is never cached and its failure is shared with concurrent callers, so a missing row is signalled that way.
    // This keeps random ids from filling memory while still letting one database lookup serve every waiting request.
    public static async Task<T?> GetOrLoadIfFoundAsync<T>(
        this HybridCache cache,
        string key,
        Func<CancellationToken, Task<T?>> load,
        HybridCacheEntryOptions options,
        IEnumerable<string> tags,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await cache.GetOrLoadAsync(
                key,
                async token => await load(token) ?? throw new NotFoundException(),
                options,
                tags,
                cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    private sealed class NotFoundException : Exception;
}
