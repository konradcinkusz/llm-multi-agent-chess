namespace Chess.Api.Ratings;

public static class RatingExtensions
{
    public static IHostApplicationBuilder AddRatings(this IHostApplicationBuilder builder) => builder;
    public static IEndpointRouteBuilder MapRatingEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
