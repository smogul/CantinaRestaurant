using CantinaApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace CantinaApi.Tests.Infrastructure;

public sealed class CustomWebApplicationFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "cantina-integration-tests-signing-key-0123456789";
    public const string Issuer = "CantinaApi.Tests";
    public const string Audience = "CantinaApi.Tests.Clients";

    private string? _truncateSql;

    public TestUsers Users { get; private set; } = null!;

    // Starts at the real time so tokens look normal; each read nudges it forward so consecutive timestamps still differ.
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow) { AutoAdvanceAmount = TimeSpan.FromMilliseconds(1) };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CantinaDb", postgres.ConnectionString);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);

        // The lowest bcrypt cost keeps hashing from dominating the suite's run time.
        builder.UseSetting("Auth:BcryptWorkFactor", "4");

        // Tests start from an empty database and create exactly the data they assert on.
        builder.UseSetting("Seed:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    // Starting the host runs the same migrate step as production; the test users are then created once for the run.
    public async ValueTask InitializeAsync()
    {
        StartServer();
        Users = await TestUsers.CreateAsync(this, CancellationToken.None);
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CantinaDbContext>();

        // Users survive resets so the tokens cached in TestUsers stay valid; every other table is emptied.
        var userTable = db.Model.FindEntityType(typeof(Data.Entities.User))!.GetTableName();
        _truncateSql ??= "TRUNCATE TABLE "
            + string.Join(", ", db.Model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .Where(table => table != userTable)
                .Distinct()
                .Select(table => $"\"{table}\""))
            + " RESTART IDENTITY CASCADE";

        await db.Database.ExecuteSqlRawAsync(_truncateSql, cancellationToken);
    }
}
