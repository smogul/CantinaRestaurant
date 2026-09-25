using System.Net;
using System.Text.Json;
using CantinaApi.Data.Entities;
using CantinaApi.Features.Auth;
using CantinaApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class RegisterTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    private const string RegisterRoute = "/api/auth/register";

    [Fact]
    public async Task Register_ReturnsCreatedCustomerWithoutPasswordHash()
    {
        var email = TestUsers.UniqueEmail();

        var response = await AnonymousClient.PostJsonAsync(RegisterRoute, new RegisterRequest("Rey", $"  {email.ToUpperInvariant()} ", "Scavenger1"), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(["id", "name", "email", "role"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Customer", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Register_IgnoresRoleInBody()
    {
        var email = TestUsers.UniqueEmail();

        var response = await AnonymousClient.PostJsonAsync(
            RegisterRoute, new { name = "Poe Dameron", email, password = "BlackOne1", role = "Admin" }, CancellationToken);

        var user = await response.ReadJsonAsync<UserResponse>(CancellationToken);
        Assert.Equal(UserRole.Customer, user.Role);
        var storedRole = await QueryDatabaseAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.Role).SingleAsync(CancellationToken));
        Assert.Equal(UserRole.Customer, storedRole);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_WithTakenEmail_ReturnsConflict(bool changeCase)
    {
        var email = TestUsers.UniqueEmail();
        await AnonymousClient.PostJsonAsync(RegisterRoute, new RegisterRequest("Finn", email, "Stormtrooper2187"), CancellationToken);

        var duplicate = changeCase ? email.ToUpperInvariant() : email;
        var response = await AnonymousClient.PostJsonAsync(RegisterRoute, new RegisterRequest("Finn Again", duplicate, "Stormtrooper2187"), CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, CancellationToken);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("12345678")]
    [InlineData("abc123")]
    [InlineData("")]
    public async Task Register_WithWeakPassword_ReturnsValidationProblem(string password)
    {
        var response = await AnonymousClient.PostJsonAsync(RegisterRoute, new RegisterRequest("Rose Tico", TestUsers.UniqueEmail(), password), CancellationToken);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
        Assert.Contains(problem.ErrorKeys(), key => string.Equals(key, "password", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Register_WithInvalidEmail_ReturnsValidationProblem(string email)
    {
        var response = await AnonymousClient.PostJsonAsync(RegisterRoute, new RegisterRequest("Maz Kanata", email, "Takodana1"), CancellationToken);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, CancellationToken);
        Assert.Contains(problem.ErrorKeys(), key => string.Equals(key, "email", StringComparison.OrdinalIgnoreCase));
    }
}
