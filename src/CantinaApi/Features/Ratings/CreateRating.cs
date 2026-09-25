using System.Security.Claims;
using CantinaApi.Common;
using CantinaApi.Common.Auth;
using CantinaApi.Common.Caching;
using CantinaApi.Data;
using CantinaApi.Data.Configurations;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
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
        HybridCache cache,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Locking the item row makes rating writes for one item take turns, so each stats recalculation sees every earlier rating.
        // It also doubles as the existence check; the soft-delete condition makes deleted items look missing.
        var itemExists = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "MenuItems" WHERE "Id" = {id} AND NOT "IsDeleted" FOR UPDATE""")
            .AnyAsync(cancellationToken);

        if (!itemExists)
        {
            return Problems.NotFound("Menu item", id);
        }

        var userId = user.GetUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stars = request.Stars!.Value;
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

        var rating = await db.Ratings.FirstOrDefaultAsync(r => r.MenuItemId == id && r.UserId == userId, cancellationToken);
        var isNew = rating is null;

        if (rating is null)
        {
            rating = new Rating { Id = Guid.CreateVersion7(), MenuItemId = id, UserId = userId, CreatedAtUtc = now };
            db.Ratings.Add(rating);
        }

        Apply(rating, stars, comment, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (isNew && ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RatingConfiguration.UniqueRaterIndex,
        })
        {
            // Unreachable while the row lock holds, kept as a safety net; EF rolls back to its savepoint, so the transaction stays usable.
            db.Entry(rating).State = EntityState.Detached;
            rating = await db.Ratings.FirstAsync(r => r.MenuItemId == id && r.UserId == userId, cancellationToken);
            Apply(rating, stars, comment, now);
            await db.SaveChangesAsync(cancellationToken);
            isNew = false;
        }

        await RatingStats.RecalculateAsync(db, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Runs after commit so no request can re-cache the old view or ratings page in between.
        await cache.RemoveByTagAsync([CacheKeys.MenuItemTag(id), CacheKeys.RatingsTag(id)], cancellationToken);

        var response = await db.Ratings
            .AsNoTracking()
            .Where(r => r.Id == rating.Id)
            .Select(RatingMappings.ToResponse)
            .SingleAsync(cancellationToken);

        return isNew
            ? TypedResults.Created($"/api/menu-items/{id}/ratings", response)
            : TypedResults.Ok(response);
    }

    private static void Apply(Rating rating, int stars, string? comment, DateTime now)
    {
        rating.Stars = stars;
        rating.Comment = comment;
        rating.UpdatedAtUtc = now;
    }
}
