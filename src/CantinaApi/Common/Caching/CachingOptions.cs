using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Caching.Hybrid;

namespace CantinaApi.Common.Caching;

public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    // Lifetime of cached list, view and ratings pages.
    [Range(1, 24 * 60 * 60)]
    public int MenuSeconds { get; init; } = 60;

    // Search results expire sooner because their keys are far more varied.
    [Range(1, 24 * 60 * 60)]
    public int SearchSeconds { get; init; } = 30;

    public HybridCacheEntryOptions MenuEntry => ForSeconds(MenuSeconds);

    public HybridCacheEntryOptions SearchEntry => ForSeconds(SearchSeconds);

    private static HybridCacheEntryOptions ForSeconds(int seconds) => new()
    {
        Expiration = TimeSpan.FromSeconds(seconds),
        LocalCacheExpiration = TimeSpan.FromSeconds(seconds),
    };
}
