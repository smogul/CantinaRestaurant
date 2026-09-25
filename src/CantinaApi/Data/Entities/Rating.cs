namespace CantinaApi.Data.Entities;

public sealed class Rating
{
    public const int MinStars = 1;
    public const int MaxStars = 5;
    public const int CommentMaxLength = 1000;

    public Guid Id { get; set; }
    public Guid MenuItemId { get; set; }
    public Guid? UserId { get; set; }
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public MenuItem MenuItem { get; set; } = null!;
}
