using CantinaApi.Common;
using CantinaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Features.MenuItems;

public static class GetMenuItem
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName(nameof(GetMenuItem))
            .WithSummary("View a menu item with its rating summary.");

    private static async Task<Results<Ok<MenuItemDetailsResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, CantinaDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.MenuItems
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new MenuItemDetailsResponse(
                m.Id,
                m.Name,
                m.Description,
                m.Price,
                m.ImageUrl,
                m.Type,
                m.Ratings.Average(r => (double?)r.Stars),
                m.Ratings.Count,
                m.CreatedAtUtc,
                m.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return Problems.NotFound("Menu item", id);
        }

        return TypedResults.Ok(item with { AverageRating = RoundRating(item.AverageRating) });
    }

    private static double? RoundRating(double? average) =>
        average is { } value ? Math.Round(value, 1, MidpointRounding.AwayFromZero) : null;
}
