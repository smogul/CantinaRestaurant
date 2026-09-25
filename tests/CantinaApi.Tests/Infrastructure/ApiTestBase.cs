using System.Net.Http.Headers;
using CantinaApi.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CantinaApi.Tests.Infrastructure;

public abstract class ApiTestBase : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly List<HttpClient> _clients = [];

    protected ApiTestBase(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        AnonymousClient = CreateClient(accessToken: null);
        AdminClient = CreateClient(Users.Admin.AccessToken);
        CustomerClients = Users.Customers.Select(customer => CreateClient(customer.AccessToken)).ToArray();
    }

    protected TestUsers Users => _factory.Users;

    protected FakeTimeProvider Time => _factory.Time;

    protected DateTime UtcNow => Time.GetUtcNow().UtcDateTime;

    protected HttpClient AnonymousClient { get; }

    // Admins manage the menu and can read everything, so most menu tests act as the admin.
    protected HttpClient AdminClient { get; }

    protected IReadOnlyList<HttpClient> CustomerClients { get; }

    protected HttpClient CustomerClient => CustomerClients[0];

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync(CancellationToken);

    public ValueTask DisposeAsync()
    {
        _clients.ForEach(client => client.Dispose());
        return ValueTask.CompletedTask;
    }

    // Each client gets its own random IP so rate limit buckets never leak between tests.
    protected HttpClient CreateClient(string? accessToken, string? clientIp = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, clientIp ?? TestClientIpStartupFilter.RandomAddress().ToString());
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        _clients.Add(client);
        return client;
    }

    protected async Task<T> QueryDatabaseAsync<T>(Func<CantinaDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CantinaDbContext>());
    }
}
