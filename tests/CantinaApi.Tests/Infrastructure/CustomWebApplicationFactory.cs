using CantinaApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CantinaApi.Tests.Infrastructure;

public sealed class CustomWebApplicationFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string? _truncateSql;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CantinaDb", postgres.ConnectionString);

        // Tests start from an empty database and create exactly the data they assert on.
        builder.UseSetting(DbSeeder.EnabledKey, "false");
    }

    // Starting the host runs the same migrate-and-seed step as production, before any test executes.
    public ValueTask InitializeAsync()
    {
        StartServer();
        return ValueTask.CompletedTask;
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CantinaDbContext>();

        // Table names come from the EF model, so new entities are reset without touching this helper.
        _truncateSql ??= "TRUNCATE TABLE "
            + string.Join(", ", db.Model.GetEntityTypes().Select(e => $"\"{e.GetTableName()}\"").Distinct())
            + " RESTART IDENTITY CASCADE";

        await db.Database.ExecuteSqlRawAsync(_truncateSql, cancellationToken);
    }
}
