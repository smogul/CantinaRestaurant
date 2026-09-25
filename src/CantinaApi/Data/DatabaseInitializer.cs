using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Data;

public static class DatabaseInitializer
{
    public static async Task MigrateAndSeedDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CantinaDbContext>();

        await db.Database.MigrateAsync(cancellationToken);

        if (app.Configuration.GetValue<bool>(DbSeeder.EnabledKey))
        {
            await DbSeeder.SeedAsync(db, app.Services.GetRequiredService<TimeProvider>(), cancellationToken);
        }
    }
}
