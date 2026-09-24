namespace CantinaApi.Data;

public static class DbSeeder
{
    // Runs after migrations on every startup, so seeding must be idempotent.
    public static Task SeedAsync(CantinaDbContext db, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
