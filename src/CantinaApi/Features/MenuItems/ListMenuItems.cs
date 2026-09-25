using System.ComponentModel.DataAnnotations;
using CantinaApi.Common;
using CantinaApi.Common.Caching;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

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
        IDbContextFactory<CantinaDbContext> dbFactory,
        HybridCache cache,
        IOptions<CachingOptions> caching,
        CancellationToken cancellationToken) =>
        await cache.GetOrLoadAsync(
            CacheKeys.MenuList(type, page),
            async token => await LoadAsync(dbFactory, type, page, token),
            caching.Value.MenuEntry,
            [CacheKeys.MenuItemsTag],
            cancellationToken);

    // The factory builds its own context because its result may be shared with other requests waiting on the same key.
    private static async Task<PagedResponse<MenuItemResponse>> LoadAsync(
        IDbContextFactory<CantinaDbContext> dbFactory, MenuItemType? type, PageRequest page, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

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
