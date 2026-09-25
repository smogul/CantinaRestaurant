using System.Net;
using System.Text.Json;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.MenuItems;

[Collection(nameof(ApiCollection))]
public sealed class MenuItemValidationTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    public static TheoryData<string, object?> InvalidFields => new()
    {
        { "name", null },
        { "name", new string('a', 101) },
        { "price", 0m },
        { "price", -1m },
        { "imageUrl", "ftp://images.example.com/dish.png" },
        { "imageUrl", "not a url" },
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task Create_WithInvalidField_ReturnsValidationProblem(string field, object? value)
    {
        var body = new MenuItemRequestBuilder().BuildJson();
        body[field] = JsonSerializer.SerializeToNode(value);

        var response = await Client.PostJsonAsync(MenuItemsRoute, body, CancellationToken);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
        Assert.Contains(problem.ErrorKeys(), key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task Update_WithInvalidField_ReturnsValidationProblem(string field, object? value)
    {
        var created = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        var body = new MenuItemRequestBuilder().BuildJson();
        body[field] = JsonSerializer.SerializeToNode(value);

        var response = await Client.PutJsonAsync(MenuItemRoute(created.Id), body, CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
    }

    [Theory]
    [InlineData("Snack")]
    [InlineData(1)]
    public async Task Create_WithInvalidType_ReturnsBadRequestProblem(object type)
    {
        var body = new MenuItemRequestBuilder().BuildJson();
        body["type"] = JsonSerializer.SerializeToNode(type);

        var response = await Client.PostJsonAsync(MenuItemsRoute, body, CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
    }

    [Fact]
    public async Task Create_WithoutType_ReturnsValidationProblem()
    {
        var body = new MenuItemRequestBuilder().BuildJson();
        body.Remove("type");

        var response = await Client.PostJsonAsync(MenuItemsRoute, body, CancellationToken);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
        Assert.Contains(problem.ErrorKeys(), key => string.Equals(key, "type", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(MenuItemsRoute + "?pageSize=0")]
    [InlineData(MenuItemsRoute + "?pageSize=101")]
    [InlineData(MenuItemsRoute + "?page=0")]
    [InlineData(MenuItemsRoute + "?type=Snack")]
    [InlineData(MenuItemsRoute + "/search?q=")]
    [InlineData(MenuItemsRoute + "/search")]
    [InlineData(MenuItemsRoute + "/search?q=%20%20")]
    [InlineData(MenuItemsRoute + "/search?q=milk&pageSize=0")]
    [InlineData(MenuItemsRoute + "/search?q=milk&pageSize=101")]
    public async Task InvalidQuery_ReturnsBadRequestProblem(string url)
    {
        var response = await Client.GetAsync(url, CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
    }

    [Fact]
    public async Task Search_WithQueryOver100Characters_ReturnsValidationProblem()
    {
        var response = await Client.GetAsync($"{MenuItemsRoute}/search?q={new string('a', 101)}", CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
    }
}
