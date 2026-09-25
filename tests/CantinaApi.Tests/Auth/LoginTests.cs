using System.Net;
using System.Net.Http.Headers;
using CantinaApi.Features.Auth;
using CantinaApi.Tests.Infrastructure;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class LoginTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    private const string LoginRoute = "/api/auth/login";

    [Fact]
    public async Task Login_ReturnsTokenThatAuthorizesMenuRequests()
    {
        var response = await AnonymousClient.PostJsonAsync(
            LoginRoute, new LoginRequest(Users.Customer.Email.ToUpperInvariant(), TestUsers.Password), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.ReadJsonAsync<AccessTokenResponse>(CancellationToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.InRange(token.ExpiresAtUtc, UtcNow.AddMinutes(59), UtcNow.AddMinutes(61));

        using var request = new HttpRequestMessage(HttpMethod.Get, ApiClientExtensions.MenuItemsRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
        var menuResponse = await AnonymousClient.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, menuResponse.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmailAndWrongPassword_ReturnIdenticalBodies()
    {
        var unknownEmail = await PostLoginAsync(new LoginRequest(TestUsers.UniqueEmail("nobody"), TestUsers.Password));
        var wrongPassword = await PostLoginAsync(new LoginRequest(Users.Customer.Email, "Wrong-Password-1"));

        await unknownEmail.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
        await wrongPassword.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
        Assert.Equal(
            await unknownEmail.Content.ReadAsByteArrayAsync(CancellationToken),
            await wrongPassword.Content.ReadAsByteArrayAsync(CancellationToken));
    }

    // Both requests share a correlation id so the only difference left would be one the API itself introduced.
    private async Task<HttpResponseMessage> PostLoginAsync(LoginRequest login)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginRoute)
        {
            Content = System.Net.Http.Json.JsonContent.Create(login, options: ApiClientExtensions.JsonOptions),
        };
        request.Headers.Add("X-Correlation-Id", "login-comparison");
        return await AnonymousClient.SendAsync(request, CancellationToken);
    }
}
