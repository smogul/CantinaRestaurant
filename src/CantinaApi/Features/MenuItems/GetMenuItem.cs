using CantinaApi.Common;
using CantinaApi.Common.Caching;
using CantinaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace CantinaApi.Features.MenuItems;

public static class GetMenuItem
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName(nameof(GetMenuItem))
            .WithSummary("View a menu item with its rating summary.");

    private static async Task<Results<Ok<MenuItemDetailsResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        IDbContextFactory<CantinaDbContext> dbFactory,
        HybridCache cache,
        IOptions<CachingOptions> caching,
        CancellationToken cancellationToken)
    {
        var item = await cache.GetOrLoadIfFoundAsync(
            CacheKeys.MenuItem(id),
            async token => await LoadAsync(dbFactory, id, token),
            caching.Value.MenuEntry,
            [CacheKeys.MenuItemsTag, CacheKeys.MenuItemTag(id)],
            cancellationToken);

        return item is null ? Problems.NotFound("Menu item", id) : TypedResults.Ok(item);
    }

    // Reads the stored stats, which are already rounded to one decimal, instead of aggregating ratings on every request.
    private static async Task<MenuItemDetailsResponse?> LoadAsync(
        IDbContextFactory<CantinaDbContext> dbFactory, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.MenuItems
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new MenuItemDetailsResponse(
                m.Id,
                m.Name,
                m.Description,
                m.Price,
                m.ImageUrl,
                m.Type,
                (double?)m.AverageRating,
                m.RatingCount,
                m.CreatedAtUtc,
                m.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
