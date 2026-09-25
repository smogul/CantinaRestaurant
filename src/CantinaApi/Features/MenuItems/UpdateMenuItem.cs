using CantinaApi.Common;
using CantinaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Features.MenuItems;

public static class UpdateMenuItem
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}", HandleAsync)
            .WithName(nameof(UpdateMenuItem))
            .WithSummary("Replace all fields of a menu item.");

    private static async Task<Results<Ok<MenuItemResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, MenuItemRequest request, CantinaDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
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

        return TypedResults.Ok(item.ToResponseDto());
    }
}
