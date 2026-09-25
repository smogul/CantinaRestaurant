using System.Net;
using CantinaApi.Common;
using CantinaApi.Data.Entities;
using CantinaApi.Features.MenuItems;
using CantinaApi.Features.Ratings;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.Performance;

[Collection(nameof(ApiCollection))]
public sealed class CachingTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task RepeatedReads_AreServedWithoutTouchingTheDatabase()
    {
        var item = await SeedRatedItemAsync("Blue Milk");

        foreach (var url in ReadUrls(item.Id))
        {
            var first = await CustomerClient.GetAsync(url, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var commandsBefore = DatabaseCommands.Count;
            var second = await CustomerClient.GetAsync(url, CancellationToken);

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(commandsBefore, DatabaseCommands.Count);
            Assert.Equal(
                await first.Content.ReadAsStringAsync(CancellationToken),
                await second.Content.ReadAsStringAsync(CancellationToken));
        }
    }

    [Fact]
    public async Task SearchesThatDifferOnlyInCaseOrSpacing_ShareOneEntry()
    {
        await SeedRatedItemAsync("Blue Milk");
        await CustomerClient.GetAsync($"{MenuItemsRoute}/search?q=milk", CancellationToken);

        var commandsBefore = DatabaseCommands.Count;
        var response = await CustomerClient.GetAsync($"{MenuItemsRoute}/search?q=%20%20MILK%20", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(commandsBefore, DatabaseCommands.Count);
    }

    [Theory]
    [InlineData("/api/menu-items?page=1&pageSize=20", "/api/menu-items?page=2&pageSize=20")]
    [InlineData("/api/menu-items?page=1&pageSize=20", "/api/menu-items?page=1&pageSize=10")]
    [InlineData("/api/menu-items?type=Dish", "/api/menu-items?type=Drink")]
    [InlineData("/api/menu-items", "/api/menu-items?type=Dish")]
    [InlineData("/api/menu-items/search?q=milk", "/api/menu-items/search?q=bantha")]
    [InlineData("/api/menu-items/search?q=milk", "/api/menu-items/search?q=milk&type=Drink")]
    [InlineData("/api/menu-items/search?q=milk", "/api/menu-items/search?q=milk&page=2")]
    public async Task DifferentInputs_UseDifferentEntries(string cachedUrl, string otherUrl)
    {
        await SeedRatedItemAsync("Blue Milk");
        await CustomerClient.GetAsync(cachedUrl, CancellationToken);

        var commandsBefore = DatabaseCommands.Count;
        var response = await CustomerClient.GetAsync(otherUrl, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(DatabaseCommands.Count > commandsBefore, $"{otherUrl} was answered from the entry cached for {cachedUrl}.");
    }

    [Fact]
    public async Task DifferentItemsAndRatingPages_UseDifferentEntries()
    {
        var first = await SeedRatedItemAsync("Blue Milk");
        var second = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Green Milk").Build(), CancellationToken);
        await CustomerClient.GetAsync(MenuItemRoute(first.Id), CancellationToken);
        await CustomerClient.GetAsync(RatingsRoute(first.Id), CancellationToken);

        foreach (var url in new[] { MenuItemRoute(second.Id), RatingsRoute(second.Id), $"{RatingsRoute(first.Id)}?page=2" })
        {
            var commandsBefore = DatabaseCommands.Count;
            var response = await CustomerClient.GetAsync(url, CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(DatabaseCommands.Count > commandsBefore, $"{url} was answered from another item's entry.");
        }
    }

    [Fact]
    public async Task CreatingAnItem_ShowsUpInTheNextListAndSearch()
    {
        await SeedRatedItemAsync("Blue Milk");
        await CustomerClient.GetAsync(MenuItemsRoute, CancellationToken);
        await CustomerClient.GetAsync($"{MenuItemsRoute}/search?q=milk", CancellationToken);

        var created = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName("Green Milk").Build(), CancellationToken);

        Assert.Contains(created.Id, (await GetPageAsync<MenuItemResponse>(MenuItemsRoute)).Items.Select(i => i.Id));
        Assert.Contains(created.Id, (await GetPageAsync<MenuItemResponse>($"{MenuItemsRoute}/search?q=milk")).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task UpdatingAnItem_ShowsTheNewValuesOnView()
    {
        var item = await SeedRatedItemAsync("Blue Milk");
        await CustomerClient.GetAsync(MenuItemRoute(item.Id), CancellationToken);
        await CustomerClient.GetAsync(MenuItemsRoute, CancellationToken);

        var update = new MenuItemRequestBuilder().WithName("Blue Milk Deluxe").WithPrice(7.25m).WithType(MenuItemType.Drink).Build();
        Assert.Equal(HttpStatusCode.OK, (await AdminClient.PutJsonAsync(MenuItemRoute(item.Id), update, CancellationToken)).StatusCode);

        var view = await GetAsync<MenuItemDetailsResponse>(MenuItemRoute(item.Id));
        Assert.Equal(("Blue Milk Deluxe", 7.25m, MenuItemType.Drink), (view.Name, view.Price, view.Type));
        Assert.Equal((5.0, 1), (view.AverageRating, view.RatingCount));
        Assert.Equal(["Blue Milk Deluxe"], (await GetPageAsync<MenuItemResponse>(MenuItemsRoute)).Items.Select(i => i.Name));
    }

    [Fact]
    public async Task DeletingAnItem_RemovesItFromEveryCachedRead()
    {
        var item = await SeedRatedItemAsync("Blue Milk");
        foreach (var url in ReadUrls(item.Id))
        {
            await CustomerClient.GetAsync(url, CancellationToken);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await AdminClient.DeleteAsync(MenuItemRoute(item.Id), CancellationToken)).StatusCode);

        await (await CustomerClient.GetAsync(MenuItemRoute(item.Id), CancellationToken)).AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
        await (await CustomerClient.GetAsync(RatingsRoute(item.Id), CancellationToken)).AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
        Assert.Empty((await GetPageAsync<MenuItemResponse>(MenuItemsRoute)).Items);
        Assert.Empty((await GetPageAsync<MenuItemResponse>($"{MenuItemsRoute}/search?q=milk")).Items);
    }

    [Fact]
    public async Task RatingAnItem_ShowsTheNewStatsAndRatingImmediately()
    {
        var item = await SeedRatedItemAsync("Blue Milk");
        await CustomerClient.GetAsync(MenuItemRoute(item.Id), CancellationToken);
        await CustomerClient.GetAsync(RatingsRoute(item.Id), CancellationToken);

        await CustomerClients[1].RateAsync(item.Id, new CreateRatingRequestBuilder().WithStars(2).WithComment("Too sweet.").Build(), CancellationToken);

        var afterCreate = await GetAsync<MenuItemDetailsResponse>(MenuItemRoute(item.Id));
        Assert.Equal((3.5, 2), (afterCreate.AverageRating, afterCreate.RatingCount));
        Assert.Contains("Too sweet.", (await GetPageAsync<RatingResponse>(RatingsRoute(item.Id))).Items.Select(r => r.Comment));

        var updated = await CustomerClients[1].PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(4).WithComment("Grew on me.").Build(), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var afterUpdate = await GetAsync<MenuItemDetailsResponse>(MenuItemRoute(item.Id));
        Assert.Equal((4.5, 2), (afterUpdate.AverageRating, afterUpdate.RatingCount));
        var ratings = (await GetPageAsync<RatingResponse>(RatingsRoute(item.Id))).Items;
        Assert.Contains(ratings, r => r.Comment == "Grew on me." && r.Stars == 4);
        Assert.DoesNotContain("Too sweet.", ratings.Select(r => r.Comment));
    }

    [Fact]
    public async Task CachedEndpoints_StillRequireAToken()
    {
        var item = await SeedRatedItemAsync("Blue Milk");

        foreach (var url in ReadUrls(item.Id))
        {
            Assert.Equal(HttpStatusCode.OK, (await CustomerClient.GetAsync(url, CancellationToken)).StatusCode);

            var anonymous = await AnonymousClient.GetAsync(url, CancellationToken);

            await anonymous.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
        }
    }

    [Fact]
    public async Task UnknownIds_AreNotCached()
    {
        var unknownId = Guid.NewGuid();

        foreach (var url in new[] { MenuItemRoute(unknownId), RatingsRoute(unknownId) })
        {
            await (await CustomerClient.GetAsync(url, CancellationToken)).AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);

            var commandsBefore = DatabaseCommands.Count;
            var repeat = await CustomerClient.GetAsync(url, CancellationToken);

            await repeat.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
            Assert.True(DatabaseCommands.Count > commandsBefore, $"The 404 for {url} was served from the cache.");
        }
    }

    private static string[] ReadUrls(Guid itemId) =>
    [
        $"{MenuItemsRoute}?page=1&pageSize=20",
        $"{MenuItemsRoute}/search?q=milk",
        MenuItemRoute(itemId),
        RatingsRoute(itemId),
    ];

    private async Task<MenuItemResponse> SeedRatedItemAsync(string name)
    {
        var item = await AdminClient.CreateMenuItemAsync(
            new MenuItemRequestBuilder().WithName(name).WithDescription("Chilled and creamy.").WithType(MenuItemType.Drink).Build(),
            CancellationToken);
        await CustomerClient.RateAsync(item.Id, new CreateRatingRequestBuilder().WithStars(5).Build(), CancellationToken);
        return item;
    }

    private async Task<T> GetAsync<T>(string url) =>
        await (await CustomerClient.GetAsync(url, CancellationToken)).ReadJsonAsync<T>(CancellationToken);

    private Task<PagedResponse<T>> GetPageAsync<T>(string url) => GetAsync<PagedResponse<T>>(url);
}
