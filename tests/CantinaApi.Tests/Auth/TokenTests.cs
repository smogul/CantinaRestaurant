using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CantinaApi.Tests.Builders;
using CantinaApi.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using static CantinaApi.Tests.Infrastructure.ApiClientExtensions;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class TokenTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", MenuItemsRoute },
        { "GET", $"{MenuItemsRoute}/search?q=milk" },
        { "GET", MenuItemRoute(Guid.NewGuid()) },
        { "POST", MenuItemsRoute },
        { "PUT", MenuItemRoute(Guid.NewGuid()) },
        { "DELETE", MenuItemRoute(Guid.NewGuid()) },
        { "POST", RatingsRoute(Guid.NewGuid()) },
        { "GET", RatingsRoute(Guid.NewGuid()) },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task MissingToken_ReturnsUnauthorizedProblem(string method, string url)
    {
        var response = await SendAsync(AnonymousClient, method, url);

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task TamperedToken_ReturnsUnauthorized()
    {
        // A customer rewrites their own role claim to Admin but cannot re-sign the token.
        var parts = Users.Customer.AccessToken.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!.AsObject();
        payload["role"] = "Admin";
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload.ToJsonString()))}.{parts[2]}";

        var response = await SendAsync(CreateClient(tampered), "POST", MenuItemsRoute);

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsUnauthorized()
    {
        var token = TestTokens.Create(
            Users.Customer, "Customer", notBeforeUtc: UtcNow.AddHours(-2), expiresUtc: UtcNow.AddMinutes(-5));

        var response = await CreateClient(token).GetAsync(MenuItemsRoute, CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_ReturnsUnauthorized()
    {
        var token = TestTokens.Create(
            Users.Admin, "Admin", UtcNow.AddMinutes(-1), UtcNow.AddMinutes(30), signingKey: "some-other-signing-key-that-is-long-enough-0123");

        var response = await CreateClient(token).GetAsync(MenuItemsRoute, CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
    }

    [Fact]
    public async Task ValidTestToken_IsAccepted()
    {
        // Guards the tests above: a token minted the same way with the right key does work.
        var token = TestTokens.Create(Users.Customer, "Customer", UtcNow.AddMinutes(-1), UtcNow.AddMinutes(30));

        var response = await CreateClient(token).GetAsync(MenuItemsRoute, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(new MenuItemRequestBuilder().Build(), options: JsonOptions);
        }

        return await client.SendAsync(request, CancellationToken);
    }
}
