using System.Net;
using System.Text.Json;
using CantinaApi.Common;
using CantinaApi.Features.MenuItems;
using CantinaApi.Features.Ratings;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.Ratings;

[Collection(nameof(ApiCollection))]
public sealed class RatingTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Rate_ReturnsCreatedRatingWithReviewerName()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var response = await CustomerClient.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(4).WithComment("Solid choice.").Build(), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rating = await response.ReadJsonAsync<RatingResponse>(CancellationToken);
        Assert.Equal((item.Id, Users.Customer.Name, 4, "Solid choice."), (rating.MenuItemId, rating.ReviewerName, rating.Stars, rating.Comment));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(null)]
    public async Task Rate_WithStarsOutOfRange_ReturnsValidationProblem(int? stars)
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var response = await CustomerClient.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(stars).Build(), CancellationToken);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
        Assert.Contains(problem.ErrorKeys(), key => string.Equals(key, "stars", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rate_WithCommentOver1000Characters_ReturnsValidationProblem()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);

        var response = await CustomerClient.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithComment(new string('a', 1001)).Build(), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
    }

    [Fact]
    public async Task Rate_DeletedItem_ReturnsNotFound()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        await AdminClient.DeleteAsync(MenuItemRoute(item.Id), CancellationToken);

        var response = await CustomerClient.PostJsonAsync(RatingsRoute(item.Id), new CreateRatingRequestBuilder().Build(), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
    }

    [Fact]
    public async Task Rate_UsesTokenSubjectAndIgnoresUserIdInBody()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        var spoofedUserId = Users.Customers[1].Id;

        var response = await CustomerClient.PostJsonAsync(
            RatingsRoute(item.Id), new { stars = 5, comment = "Mine, not Leia's.", userId = spoofedUserId }, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rating = await response.ReadJsonAsync<RatingResponse>(CancellationToken);
        var storedUserId = await QueryDatabaseAsync(db =>
            db.Ratings.Where(r => r.Id == rating.Id).Select(r => r.UserId).SingleAsync(CancellationToken));
        Assert.Equal(Users.Customer.Id, storedUserId);
    }

    [Fact]
    public async Task Rate_SameItemTwice_UpdatesTheExistingRating()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        var first = await CustomerClient.RateAsync(item.Id, new CreateRatingRequestBuilder().WithStars(2).WithComment("Too sweet.").Build(), CancellationToken);

        var response = await CustomerClient.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(5).WithComment("Grew on me.").Build(), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await response.ReadJsonAsync<RatingResponse>(CancellationToken);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal((5, "Grew on me."), (second.Stars, second.Comment));
        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.True(second.UpdatedAtUtc > first.UpdatedAtUtc);

        var view = await (await CustomerClient.GetAsync(MenuItemRoute(item.Id), CancellationToken))
            .ReadJsonAsync<MenuItemDetailsResponse>(CancellationToken);
        Assert.Equal((1, 5.0), (view.RatingCount, view.AverageRating));
    }

    [Fact]
    public async Task Rate_ByDifferentCustomers_UpdatesAverageAndCountOnView()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        var stars = new[] { 5, 4, 4 };
        for (var i = 0; i < stars.Length; i++)
        {
            await CustomerClients[i].RateAsync(item.Id, new CreateRatingRequestBuilder().WithStars(stars[i]).Build(), CancellationToken);
        }

        var view = await (await CustomerClient.GetAsync(MenuItemRoute(item.Id), CancellationToken))
            .ReadJsonAsync<MenuItemDetailsResponse>(CancellationToken);

        Assert.Equal(4.3, view.AverageRating);
        Assert.Equal(3, view.RatingCount);
    }

    [Fact]
    public async Task ListRatings_IsPaginatedNewestFirstWithNamesButNoEmails()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        for (var i = 0; i < CustomerClients.Count; i++)
        {
            await CustomerClients[i].RateAsync(item.Id, new CreateRatingRequestBuilder().WithComment($"rating {i + 1}").Build(), CancellationToken);
        }

        var firstPageResponse = await CustomerClient.GetAsync($"{RatingsRoute(item.Id)}?page=1&pageSize=2", CancellationToken);
        var firstPage = await firstPageResponse.ReadJsonAsync<PagedResponse<RatingResponse>>(CancellationToken);
        var secondPage = await (await CustomerClient.GetAsync($"{RatingsRoute(item.Id)}?page=2&pageSize=2", CancellationToken))
            .ReadJsonAsync<PagedResponse<RatingResponse>>(CancellationToken);

        Assert.Equal(["rating 3", "rating 2"], firstPage.Items.Select(rating => rating.Comment));
        Assert.Equal([Users.Customers[2].Name, Users.Customers[1].Name], firstPage.Items.Select(rating => rating.ReviewerName));
        Assert.Equal((1, 2, 3, 2), (firstPage.Page, firstPage.PageSize, firstPage.TotalCount, firstPage.TotalPages));
        Assert.Equal(["rating 1"], secondPage.Items.Select(rating => rating.Comment));

        var rawJson = await firstPageResponse.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("email", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Users.Customers[2].Email, rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListRatings_ForUnknownItem_ReturnsNotFound()
    {
        var response = await CustomerClient.GetAsync(RatingsRoute(Guid.NewGuid()), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, CancellationToken);
    }
}
