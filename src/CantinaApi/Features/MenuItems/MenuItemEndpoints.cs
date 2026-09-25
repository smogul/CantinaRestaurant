using CantinaApi.Features.Ratings;

namespace CantinaApi.Features.MenuItems;

public static class MenuItemEndpoints
{
    public static IEndpointRouteBuilder MapMenuItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/menu-items").WithTags("Menu items");

        CreateMenuItem.Map(group);
        ListMenuItems.Map(group);
        SearchMenuItems.Map(group);
        GetMenuItem.Map(group);
        UpdateMenuItem.Map(group);
        DeleteMenuItem.Map(group);
        group.MapRatingEndpoints();

        return app;
    }
}
