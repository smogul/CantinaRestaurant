using System.Diagnostics;
using System.Net;
using CantinaApi.Features.Auth;
using CantinaApi.Tests.Infrastructure;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class LoginTimingTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    private const int Attempts = 5;

    [Fact]
    public async Task UnknownEmail_TakesComparableTimeToWrongPassword()
    {
        var target = await RegisterAsync();

        // Warm both paths on a separate account so JIT and connection setup do not skew the first measurement.
        var warmUp = await RegisterAsync();
        await TimeLoginAsync(warmUp, "Wrong-Password-1");
        await TimeLoginAsync(TestUsers.UniqueEmail("nobody"), "Wrong-Password-1");

        var wrongPassword = TimeSpan.Zero;
        var unknownEmail = TimeSpan.Zero;
        for (var i = 0; i < Attempts; i++)
        {
            wrongPassword += await TimeLoginAsync(target, "Wrong-Password-1");
            unknownEmail += await TimeLoginAsync(TestUsers.UniqueEmail("nobody"), "Wrong-Password-1");
        }

        // Deliberately loose: it catches an unknown-email path that skips hashing, without flaking on machine noise.
        Assert.True(
            unknownEmail.TotalMilliseconds >= wrongPassword.TotalMilliseconds * 0.5,
            $"Unknown email averaged {unknownEmail.TotalMilliseconds / Attempts:F1} ms, wrong password {wrongPassword.TotalMilliseconds / Attempts:F1} ms.");
    }

    private async Task<string> RegisterAsync()
    {
        var email = TestUsers.UniqueEmail("timing");
        var response = await AnonymousClient.PostJsonAsync("/api/auth/register", new RegisterRequest("Wuher", email, TestUsers.Password), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    // Each request uses a fresh IP so the login rate limit never interferes with the measurement.
    private async Task<TimeSpan> TimeLoginAsync(string email, string password)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await AnonymousClient.PostLoginAsync(email, password, CancellationToken, TestClientIpStartupFilter.RandomAddress().ToString());
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return stopwatch.Elapsed;
    }
}
