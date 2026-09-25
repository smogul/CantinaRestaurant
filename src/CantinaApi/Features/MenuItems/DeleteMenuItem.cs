using CantinaApi.Common;
using CantinaApi.Common.Auth;
using CantinaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Features.MenuItems;

public static class DeleteMenuItem
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/{id:guid}", HandleAsync)
            .WithName(nameof(DeleteMenuItem))
            .WithSummary("Soft delete a menu item; its ratings are kept.")
            .RequireAuthorization(Policies.AdminOnly);

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, CantinaDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The query filter skips already deleted items, so a second delete is a 404.
        var updated = await db.MenuItems
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.IsDeleted, true)
                    .SetProperty(m => m.UpdatedAtUtc, now),
                cancellationToken);

        return updated == 0 ? Problems.NotFound("Menu item", id) : TypedResults.NoContent();
    }
}
