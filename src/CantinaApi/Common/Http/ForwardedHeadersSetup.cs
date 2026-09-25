using System.Net;
using CantinaApi.Common.Auth;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace CantinaApi.Common.Http;

public static class ForwardedHeadersSetup
{
    // X-Forwarded-For is trusted only from listed proxies; with none listed, any client could spoof its IP and dodge the rate limits.
    public static IServiceCollection AddCantinaForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<BruteForceOptions>>((forwarded, bruteForce) =>
            {
                var proxies = bruteForce.Value.KnownProxies;

                // The middleware trusts every sender when both known lists are empty, so with no proxy configured the header is switched off entirely.
                forwarded.ForwardedHeaders = proxies.Length > 0 ? ForwardedHeaders.XForwardedFor : ForwardedHeaders.None;
                forwarded.KnownIPNetworks.Clear();
                forwarded.KnownProxies.Clear();

                foreach (var proxy in proxies)
                {
                    forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            });

        return services;
    }
}
