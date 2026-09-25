using System.Net;
using CantinaApi.Data.Entities;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.MenuItems;

[Collection(nameof(ApiCollection))]
public sealed class MenuItemConflictTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Theory]
    [InlineData("Blue Milk")]
    [InlineData("BLUE MILK")]
    [InlineData("  blue milk  ")]
    public async Task Create_WithDuplicateNameInSameType_ReturnsConflict(string duplicateName)
    {
        await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Blue Milk").WithType(MenuItemType.Drink).Build(), CancellationToken);
        var duplicate = new MenuItemRequestBuilder().WithName(duplicateName).WithType(MenuItemType.Drink).Build();

        var response = await Client.PostJsonAsync(MenuItemsRoute, duplicate, CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, CancellationToken);
    }

    [Fact]
    public async Task Create_WithSameNameInOtherType_Succeeds()
    {
        await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Tatooine Sunset").WithType(MenuItemType.Drink).Build(), CancellationToken);
        var dish = new MenuItemRequestBuilder().WithName("Tatooine Sunset").WithType(MenuItemType.Dish).Build();

        var response = await Client.PostJsonAsync(MenuItemsRoute, dish, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNameOfDeletedItem_Succeeds()
    {
        var original = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Ronto Wrap").Build(), CancellationToken);
        await Client.DeleteAsync(MenuItemRoute(original.Id), CancellationToken);

        var response = await Client.PostJsonAsync(MenuItemsRoute, new MenuItemRequestBuilder().WithName("Ronto Wrap").Build(), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_ToNameTakenInSameType_ReturnsConflict()
    {
        await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Bantha Burger").Build(), CancellationToken);
        var other = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Nerf Steak").Build(), CancellationToken);

        var response = await Client.PutJsonAsync(
            MenuItemRoute(other.Id), new MenuItemRequestBuilder().WithName("bantha burger").Build(), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, CancellationToken);
    }

    [Fact]
    public async Task Update_KeepingOwnName_Succeeds()
    {
        var item = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Dewback Ribs").Build(), CancellationToken);

        var response = await Client.PutJsonAsync(
            MenuItemRoute(item.Id), new MenuItemRequestBuilder().WithName("DEWBACK RIBS").WithPrice(21m).Build(), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
