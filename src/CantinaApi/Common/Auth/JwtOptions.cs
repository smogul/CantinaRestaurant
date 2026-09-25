using System.ComponentModel.DataAnnotations;

namespace CantinaApi.Common.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSigningKeyBytes = 32;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    // HMAC-SHA256 needs a key of at least 256 bits, which startup validation enforces.
    [Required]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 24 * 60)]
    public int LifetimeMinutes { get; init; } = 60;
}
