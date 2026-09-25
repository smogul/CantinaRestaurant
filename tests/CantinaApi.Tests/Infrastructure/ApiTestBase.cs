using CantinaApi.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CantinaApi.Tests.Infrastructure;

public abstract class ApiTestBase(CustomWebApplicationFactory factory) : IAsyncLifetime
{
    protected HttpClient Client { get; } = factory.CreateClient();

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetDatabaseAsync(CancellationToken);

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }

    protected async Task<T> QueryDatabaseAsync<T>(Func<CantinaDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CantinaDbContext>());
    }
}
