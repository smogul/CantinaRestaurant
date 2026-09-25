using CantinaApi.Common.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CantinaApi.Data;

public static class DatabaseInitializer
{
    public static async Task MigrateAndSeedDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CantinaDbContext>();

        await db.Database.MigrateAsync(cancellationToken);

        var seedOptions = app.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (seedOptions.Enabled)
        {
            await DbSeeder.SeedAsync(
                db,
                seedOptions,
                app.Services.GetRequiredService<PasswordHasher>(),
                app.Services.GetRequiredService<TimeProvider>(),
                cancellationToken);
        }
    }
}
