using System.Net;
using System.Text.Json;
using CantinaApi.Tests.Infrastructure;

namespace CantinaApi.Tests;

[Collection(nameof(ApiCollection))]
public sealed class HealthCheckTests(CustomWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task GetHealth_ReturnsOkWithHealthyDatabase()
    {
        var response = await Client.GetAsync("/health", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
    }
}
