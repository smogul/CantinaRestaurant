using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CantinaApi.Data.Configurations;

public sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Ratings_Stars", $"\"Stars\" BETWEEN {Rating.MinStars} AND {Rating.MaxStars}"));

        builder.Property(r => r.Comment).HasMaxLength(Rating.CommentMaxLength);

        builder.HasOne(r => r.MenuItem)
            .WithMany(m => m.Ratings)
            .HasForeignKey(r => r.MenuItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves the newest-first ratings list and doubles as the foreign key index.
        builder.HasIndex(r => new { r.MenuItemId, r.CreatedAtUtc });

        // Mirrors the MenuItem filter so ratings of a deleted item are hidden but kept.
        builder.HasQueryFilter(r => !r.MenuItem.IsDeleted);
    }
}
