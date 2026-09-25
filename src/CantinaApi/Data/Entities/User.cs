namespace CantinaApi.Data.Entities;

public sealed class User
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Always stored trimmed and lowercased so lookups and the unique index ignore case.
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    // Lockout fields are stored now and enforced from Phase 3.
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
