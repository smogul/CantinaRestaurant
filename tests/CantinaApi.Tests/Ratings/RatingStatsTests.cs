using System.Net;
using CantinaApi.Features.Auth;
using CantinaApi.Features.MenuItems;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.Ratings;

[Collection(nameof(ApiCollection))]
public sealed class RatingStatsTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Stats_FollowCreatesAndUpdates()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        Assert.Equal((null, 0), await ViewStatsAsync(item.Id));

        await CustomerClients[0].RateAsync(item.Id, new CreateRatingRequestBuilder().WithStars(5).Build(), CancellationToken);
        Assert.Equal((5.0, 1), await ViewStatsAsync(item.Id));

        await CustomerClients[1].RateAsync(item.Id, new CreateRatingRequestBuilder().WithStars(2).Build(), CancellationToken);
        Assert.Equal((3.5, 2), await ViewStatsAsync(item.Id));

        await CustomerClients[0].PostJsonAsync(RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(1).Build(), CancellationToken);
        Assert.Equal((1.5, 2), await ViewStatsAsync(item.Id));

        await AssertStoredStatsMatchRatingsAsync(item.Id);
    }

    [Fact]
    public async Task Stats_StayCorrectWhenManyCustomersRateAtOnce()
    {
        var item = await AdminClient.CreateMenuItemAsync(new MenuItemRequestBuilder().Build(), CancellationToken);
        var customers = await RegisterCustomersAsync(11);

        // Holding each rating write open makes the requests overlap every time instead of only when the timing happens to line up.
        RatingWriteDelay.Delay = TimeSpan.FromMilliseconds(100);

        // Six threes and five twos average 2.5454, which rounds to 2.5; rounding twice via 2.55 would wrongly give 2.6.
        var firstStars = new[] { 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2 };
        var created = await Task.WhenAll(customers.Select((client, i) => client.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(firstStars[i]).Build(), CancellationToken)));

        Assert.All(created, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal((2.5, 11), await ViewStatsAsync(item.Id));

        var updatedStars = new[] { 5, 5, 5, 5, 4, 4, 4, 1, 1, 1, 1 };
        var updated = await Task.WhenAll(customers.Select((client, i) => client.PostJsonAsync(
            RatingsRoute(item.Id), new CreateRatingRequestBuilder().WithStars(updatedStars[i]).Build(), CancellationToken)));

        Assert.All(updated, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        RatingWriteDelay.Delay = TimeSpan.Zero;
        Assert.Equal((Math.Round(updatedStars.Average(), 1, MidpointRounding.AwayFromZero), 11), await ViewStatsAsync(item.Id));
        await AssertStoredStatsMatchRatingsAsync(item.Id);
    }

    private async Task<(double? AverageRating, int RatingCount)> ViewStatsAsync(Guid itemId)
    {
        var view = await (await CustomerClient.GetAsync(MenuItemRoute(itemId), CancellationToken)).ReadJsonAsync<MenuItemDetailsResponse>(CancellationToken);
        return (view.AverageRating, view.RatingCount);
    }

    // Compares the stored columns with a fresh aggregation of the Ratings table.
    private async Task AssertStoredStatsMatchRatingsAsync(Guid itemId)
    {
        var (stored, fresh) = await QueryDatabaseAsync(async db => (
            await db.MenuItems.Where(m => m.Id == itemId).Select(m => new { m.AverageRating, m.RatingCount }).SingleAsync(CancellationToken),
            await db.Ratings.Where(r => r.MenuItemId == itemId).GroupBy(r => r.MenuItemId)
                .Select(g => new { Average = g.Average(r => (decimal)r.Stars), Count = g.Count() }).SingleAsync(CancellationToken)));

        Assert.Equal(fresh.Count, stored.RatingCount);
        Assert.Equal(Math.Round(fresh.Average, 1, MidpointRounding.AwayFromZero), stored.AverageRating);
    }

    // Each customer gets its own client and therefore its own IP, so the register and login rate limits never apply.
    private async Task<IReadOnlyList<HttpClient>> RegisterCustomersAsync(int count)
    {
        var clients = new List<HttpClient>();
        for (var i = 0; i < count; i++)
        {
            var anonymous = CreateClient(accessToken: null);
            var email = TestUsers.UniqueEmail("stats");
            var registered = await anonymous.PostJsonAsync("/api/auth/register", new RegisterRequest($"Patron {i + 1}", email, TestUsers.Password), CancellationToken);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            clients.Add(CreateClient(await TestUsers.LoginAsync(anonymous, email, TestUsers.Password, CancellationToken)));
        }

        return clients;
    }
}
