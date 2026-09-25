using CantinaApi.Common;
using CantinaApi.Data.Entities;
using CantinaApi.Features.MenuItems;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.MenuItems;

[Collection(nameof(ApiCollection))]
public sealed class ListMenuItemsTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task List_PaginatesWithCorrectTotals()
    {
        foreach (var name in new[] { "Echo", "Alpha", "Delta", "Charlie", "Bravo" })
        {
            await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName(name).Build(), CancellationToken);
        }

        var first = await GetPageAsync($"{MenuItemsRoute}?page=1&pageSize=2");
        var last = await GetPageAsync($"{MenuItemsRoute}?page=3&pageSize=2");
        var beyond = await GetPageAsync($"{MenuItemsRoute}?page=4&pageSize=2");

        Assert.Equal(["Alpha", "Bravo"], first.Items.Select(item => item.Name));
        Assert.Equal((1, 2, 5, 3), (first.Page, first.PageSize, first.TotalCount, first.TotalPages));
        Assert.Equal(["Echo"], last.Items.Select(item => item.Name));
        Assert.Equal((3, 5, 3), (last.Page, last.TotalCount, last.TotalPages));
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public async Task List_UsesDefaultPagingAndOrdersByName()
    {
        await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Mynock Wings").Build(), CancellationToken);
        await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Gorg Skewers").Build(), CancellationToken);

        var page = await GetPageAsync(MenuItemsRoute);

        Assert.Equal((1, PageRequest.DefaultPageSize, 2, 1), (page.Page, page.PageSize, page.TotalCount, page.TotalPages));
        Assert.Equal(["Gorg Skewers", "Mynock Wings"], page.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task List_FiltersByType()
    {
        await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Bantha Burger").WithType(MenuItemType.Dish).Build(), CancellationToken);
        await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Blue Milk").WithType(MenuItemType.Drink).Build(), CancellationToken);

        var drinks = await GetPageAsync($"{MenuItemsRoute}?type=Drink");

        Assert.Equal(["Blue Milk"], drinks.Items.Select(item => item.Name));
        Assert.Equal(1, drinks.TotalCount);
    }

    [Fact]
    public async Task List_WhenEmpty_ReturnsZeroPages()
    {
        var page = await GetPageAsync(MenuItemsRoute);

        Assert.Empty(page.Items);
        Assert.Equal((0, 0), (page.TotalCount, page.TotalPages));
    }

    private async Task<PagedResponse<MenuItemResponse>> GetPageAsync(string url) =>
        await (await AdminClient.GetAsync(url, CancellationToken)).ReadJsonAsync<PagedResponse<MenuItemResponse>>(CancellationToken);
}
