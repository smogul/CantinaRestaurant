using CantinaApi.Features.Ratings;

namespace CantinaApi.Tests.Builders;

public sealed class CreateRatingRequestBuilder
{
    private int? _stars = 5;
    private string? _comment = "Worth the trip to Mos Eisley.";

    public CreateRatingRequestBuilder WithStars(int? stars)
    {
        _stars = stars;
        return this;
    }

    public CreateRatingRequestBuilder WithComment(string? comment)
    {
        _comment = comment;
        return this;
    }

    public CreateRatingRequest Build() => new(_stars, _comment);
}
