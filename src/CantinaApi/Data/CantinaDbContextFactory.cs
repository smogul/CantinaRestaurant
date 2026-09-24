using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CantinaApi.Data;

// Lets dotnet ef create migrations without starting the app or needing a live database.
public sealed class CantinaDbContextFactory : IDesignTimeDbContextFactory<CantinaDbContext>
{
    public CantinaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CantinaDb")
            ?? "Host=localhost;Database=cantina";

        var options = new DbContextOptionsBuilder<CantinaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CantinaDbContext(options);
    }
}
