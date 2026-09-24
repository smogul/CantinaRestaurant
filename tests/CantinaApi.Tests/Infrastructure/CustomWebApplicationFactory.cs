using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CantinaApi.Tests.Infrastructure;

public sealed class CustomWebApplicationFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CantinaDb", postgres.ConnectionString);
    }

    // Starting the host runs the same migrate-and-seed step as production, before any test executes.
    public ValueTask InitializeAsync()
    {
        StartServer();
        return ValueTask.CompletedTask;
    }
}
