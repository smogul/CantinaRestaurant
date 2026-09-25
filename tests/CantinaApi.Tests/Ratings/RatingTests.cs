using System.Net;
using CantinaApi.Common;
using CantinaApi.Features.MenuItems;
using CantinaApi.Features.Ratings;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.Ratings;

[Collection(nameof(ApiCollection))]
public sealed class RatingTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Rate_ReturnsCreatedRating()
    {
        var item = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var response = await Client.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(4).WithComment("Solid choice.").Build(), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rating = await response.ReadJsonAsync<RatingResponse>(CancellationToken);
        Assert.Equal((item.Id, 4, "Solid choice."), (rating.MenuItemId, rating.Stars, rating.Comment));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(null)]
    public async Task Rate_WithStarsOutOfRange_ReturnsValidationProblem(int? stars)
    {
        var item = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var response = await Client.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(stars).Build(), CancellationToken);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
        Assert.Contains(problem.ErrorKeys(), key => string.Equals(key, "stars", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rate_WithCommentOver1000Characters_ReturnsValidationProblem()
    {
        var item = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var response = await Client.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithComment(new string('a', 1001)).Build(), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
    }

    [Fact]
    public async Task Rate_DeletedItem_ReturnsNotFound()
    {
        var item = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        await Client.DeleteAsync(MenuItemRoute(item.Id), CancellationToken);

        var response = await Client.PostJsonAsync(RatingsRoute(item.Id), new CreateRatingRequestBuilder().Build(), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
    }

    [Fact]
    public async Task Rate_UpdatesAverageAndCountOnView()
    {
        var item = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        foreach (var stars in new[] { 5, 4, 4 })
        {
            await Client.RateAsync(item.Id, new CreateRatingRequestBuilder().WithStars(stars).Build(), CancellationToken);
        }

        var view = await (await Client.GetAsync(MenuItemRoute(item.Id), CancellationToken))
            .ReadJsonAsync<MenuItemDetailsResponse>(CancellationToken);

        Assert.Equal(4.3, view.AverageRating);
        Assert.Equal(3, view.RatingCount);
    }

    [Fact]
    public async Task ListRatings_IsPaginatedNewestFirst()
    {
        var item = await Client.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        foreach (var comment in new[] { "first", "second", "third" })
        {
            await Client.RateAsync(item.Id, new CreateRatingRequestBuilder().WithComment(comment).Build(), CancellationToken);
        }

        var firstPage = await GetRatingsAsync(item.Id, page: 1);
        var secondPage = await GetRatingsAsync(item.Id, page: 2);

        Assert.Equal(["third", "second"], firstPage.Items.Select(rating => rating.Comment));
        Assert.Equal((1, 2, 3, 2), (firstPage.Page, firstPage.PageSize, firstPage.TotalCount, firstPage.TotalPages));
        Assert.Equal(["first"], secondPage.Items.Select(rating => rating.Comment));
    }

    [Fact]
    public async Task ListRatings_ForUnknownItem_ReturnsNotFound()
    {
        var response = await Client.GetAsync(RatingsRoute(Guid.NewGuid()), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
    }

    private async Task<PagedResponse<RatingResponse>> GetRatingsAsync(Guid menuItemId, int page) =>
        await (await Client.GetAsync($"{RatingsRoute(menuItemId)}?page={page}&pageSize=2", CancellationToken))
            .ReadJsonAsync<PagedResponse<RatingResponse>>(CancellationToken);
}
