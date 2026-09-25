using CantinaApi.Common;
using CantinaApi.Data;
using CantinaApi.Data.Configurations;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CantinaApi.Features.MenuItems;

// Rules shared by create and update.
internal static class MenuItemRules
{
    public static void Apply(this MenuItem item, MenuItemRequest request)
    {
        item.Name = request.Name.Trim();
        item.Description = request.Description.Trim();

        // Rounded the same way numeric(10,2) stores it, so the response matches the database.
        item.Price = Math.Round(request.Price, 2, MidpointRounding.AwayFromZero);
        item.ImageUrl = request.ImageUrl;
        item.Type = request.Type!.Value;
    }

    // The pre-check keeps ordinary conflicts out of the error logs; the unique index still catches races.
    public static async Task<bool> TrySaveAsync(CantinaDbContext db, MenuItem item, CancellationToken cancellationToken)
    {
        var lowerName = item.Name.ToLowerInvariant();
        var nameTaken = await db.MenuItems.AnyAsync(
            m => m.Id != item.Id && m.Type == item.Type && m.Name.ToLower() == lowerName,
            cancellationToken);

        if (nameTaken)
        {
            return false;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: MenuItemConfiguration.UniqueNameIndex,
        })
        {
            return false;
        }
    }

    public static ProblemHttpResult DuplicateName(MenuItem item) =>
        Problems.Conflict($"A {item.Type.ToString().ToLowerInvariant()} named '{item.Name}' already exists.");
}
