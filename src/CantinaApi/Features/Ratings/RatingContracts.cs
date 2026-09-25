using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using CantinaApi.Data.Entities;

namespace CantinaApi.Features.Ratings;

// No user id property: the rater always comes from the token, so a userId in the body is dropped.
public sealed record CreateRatingRequest(
    [property: Required, Range(Rating.MinStars, Rating.MaxStars)] int? Stars,
    [property: MaxLength(Rating.CommentMaxLength)] string? Comment);

// Shows the reviewer's name only; emails stay private.
[ImmutableObject(true)]
public sealed record RatingResponse(
    Guid Id,
    Guid MenuItemId,
    string ReviewerName,
    int Stars,
    string? Comment,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public static class RatingMappings
{
    public static readonly Expression<Func<Rating, RatingResponse>> ToResponse = r =>
        new RatingResponse(r.Id, r.MenuItemId, r.User.Name, r.Stars, r.Comment, r.CreatedAtUtc, r.UpdatedAtUtc);
}
