using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Data;

public sealed class CantinaDbContext(DbContextOptions<CantinaDbContext> options) : DbContext(options);
