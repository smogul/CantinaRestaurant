using CantinaApi.Common.Auth;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Features.Auth;

public static class Login
{
    private const string InvalidCredentials = "Invalid credentials";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/login", HandleAsync)
            .WithName(nameof(Login))
            .WithSummary("Exchange an email and password for a bearer token.")
            .AllowAnonymous();

    private static async Task<Results<Ok<AccessTokenResponse>, ProblemHttpResult>> HandleAsync(
        LoginRequest request, CantinaDbContext db, PasswordHasher passwordHasher, JwtTokenService tokenService, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            passwordHasher.VerifyAgainstDummyHash(request.Password);
            return InvalidCredentialsProblem();
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return InvalidCredentialsProblem();
        }

        var token = tokenService.CreateToken(user);
        return TypedResults.Ok(new AccessTokenResponse(token.AccessToken, "Bearer", token.ExpiresAtUtc));
    }

    // Unknown email and wrong password get the same response so callers cannot tell which accounts exist.
    private static ProblemHttpResult InvalidCredentialsProblem() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: InvalidCredentials,
        detail: InvalidCredentials);
}
