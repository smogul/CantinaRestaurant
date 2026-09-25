using System.Net;

namespace CantinaApi.Common.Auth;

// Collects the lockout, rate limit and proxy settings, which live under separate configuration sections.
public sealed class BruteForceOptions
{
    public const string LockoutSection = "Auth:Lockout";
    public const string LoginRateLimitSection = "RateLimiting:Login";
    public const string RegisterRateLimitSection = "RateLimiting:Register";
    public const string KnownProxiesSection = "ForwardedHeaders:KnownProxies";

    public LockoutSettings Lockout { get; } = new();
    public RateLimitSettings Login { get; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public RateLimitSettings Register { get; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public string[] KnownProxies { get; set; } = [];

    public bool IsValid(out string error)
    {
        error = Lockout switch
        {
            { MaxFailedAttempts: < 1 } => "Auth:Lockout:MaxFailedAttempts must be at least 1.",
            { DurationMinutes: < 1 } => "Auth:Lockout:DurationMinutes must be at least 1.",
            _ when !Login.IsValid => "RateLimiting:Login needs a PermitLimit and WindowSeconds of at least 1.",
            _ when !Register.IsValid => "RateLimiting:Register needs a PermitLimit and WindowSeconds of at least 1.",
            _ when !KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _)) => "ForwardedHeaders:KnownProxies must contain only IP addresses.",
            _ => string.Empty,
        };

        return error.Length == 0;
    }
}

public sealed class LockoutSettings
{
    public int MaxFailedAttempts { get; set; } = 5;
    public int DurationMinutes { get; set; } = 15;
}

public sealed class RateLimitSettings
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }

    public bool IsValid => PermitLimit >= 1 && WindowSeconds >= 1;
}
