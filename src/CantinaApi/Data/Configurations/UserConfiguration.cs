using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CantinaApi.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string UniqueEmailIndex = "IX_Users_Email";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Name).HasMaxLength(User.NameMaxLength).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.FailedLoginAttempts).HasDefaultValue(0);

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName(UniqueEmailIndex);
    }
}
