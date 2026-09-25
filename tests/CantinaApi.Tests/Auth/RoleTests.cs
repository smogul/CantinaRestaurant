using System.Net;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class RoleTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Customer_CannotManageMenuItems()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var create = await CustomerClient.PostJsonAsync(MenuItemsRoute, new MenuItemRequestBuilder().Build(), CancellationToken);
        var update = await CustomerClient.PutJsonAsync(MenuItemRoute(item.Id), new MenuItemRequestBuilder().Build(), CancellationToken);
        var delete = await CustomerClient.DeleteAsync(MenuItemRoute(item.Id), CancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Forbidden, CancellationToken);
        await update.AssertProblemAsync(HttpStatusCode.Forbidden, CancellationToken);
        await delete.AssertProblemAsync(HttpStatusCode.Forbidden, CancellationToken);
    }

    [Fact]
    public async Task Admin_CanManageMenuItems()
    {
        var create = await AdminClient.PostJsonAsync(MenuItemsRoute, new MenuItemRequestBuilder().Build(), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var item = await create.ReadJsonAsync<CantinaApi.Features.MenuItems.MenuItemResponse>(CancellationToken);

        var update = await AdminClient.PutJsonAsync(MenuItemRoute(item.Id), new MenuItemRequestBuilder().Build(), CancellationToken);
        var delete = await AdminClient.DeleteAsync(MenuItemRoute(item.Id), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Customer_CanReadMenuAndRatings()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Blue Milk").Build(), CancellationToken);

        foreach (var url in new[] { MenuItemsRoute, $"{MenuItemsRoute}/search?q=milk", MenuItemRoute(item.Id), RatingsRoute(item.Id) })
        {
            var response = await CustomerClient.GetAsync(url, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Admin_CannotRateMenuItems()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var response = await AdminClient.PostJsonAsync(RatingsRoute(item.Id), new CreateRatingRequestBuilder().Build(), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Forbidden, CancellationToken);
    }
}
