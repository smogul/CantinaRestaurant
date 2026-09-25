using System.Net;
using System.Text.Json.Nodes;
using CantinaApi.Features.Auth;
using CantinaApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class LockoutTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    private const string Password = "Correct-Horse-1";
    private const string WrongPassword = "Wrong-Horse-1";

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount()
    {
        var email = await RegisterAsync();
        await FailLoginsAsync(email, 5);

        var response = await AnonymousClient.PostLoginAsync(email, Password, CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
        var (attempts, lockoutEnd) = await GetLockoutStateAsync(email);
        Assert.Equal(0, attempts);
        Assert.NotNull(lockoutEnd);
        Assert.InRange(lockoutEnd.Value, UtcNow.AddMinutes(14), UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task AfterLockoutExpires_CorrectPasswordSucceedsAndResetsCounters()
    {
        var email = await RegisterAsync();
        await FailLoginsAsync(email, 5);

        Time.Advance(TimeSpan.FromMinutes(16));
        var response = await AnonymousClient.PostLoginAsync(email, Password, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((0, null), await GetLockoutStateAsync(email));
    }

    [Fact]
    public async Task FourWrongPasswords_ThenCorrectPassword_SucceedsAndResetsCounter()
    {
        var email = await RegisterAsync();
        await FailLoginsAsync(email, 4);
        Assert.Equal((4, null), await GetLockoutStateAsync(email));

        var response = await AnonymousClient.PostLoginAsync(email, Password, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((0, null), await GetLockoutStateAsync(email));
    }

    [Fact]
    public async Task UnknownEmailWrongPasswordAndLockedAccount_ReturnIdenticalBodies()
    {
        var email = await RegisterAsync();

        var unknownEmail = await ReadFailureBodyAsync(await AnonymousClient.PostLoginAsync(TestUsers.UniqueEmail("nobody"), Password, CancellationToken));
        var wrongPassword = await ReadFailureBodyAsync(await AnonymousClient.PostLoginAsync(email, WrongPassword, CancellationToken));
        await FailLoginsAsync(email, 4);
        var lockedAccount = await ReadFailureBodyAsync(await AnonymousClient.PostLoginAsync(email, Password, CancellationToken));

        Assert.Equal(unknownEmail, wrongPassword);
        Assert.Equal(unknownEmail, lockedAccount);
    }

    [Fact]
    public async Task ConcurrentWrongPasswords_StillLockTheAccount()
    {
        var email = await RegisterAsync();

        // Each request comes from its own IP so only the account lockout, not rate limiting, is under test.
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            AnonymousClient.PostLoginAsync(email, WrongPassword, CancellationToken, TestClientIpStartupFilter.RandomAddress().ToString())));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        var (_, lockoutEnd) = await GetLockoutStateAsync(email);
        Assert.True(lockoutEnd > UtcNow);

        var correctPassword = await AnonymousClient.PostLoginAsync(email, Password, CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);
    }

    private async Task<string> RegisterAsync()
    {
        var email = TestUsers.UniqueEmail("lockout");
        var response = await AnonymousClient.PostJsonAsync("/api/auth/register", new RegisterRequest("Greedo", email, Password), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private async Task FailLoginsAsync(string email, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var response = await AnonymousClient.PostLoginAsync(email, WrongPassword, CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private Task<(int FailedLoginAttempts, DateTime? LockoutEndUtc)> GetLockoutStateAsync(string email) =>
        QueryDatabaseAsync(async db => await db.Users
            .Where(u => u.Email == email)
            .Select(u => new ValueTuple<int, DateTime?>(u.FailedLoginAttempts, u.LockoutEndUtc))
            .SingleAsync(CancellationToken));

    // The correlation id is unique per request by design, so it is the one field removed before comparing.
    private async Task<string> ReadFailureBodyAsync(HttpResponseMessage response)
    {
        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, CancellationToken);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!.AsObject();
        Assert.True(body.Remove("correlationId"));
        return body.ToJsonString();
    }
}
