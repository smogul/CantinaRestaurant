using System.Net;
using CantinaApi.Common;
using CantinaApi.Data.Entities;
using CantinaApi.Features.MenuItems;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.MenuItems;

[Collection(nameof(ApiCollection))]
public sealed class MenuItemCrudTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Create_ReturnsCreatedWithLocationAndItem()
    {
        var request = new MenuItemRequestBuilder()
            .WithName("Bantha Burger")
            .WithDescription("Smoked bantha patty on a toasted bun.")
            .WithPrice(14.50m)
            .WithImageUrl("https://images.example.com/bantha-burger.png")
            .WithType(MenuItemType.Dish)
            .Build();

        var response = await Client.PostJsonAsync(MenuItemsRoute, request, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await response.ReadJsonAsync<MenuItemResponse>(CancellationToken);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(MenuItemRoute(item.Id), response.Headers.Location?.AbsolutePath);
        Assert.Equal("Bantha Burger", item.Name);
        Assert.Equal("Smoked bantha patty on a toasted bun.", item.Description);
        Assert.Equal(14.50m, item.Price);
        Assert.Equal("https://images.example.com/bantha-burger.png", item.ImageUrl);
        Assert.Equal(MenuItemType.Dish, item.Type);
        Assert.Equal(item.CreatedAtUtc, item.UpdatedAtUtc);
    }

    [Fact]
    public async Task Get_ReturnsItemWithEmptyRatingSummary()
    {
        var created = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithType(MenuItemType.Drink).Build(), CancellationToken);

        var response = await Client.GetAsync(MenuItemRoute(created.Id), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.ReadJsonAsync<MenuItemDetailsResponse>(CancellationToken);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(created.Name, item.Name);
        Assert.Equal(MenuItemType.Drink, item.Type);
        Assert.Null(item.AverageRating);
        Assert.Equal(0, item.RatingCount);
    }

    [Fact]
    public async Task Update_ReplacesAllFieldsAndSetsUpdatedAt()
    {
        var created = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        var update = new MenuItemRequestBuilder()
            .WithName("Blue Milk")
            .WithDescription("Chilled bantha milk.")
            .WithPrice(4.50m)
            .WithImageUrl("https://images.example.com/blue-milk.png")
            .WithType(MenuItemType.Drink)
            .Build();

        var response = await Client.PutJsonAsync(MenuItemRoute(created.Id), update, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<MenuItemResponse>(CancellationToken);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(update.Name, updated.Name);
        Assert.Equal(update.Description, updated.Description);
        Assert.Equal(update.Price, updated.Price);
        Assert.Equal(update.ImageUrl, updated.ImageUrl);
        Assert.Equal(MenuItemType.Drink, updated.Type);
        Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.True(updated.UpdatedAtUtc > created.UpdatedAtUtc);

        var viewed = await (await Client.GetAsync(MenuItemRoute(created.Id), CancellationToken))
            .ReadJsonAsync<MenuItemDetailsResponse>(CancellationToken);
        Assert.Equal("Blue Milk", viewed.Name);
    }

    [Fact]
    public async Task Delete_SoftDeletesItemAndKeepsItsRatings()
    {
        var deleted = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Nerf Steak").Build(), CancellationToken);
        var kept = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Ronto Wrap").Build(), CancellationToken);
        await Client.RateAsync(deleted.Id, new CreateRatingRequestBuilder().WithStars(5).Build(), CancellationToken);
        await Client.RateAsync(deleted.Id, new CreateRatingRequestBuilder().WithStars(3).Build(), CancellationToken);

        var response = await Client.DeleteAsync(MenuItemRoute(deleted.Id), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var view = await Client.GetAsync(MenuItemRoute(deleted.Id), CancellationToken);
        await view.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);

        var list = await (await Client.GetAsync(MenuItemsRoute, CancellationToken))
            .ReadJsonAsync<PagedResponse<MenuItemResponse>>(CancellationToken);
        Assert.Equal([kept.Id], list.Items.Select(item => item.Id));

        var search = await (await Client.GetAsync($"{MenuItemsRoute}/search?q=nerf", CancellationToken))
            .ReadJsonAsync<PagedResponse<MenuItemResponse>>(CancellationToken);
        Assert.Empty(search.Items);

        var (isDeleted, ratingCount) = await QueryDatabaseAsync(async db => (
            await db.MenuItems.IgnoreQueryFilters().Where(m => m.Id == deleted.Id).Select(m => m.IsDeleted).SingleAsync(CancellationToken),
            await db.Ratings.IgnoreQueryFilters().CountAsync(r => r.MenuItemId == deleted.Id, CancellationToken)));
        Assert.True(isDeleted);
        Assert.Equal(2, ratingCount);
    }

    [Fact]
    public async Task Delete_TwiceReturnsNotFound()
    {
        var created = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        await Client.DeleteAsync(MenuItemRoute(created.Id), CancellationToken);

        var response = await Client.DeleteAsync(MenuItemRoute(created.Id), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
    }

    [Theory]
    [InlineData("view")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("rate")]
    public async Task UnknownId_ReturnsNotFound(string operation)
    {
        var id = Guid.NewGuid();

        var response = operation switch
        {
            "view" => await Client.GetAsync(MenuItemRoute(id), CancellationToken),
            "update" => await Client.PutJsonAsync(MenuItemRoute(id), new MenuItemRequestBuilder().Build(), CancellationToken),
            "delete" => await Client.DeleteAsync(MenuItemRoute(id), CancellationToken),
            "rate" => await Client.PostJsonAsync(RatingsRoute(id), new CreateRatingRequestBuilder().Build(), CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        await response.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
    }
}
