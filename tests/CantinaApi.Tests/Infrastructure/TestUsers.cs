using System.Net;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using CantinaApi.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CantinaApi.Tests.Infrastructure;

public sealed record TestUser(Guid Id, string Name, string Email, string Password, string AccessToken);

// Users are created through the real register and login endpoints once per fixture, and their tokens are reused.
public sealed class TestUsers
{
    public const string Password = "Cantina-Test-1";

    public required TestUser Admin { get; init; }
    public required IReadOnlyList<TestUser> Customers { get; init; }

    public TestUser Customer => Customers[0];

    public static async Task<TestUsers> CreateAsync(CustomWebApplicationFactory factory, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient();

        // There is no admin registration, so the admin registers as a customer and is promoted in the database before logging in.
        var admin = await RegisterAsync(client, "Chalmun", cancellationToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CantinaDbContext>().Users
                .Where(u => u.Id == admin.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Role, UserRole.Admin), cancellationToken);
        }

        var customers = new List<TestUser>();
        foreach (var name in new[] { "Han Solo", "Leia Organa", "Lando Calrissian" })
        {
            var customer = await RegisterAsync(client, name, cancellationToken);
            customers.Add(customer with { AccessToken = await LoginAsync(client, customer.Email, Password, cancellationToken) });
        }

        return new TestUsers
        {
            Admin = admin with { AccessToken = await LoginAsync(client, admin.Email, Password, cancellationToken) },
            Customers = customers,
        };
    }

    public static string UniqueEmail(string prefix = "patron") => $"{prefix}-{Guid.NewGuid():N}@cantina.example";

    private static async Task<TestUser> RegisterAsync(HttpClient client, string name, CancellationToken cancellationToken)
    {
        var request = new RegisterRequest(name, UniqueEmail(), Password);
        var response = await client.PostJsonAsync("/api/auth/register", request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Registering {name} returned {(int)response.StatusCode}.");
        }

        var user = await response.ReadJsonAsync<UserResponse>(cancellationToken);
        return new TestUser(user.Id, user.Name, user.Email, Password, AccessToken: string.Empty);
    }

    public static async Task<string> LoginAsync(HttpClient client, string email, string password, CancellationToken cancellationToken)
    {
        var response = await client.PostJsonAsync("/api/auth/login", new LoginRequest(email, password), cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Logging in {email} returned {(int)response.StatusCode}.");
        }

        return (await response.ReadJsonAsync<AccessTokenResponse>(cancellationToken)).AccessToken;
    }
}
