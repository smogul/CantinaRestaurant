using CantinaApi.Common;
using CantinaApi.Common.Auth;
using CantinaApi.Common.Caching;
using CantinaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace CantinaApi.Features.MenuItems;

public static class UpdateMenuItem
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}", HandleAsync)
            .WithName(nameof(UpdateMenuItem))
            .WithSummary("Replace all fields of a menu item.")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<Ok<MenuItemResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, MenuItemRequest request, CantinaDbContext db, HybridCache cache, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (item is null)
        {
            return Problems.NotFound("Menu item", id);
        }

        item.Apply(request);
        item.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (!await MenuItemRules.TrySaveAsync(db, item, cancellationToken))
        {
            return MenuItemRules.DuplicateName(item);
        }

        // Runs after the update has committed; the view entry carries this tag too, so it is refreshed as well.
        await cache.RemoveByTagAsync(CacheKeys.MenuItemsTag, cancellationToken);

        return TypedResults.Ok(item.ToResponseDto());
    }
}
