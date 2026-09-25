using CantinaApi.Common.Auth;
using CantinaApi.Common.Caching;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Hybrid;

namespace CantinaApi.Features.MenuItems;

public static class CreateMenuItem
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", HandleAsync)
            .WithName(nameof(CreateMenuItem))
            .WithSummary("Create a menu item.")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<CreatedAtRoute<MenuItemResponse>, ProblemHttpResult>> HandleAsync(
        MenuItemRequest request, CantinaDbContext db, HybridCache cache, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var item = new MenuItem { Id = Guid.CreateVersion7(), CreatedAtUtc = now, UpdatedAtUtc = now };
        item.Apply(request);
        db.MenuItems.Add(item);

        if (!await MenuItemRules.TrySaveAsync(db, item, cancellationToken))
        {
            return MenuItemRules.DuplicateName(item);
        }

        // Runs after the insert has committed so no request can re-cache the old menu in between.
        await cache.RemoveByTagAsync(CacheKeys.MenuItemsTag, cancellationToken);

        return TypedResults.CreatedAtRoute(item.ToResponseDto(), nameof(GetMenuItem), new { id = item.Id });
    }
}
