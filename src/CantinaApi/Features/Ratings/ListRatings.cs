using CantinaApi.Common;
using CantinaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Features.Ratings;

public static class ListRatings
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}/ratings", HandleAsync)
            .WithName(nameof(ListRatings))
            .WithSummary("List a menu item's ratings, newest first.");

    private static async Task<Results<Ok<PagedResponse<RatingResponse>>, ProblemHttpResult>> HandleAsync(
        Guid id, [AsParameters] PageRequest page, CantinaDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.MenuItems.AnyAsync(m => m.Id == id, cancellationToken))
        {
            return Problems.NotFound("Menu item", id);
        }

        var ratings = await db.Ratings
            .AsNoTracking()
            .Where(r => r.MenuItemId == id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Select(RatingMappings.ToResponse)
            .ToPagedResponseAsync(page, cancellationToken);

        return TypedResults.Ok(ratings);
    }
}
