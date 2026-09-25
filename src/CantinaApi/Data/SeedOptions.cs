namespace CantinaApi.Data;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; init; }
    public string AdminName { get; init; } = "Chalmun";
    public string? AdminEmail { get; init; }
    public string? AdminPassword { get; init; }
    public string CustomerName { get; init; } = "Han Solo";
    public string? CustomerEmail { get; init; }
    public string? CustomerPassword { get; init; }

    // Credentials only matter when seeding is on, so a disabled seed never blocks startup.
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(AdminEmail) && !string.IsNullOrWhiteSpace(AdminPassword)
        && !string.IsNullOrWhiteSpace(CustomerEmail) && !string.IsNullOrWhiteSpace(CustomerPassword);
}
