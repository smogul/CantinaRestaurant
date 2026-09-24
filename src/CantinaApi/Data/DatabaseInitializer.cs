using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Data;

public static class DatabaseInitializer
{
    public static async Task MigrateAndSeedDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CantinaDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
        await DbSeeder.SeedAsync(db, cancellationToken);
    }
}
