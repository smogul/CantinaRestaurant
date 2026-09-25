using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Data;

// Recalculates stored rating stats from the Ratings table, so the result is right whatever happened before.
public static class RatingStats
{
    // The average is stored already rounded to one decimal, the precision the API returns, so no second rounding can shift it.
    public static Task RecalculateAsync(CantinaDbContext db, Guid menuItemId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            UPDATE "MenuItems" AS m
            SET "RatingCount" = s.rating_count, "AverageRating" = s.average_rating
            FROM (
                SELECT count(*)::int AS rating_count, round(avg("Stars"), 1) AS average_rating
                FROM "Ratings"
                WHERE "MenuItemId" = {menuItemId}
            ) AS s
            WHERE m."Id" = {menuItemId}
            """,
            cancellationToken);

    public static Task RecalculateAllAsync(CantinaDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "MenuItems" AS m
            SET "RatingCount" = s.rating_count, "AverageRating" = s.average_rating
            FROM (
                SELECT "MenuItemId", count(*)::int AS rating_count, round(avg("Stars"), 1) AS average_rating
                FROM "Ratings"
                GROUP BY "MenuItemId"
            ) AS s
            WHERE m."Id" = s."MenuItemId"
            """,
            cancellationToken);
}
