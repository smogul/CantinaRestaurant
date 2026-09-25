using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace CantinaApi.Tests.Infrastructure;

// Test-only: TestServer has no real client address, so each request takes its IP from a header instead of sharing one rate limit bucket.
public sealed class TestClientIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.TryParse(context.Request.Headers[HeaderName], out var address)
                ? address
                : RandomAddress();
            return nextMiddleware(context);
        });
        next(app);
    };

    // Addresses in 10.0.0.0/8 so they never collide with a real client.
    public static IPAddress RandomAddress()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        bytes[0] = 10;
        return new IPAddress(bytes);
    }
}
