using System.Globalization;
using System.Threading.RateLimiting;
using CantinaApi.Common.Auth;
using CantinaApi.Common.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CantinaApi.Common.RateLimiting;

public static class RateLimitingSetup
{
    public static IServiceCollection AddCantinaRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Separate policies so login attempts and sign-ups each have their own budget per IP.
            options.AddPolicy(RateLimitPolicies.Login, context => PerClientIp(context, bruteForce => bruteForce.Login));
            options.AddPolicy(RateLimitPolicies.Register, context => PerClientIp(context, bruteForce => bruteForce.Register));

            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    private static RateLimitPartition<string> PerClientIp(HttpContext context, Func<BruteForceOptions, RateLimitSettings> selectSettings)
    {
        var settings = selectSettings(context.RequestServices.GetRequiredService<IOptions<BruteForceOptions>>().Value);

        return RateLimitPartition.GetFixedWindowLimiter(ClientIp.Get(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.PermitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = 0,
        });
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var policyName = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RateLimitingSetup))
            .LogWarning(
                "Rate limit {Policy} rejected {ClientIp} on {Path}",
                policyName,
                ClientIp.Get(httpContext),
                httpContext.Request.Path.Value);

        // Rounded up so a client that waits exactly this long is never still inside the window.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                // The framework has no default type link for 429, so it points at RFC 6585, which defines the status.
                Type = "https://tools.ietf.org/html/rfc6585#section-4",
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Too many attempts from this address. Wait before trying again.",
            },
        });
    }
}
