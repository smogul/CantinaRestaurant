using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CantinaApi.Data.Configurations;

public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    // Created in the migration with raw SQL because EF cannot model an index on lower("Name").
    public const string UniqueNameIndex = "IX_MenuItems_Type_LowerName";

    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.Property(m => m.Name).HasMaxLength(MenuItem.NameMaxLength).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(MenuItem.DescriptionMaxLength).IsRequired();
        builder.Property(m => m.Price).HasColumnType("numeric(10,2)");
        builder.Property(m => m.ImageUrl).HasMaxLength(MenuItem.ImageUrlMaxLength).IsRequired();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.IsDeleted).HasDefaultValue(false);

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
