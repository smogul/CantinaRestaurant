using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Data;

public sealed class CantinaDbContext(DbContextOptions<CantinaDbContext> options) : DbContext(options)
{
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Rating> Ratings => Set<Rating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CantinaDbContext).Assembly);
    }
}
