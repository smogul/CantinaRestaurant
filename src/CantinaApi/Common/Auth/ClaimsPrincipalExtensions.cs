using System.Security.Claims;

namespace CantinaApi.Common.Auth;

public static class ClaimsPrincipalExtensions
{
    // Only called on endpoints that require authentication, so a missing sub means a bug, not a bad request.
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimNames.Subject), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user has no valid sub claim.");
}
