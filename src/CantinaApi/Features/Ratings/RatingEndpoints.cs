namespace CantinaApi.Features.Ratings;

public static class RatingEndpoints
{
    public static RouteGroupBuilder MapRatingEndpoints(this RouteGroupBuilder menuItems)
    {
        CreateRating.Map(menuItems);
        ListRatings.Map(menuItems);
        return menuItems;
    }
}
