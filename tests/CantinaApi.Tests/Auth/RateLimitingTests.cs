using System.Net;
using CantinaApi.Features.Auth;
using CantinaApi.Tests.Infrastructure;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class RateLimitingTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    private const int LoginPermitLimit = 10;
    private const int RegisterPermitLimit = 5;

    [Fact]
    public async Task Login_BeyondLimitFromOneIp_ReturnsTooManyRequestsWithRetryAfter()
    {
        var clientIp = TestClientIpStartupFilter.RandomAddress().ToString();
        await UseLoginBudgetAsync(clientIp);

        var response = await AnonymousClient.PostLoginAsync(TestUsers.UniqueEmail(), "Wrong-Password-1", CancellationToken, clientIp);

        var problem = await response.AssertProblemAsync(HttpStatusCode.TooManyRequests, CancellationToken);
        Assert.Equal("Too many requests", problem.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc6585#section-4", problem.GetProperty("type").GetString());
        var retryAfter = Assert.Single(response.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter), 1, 60);
    }

    [Fact]
    public async Task Login_FromAnotherIp_IsUnaffectedWhileOneIpIsLimited()
    {
        var limitedIp = TestClientIpStartupFilter.RandomAddress().ToString();
        await UseLoginBudgetAsync(limitedIp);
        var limited = await AnonymousClient.PostLoginAsync(TestUsers.UniqueEmail(), "Wrong-Password-1", CancellationToken, limitedIp);

        var other = await AnonymousClient.PostLoginAsync(Users.Customer.Email, TestUsers.Password, CancellationToken, TestClientIpStartupFilter.RandomAddress().ToString());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Register_BeyondLimit_IsRejectedButLoginFromSameIpStillWorks()
    {
        for (var i = 0; i < RegisterPermitLimit; i++)
        {
            var allowed = await AnonymousClient.PostJsonAsync("/api/auth/register", NewRegistration(), CancellationToken);
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var rejected = await AnonymousClient.PostJsonAsync("/api/auth/register", NewRegistration(), CancellationToken);
        var login = await AnonymousClient.PostLoginAsync(Users.Customer.Email, TestUsers.Password, CancellationToken);

        await rejected.AssertProblemAsync(HttpStatusCode.TooManyRequests, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task SpoofedForwardedFor_WithoutKnownProxies_DoesNotChangeThePartition()
    {
        var clientIp = TestClientIpStartupFilter.RandomAddress().ToString();

        HttpResponseMessage? last = null;
        for (var i = 0; i <= LoginPermitLimit; i++)
        {
            // A fresh fake origin on every request would give each one its own bucket if the header were trusted.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest(TestUsers.UniqueEmail(), "Wrong-Password-1")),
            };
            request.Headers.Add(TestClientIpStartupFilter.HeaderName, clientIp);
            request.Headers.Add("X-Forwarded-For", TestClientIpStartupFilter.RandomAddress().ToString());
            last = await AnonymousClient.SendAsync(request, CancellationToken);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    private async Task UseLoginBudgetAsync(string clientIp)
    {
        for (var i = 0; i < LoginPermitLimit; i++)
        {
            var response = await AnonymousClient.PostLoginAsync(TestUsers.UniqueEmail(), "Wrong-Password-1", CancellationToken, clientIp);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private static RegisterRequest NewRegistration() => new("Jawa Trader", TestUsers.UniqueEmail("register"), "Utinni-1234");
}
