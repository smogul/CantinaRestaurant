using System.Text;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CantinaApi.Common.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddCantinaAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                jwt => Encoding.UTF8.GetByteCount(jwt.SigningKey) >= JwtOptions.MinSigningKeyBytes,
                $"Jwt:SigningKey must be at least {JwtOptions.MinSigningKeyBytes} bytes.")
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<BruteForceOptions>()
            .Configure<IConfiguration>((bruteForce, config) =>
            {
                config.GetSection(BruteForceOptions.LockoutSection).Bind(bruteForce.Lockout);
                config.GetSection(BruteForceOptions.LoginRateLimitSection).Bind(bruteForce.Login);
                config.GetSection(BruteForceOptions.RegisterRateLimitSection).Bind(bruteForce.Register);
                bruteForce.KnownProxies = config.GetSection(BruteForceOptions.KnownProxiesSection).Get<string[]>() ?? [];
            })
            .Validate(bruteForce => bruteForce.IsValid(out _), "Brute-force settings are invalid; check Auth:Lockout, RateLimiting and ForwardedHeaders.")
            .ValidateOnStart();

        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwtOptions, timeProvider) =>
            {
                var jwt = jwtOptions.Value;

                // Keeps the short JWT claim names such as sub and role instead of long .NET claim URIs.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // Checks nbf and exp against the injected clock that also issues tokens and runs lockouts, so tests can move time consistently.
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = timeProvider.GetUtcNow().UtcDateTime;
                        return expires is not null
                            && expires.Value > now - parameters.ClockSkew
                            && (notBefore is null || notBefore.Value <= now + parameters.ClockSkew);
                    },
                    NameClaimType = ClaimNames.Name,
                    RoleClaimType = ClaimNames.Role,
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = WriteChallengeAsync,
                    OnForbidden = WriteForbiddenAsync,
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(Policies.CustomerOnly, policy => policy.RequireRole(nameof(UserRole.Customer)));

        return services;
    }

    // Replaces the empty default 401 with ProblemDetails but keeps the WWW-Authenticate header RFC 9110 requires.
    private static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var hasInvalidToken = context.AuthenticateFailure is not null;
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = hasInvalidToken ? "Bearer error=\"invalid_token\"" : "Bearer";

        await WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status401Unauthorized,
            hasInvalidToken ? "The bearer token is invalid or has expired." : "A bearer token is required.");
    }

    private static Task WriteForbiddenAsync(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "You do not have permission to perform this action.");
    }

    private static async Task WriteProblemAsync(HttpContext httpContext, int status, string detail)
    {
        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Detail = detail },
        });
    }
}
