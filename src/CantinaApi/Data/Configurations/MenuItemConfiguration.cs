using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CantinaApi.Data.Configurations;

public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    // Created in the migration with raw SQL because EF cannot model an index on lower("Name").
    public const string UniqueNameIndex = "IX_MenuItems_Type_LowerName";

    // Soft-deleted rows are never listed or searched, so the indexes leave them out.
    private const string LiveItemsFilter = "\"IsDeleted\" = false";

    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.Property(m => m.Name).HasMaxLength(MenuItem.NameMaxLength).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(MenuItem.DescriptionMaxLength).IsRequired();
        builder.Property(m => m.Price).HasColumnType("numeric(10,2)");
        builder.Property(m => m.ImageUrl).HasMaxLength(MenuItem.ImageUrlMaxLength).IsRequired();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.IsDeleted).HasDefaultValue(false);

        builder.Property(m => m.AverageRating).HasColumnType("numeric(3,2)");
        builder.Property(m => m.RatingCount).HasDefaultValue(0);

        // Every list and search orders live items by Name then Id, so this index returns pages in order without sorting the table.
        builder.HasIndex(m => new { m.Name, m.Id }, "IX_MenuItems_Name_Id_Live").HasFilter(LiveItemsFilter);

        // Trigram indexes let ILIKE contains searches find candidate rows without testing every row.
        builder.HasIndex(m => m.Name, "IX_MenuItems_Name_Trigram")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasFilter(LiveItemsFilter);
        builder.HasIndex(m => m.Description, "IX_MenuItems_Description_Trigram")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasFilter(LiveItemsFilter);

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
