using CantinaApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace CantinaApi.Tests.Auth;

[Collection(nameof(ApiCollection))]
public sealed class StartupTests(CustomWebApplicationFactory factory)
{
    [Fact]
    public void App_WithShortSigningKey_FailsToStart()
    {
        using var shortKeyFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("Jwt:SigningKey", new string('k', 16)));

        var exception = Assert.ThrowsAny<Exception>(() => shortKeyFactory.CreateClient());

        var validationError = Assert.IsType<OptionsValidationException>(exception.GetBaseException());
        Assert.Contains(validationError.Failures, failure => failure.Contains("Jwt:SigningKey"));
    }
}
