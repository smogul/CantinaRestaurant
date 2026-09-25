using CantinaApi.Common;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Features.Ratings;

public static class CreateRating
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{id:guid}/ratings", HandleAsync)
            .WithName(nameof(CreateRating))
            .WithSummary("Rate a menu item from 1 to 5 stars.");

    private static async Task<Results<Created<RatingResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, CreateRatingRequest request, CantinaDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        // The query filter makes deleted items look missing, so they cannot be rated.
        if (!await db.MenuItems.AnyAsync(m => m.Id == id, cancellationToken))
        {
            return Problems.NotFound("Menu item", id);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rating = new Rating
        {
            Id = Guid.CreateVersion7(),
            MenuItemId = id,
            Stars = request.Stars!.Value,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Ratings.Add(rating);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/menu-items/{id}/ratings", rating.ToResponseDto());
    }
}
