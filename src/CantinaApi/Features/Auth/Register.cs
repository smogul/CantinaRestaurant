using CantinaApi.Common;
using CantinaApi.Common.Auth;
using CantinaApi.Common.RateLimiting;
using CantinaApi.Data;
using CantinaApi.Data.Configurations;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CantinaApi.Features.Auth;

public static class Register
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/register", HandleAsync)
            .WithName(nameof(Register))
            .WithSummary("Register a customer account.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Register);

    private static async Task<Results<Created<UserResponse>, ProblemHttpResult>> HandleAsync(
        RegisterRequest request, CantinaDbContext db, PasswordHasher passwordHasher, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        // The pre-check keeps ordinary conflicts out of the error logs; the unique index still catches races.
        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return EmailTaken();
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = UserRole.Customer,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UserConfiguration.UniqueEmailIndex,
        })
        {
            return EmailTaken();
        }

        // There is no endpoint to read a user back yet, so no Location header is sent.
        return TypedResults.Created((string?)null, new UserResponse(user.Id, user.Name, user.Email, user.Role));
    }

    private static ProblemHttpResult EmailTaken() => Problems.Conflict("An account with this email already exists.");
}
