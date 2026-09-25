using System.ComponentModel.DataAnnotations;
using CantinaApi.Common;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Features.MenuItems;

public static class ListMenuItems
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", HandleAsync)
            .WithName(nameof(ListMenuItems))
            .WithSummary("List menu items by name, optionally filtered by type.");

    private static async Task<PagedResponse<MenuItemResponse>> HandleAsync(
        [AsParameters] PageRequest page,
        [EnumDataType(typeof(MenuItemType))] MenuItemType? type,
        CantinaDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.MenuItems.AsNoTracking();
        if (type is not null)
        {
            query = query.Where(m => m.Type == type);
        }

        return await query
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Id)
            .Select(MenuItemMappings.ToResponse)
            .ToPagedResponseAsync(page, cancellationToken);
    }
}
