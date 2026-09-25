using System.Security.Claims;
using CantinaApi.Common;
using CantinaApi.Common.Auth;
using CantinaApi.Data;
using CantinaApi.Data.Configurations;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CantinaApi.Features.Ratings;

public static class CreateRating
{
    // Staff must not rate their own menu, so only customers can rate.
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{id:guid}/ratings", HandleAsync)
            .WithName(nameof(CreateRating))
            .WithSummary("Rate a menu item from 1 to 5 stars; rating it again updates your rating.")
            .RequireAuthorization(Policies.CustomerOnly);

    private static async Task<Results<Created<RatingResponse>, Ok<RatingResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        CreateRatingRequest request,
        ClaimsPrincipal user,
        CantinaDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // The query filter makes deleted items look missing, so they cannot be rated.
        if (!await db.MenuItems.AnyAsync(m => m.Id == id, cancellationToken))
        {
            return Problems.NotFound("Menu item", id);
        }

        var userId = user.GetUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stars = request.Stars!.Value;
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

        var existing = await db.Ratings.FirstOrDefaultAsync(r => r.MenuItemId == id && r.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            return TypedResults.Ok(await UpdateAsync(db, existing, stars, comment, now, cancellationToken));
        }

        var rating = new Rating
        {
            Id = Guid.CreateVersion7(),
            MenuItemId = id,
            UserId = userId,
            Stars = stars,
            Comment = comment,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Ratings.Add(rating);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RatingConfiguration.UniqueRaterIndex,
        })
        {
            // A concurrent request from the same customer won the insert, so this one becomes an update.
            db.Entry(rating).State = EntityState.Detached;
            var winner = await db.Ratings.FirstAsync(r => r.MenuItemId == id && r.UserId == userId, cancellationToken);
            return TypedResults.Ok(await UpdateAsync(db, winner, stars, comment, now, cancellationToken));
        }

        return TypedResults.Created($"/api/menu-items/{id}/ratings", await LoadResponseAsync(db, rating.Id, cancellationToken));
    }

    private static async Task<RatingResponse> UpdateAsync(
        CantinaDbContext db, Rating rating, int stars, string? comment, DateTime now, CancellationToken cancellationToken)
    {
        rating.Stars = stars;
        rating.Comment = comment;
        rating.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        return await LoadResponseAsync(db, rating.Id, cancellationToken);
    }

    // Read back through the shared projection so the reviewer name matches the ratings list.
    private static Task<RatingResponse> LoadResponseAsync(CantinaDbContext db, Guid ratingId, CancellationToken cancellationToken) =>
        db.Ratings
            .AsNoTracking()
            .Where(r => r.Id == ratingId)
            .Select(RatingMappings.ToResponse)
            .SingleAsync(cancellationToken);
}
