using CantinaApi.Data.Entities;

namespace CantinaApi.Common.Caching;

// Cached menu data is identical for every authenticated caller; user-specific data must never be cached this way.
public static class CacheKeys
{
    public const string MenuItemsTag = "menu-items";

    public static string MenuItemTag(Guid id) => $"menu-item:{id}";

    public static string RatingsTag(Guid menuItemId) => $"ratings:{menuItemId}";

    // Every input that changes a result is part of its key, with the free-text query last so it cannot blur the other parts.
    public static string MenuList(MenuItemType? type, PageRequest page) =>
        $"menu-items:list:type={TypePart(type)}:page={page.Page}:size={page.PageSize}";

    public static string MenuSearch(string normalizedQuery, MenuItemType? type, PageRequest page) =>
        $"menu-items:search:type={TypePart(type)}:page={page.Page}:size={page.PageSize}:q={normalizedQuery}";

    public static string MenuItem(Guid id) => $"menu-items:view:{id}";

    public static string Ratings(Guid menuItemId, PageRequest page) =>
        $"ratings:{menuItemId}:page={page.Page}:size={page.PageSize}";

    private static string TypePart(MenuItemType? type) => type?.ToString() ?? "all";
}
