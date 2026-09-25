using Microsoft.AspNetCore.Http.HttpResults;

namespace CantinaApi.Common;

public static class Problems
{
    public static ProblemHttpResult NotFound(string resource, Guid id) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"{resource} not found.",
        detail: $"No {resource.ToLowerInvariant()} exists with id '{id}'.");

    public static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Conflict.",
        detail: detail);
}
