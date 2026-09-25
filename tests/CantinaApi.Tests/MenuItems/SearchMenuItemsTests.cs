using CantinaApi.Common;
using CantinaApi.Data.Entities;
using CantinaApi.Features.MenuItems;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.MenuItems;

[Collection(nameof(ApiCollection))]
public sealed class SearchMenuItemsTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Theory]
    [InlineData("bantha")]
    [InlineData("BANTHA")]
    [InlineData("BaNtHa")]
    public async Task Search_MatchesNameAndDescriptionIgnoringCase(string q)
    {
        await SeedCantinaMenuAsync();

        var results = await SearchAsync($"q={q}");

        Assert.Equal(["Bantha Burger", "Blue Milk"], results.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Search_RespectsTypeFilter()
    {
        await SeedCantinaMenuAsync();

        var results = await SearchAsync("q=bantha&type=Drink");

        Assert.Equal(["Blue Milk"], results.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Search_Paginates()
    {
        await SeedCantinaMenuAsync();

        var results = await SearchAsync("q=bantha&page=2&pageSize=1");

        Assert.Equal(["Blue Milk"], results.Items.Select(item => item.Name));
        Assert.Equal((2, 1, 2, 2), (results.Page, results.PageSize, results.TotalCount, results.TotalPages));
    }

    [Theory]
    [InlineData("100%", "100% Blue Milk")]
    [InlineData("a_J", "Jawa_Juice")]
    [InlineData("\\", "Back\\slash Brew")]
    public async Task Search_TreatsWildcardsLiterally(string q, string expectedName)
    {
        foreach (var name in new[] { "100% Blue Milk", "1000 Credit Feast", "Jawa_Juice", "JawaXJuice", "Back\\slash Brew" })
        {
            await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().WithName(name).Build(), CancellationToken);
        }

        var results = await SearchAsync($"q={Uri.EscapeDataString(q)}");

        Assert.Equal([expectedName], results.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Search_WithNoMatches_ReturnsEmptyPage()
    {
        await SeedCantinaMenuAsync();

        var results = await SearchAsync("q=wookiee");

        Assert.Empty(results.Items);
        Assert.Equal(0, results.TotalCount);
    }

    private async Task SeedCantinaMenuAsync()
    {
        await Client.CreateMenuItemAsync(new MenuItemRequestBuilder()
            .WithName("Bantha Burger").WithDescription("Smoked patty on a toasted bun.").WithType(MenuItemType.Dish).Build(), CancellationToken);
        await Client.CreateMenuItemAsync(new MenuItemRequestBuilder()
            .WithName("Blue Milk").WithDescription("Chilled bantha milk with vanilla.").WithType(MenuItemType.Drink).Build(), CancellationToken);
        await Client.CreateMenuItemAsync(new MenuItemRequestBuilder()
            .WithName("Ronto Wrap").WithDescription("Grilled ronto in flatbread.").WithType(MenuItemType.Dish).Build(), CancellationToken);
    }

    private async Task<PagedResponse<MenuItemResponse>> SearchAsync(string query) =>
        await (await Client.GetAsync($"{MenuItemsRoute}/search?{query}", CancellationToken))
            .ReadJsonAsync<PagedResponse<MenuItemResponse>>(CancellationToken);
}
