using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using CantinaApi.Data.Entities;

namespace CantinaApi.Features.Ratings;

public sealed record CreateRatingRequest(
    [property: Required, Range(Rating.MinStars, Rating.MaxStars)] int? Stars,
    [property: MaxLength(Rating.CommentMaxLength)] string? Comment);

public sealed record RatingResponse(Guid Id, Guid MenuItemId, int Stars, string? Comment, DateTime CreatedAtUtc);

public static class RatingMappings
{
    public static readonly Expression<Func<Rating, RatingResponse>> ToResponse = r =>
        new RatingResponse(r.Id, r.MenuItemId, r.Stars, r.Comment, r.CreatedAtUtc);

    private static readonly Func<Rating, RatingResponse> ToResponseCompiled = ToResponse.Compile();

    public static RatingResponse ToResponseDto(this Rating rating) => ToResponseCompiled(rating);
}
