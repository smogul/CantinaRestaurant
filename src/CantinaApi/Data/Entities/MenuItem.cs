namespace CantinaApi.Data.Entities;

public sealed class MenuItem
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int ImageUrlMaxLength = 2048;

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public MenuItemType Type { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Stored rating stats, recalculated from Ratings on every rating write so reads skip the aggregation.
    public decimal? AverageRating { get; set; }
    public int RatingCount { get; set; }

    public List<Rating> Ratings { get; set; } = [];
}
