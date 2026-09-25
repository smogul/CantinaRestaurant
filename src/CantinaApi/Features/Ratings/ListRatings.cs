using CantinaApi.Common;
using CantinaApi.Common.Caching;
using CantinaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace CantinaApi.Features.Ratings;

public static class ListRatings
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}/ratings", HandleAsync)
            .WithName(nameof(ListRatings))
            .WithSummary("List a menu item's ratings, newest first.");

    private static async Task<Results<Ok<PagedResponse<RatingResponse>>, ProblemHttpResult>> HandleAsync(
        Guid id,
        [AsParameters] PageRequest page,
        IDbContextFactory<CantinaDbContext> dbFactory,
        HybridCache cache,
        IOptions<CachingOptions> caching,
        CancellationToken cancellationToken)
    {
        var ratings = await cache.GetOrLoadIfFoundAsync(
            CacheKeys.Ratings(id, page),
            async token => await LoadAsync(dbFactory, id, page, token),
            caching.Value.MenuEntry,
            [CacheKeys.RatingsTag(id)],
            cancellationToken);

        return ratings is null ? Problems.NotFound("Menu item", id) : TypedResults.Ok(ratings);
    }

    // The factory builds its own context because its result may be shared with other requests waiting on the same key.
    private static async Task<PagedResponse<RatingResponse>?> LoadAsync(
        IDbContextFactory<CantinaDbContext> dbFactory, Guid id, PageRequest page, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (!await db.MenuItems.AnyAsync(m => m.Id == id, cancellationToken))
        {
            return null;
        }

        return await db.Ratings
            .AsNoTracking()
            .Where(r => r.MenuItemId == id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Select(RatingMappings.ToResponse)
            .ToPagedResponseAsync(page, cancellationToken);
    }
}
