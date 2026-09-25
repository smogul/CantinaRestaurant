using CantinaApi.Common.Auth;
using CantinaApi.Common.Http;
using CantinaApi.Common.RateLimiting;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CantinaApi.Features.Auth;

public static class Login
{
    private const string InvalidCredentials = "Invalid credentials";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/login", HandleAsync)
            .WithName(nameof(Login))
            .WithSummary("Exchange an email and password for a bearer token.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login);

    // Passwords never reach a log statement on any path.
    private static async Task<Results<Ok<AccessTokenResponse>, ProblemHttpResult>> HandleAsync(
        LoginRequest request,
        HttpContext httpContext,
        CantinaDbContext db,
        PasswordHasher passwordHasher,
        JwtTokenService tokenService,
        IOptions<BruteForceOptions> bruteForceOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(Login));
        var clientIp = ClientIp.Get(httpContext);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var lockout = bruteForceOptions.Value.Lockout;

        if (user is null)
        {
            passwordHasher.VerifyAgainstDummyHash(request.Password);
            await RecordFailedAttemptAsync(db, userId: null, lockout, now, cancellationToken);
            logger.LogWarning("Failed login for unknown email from {ClientIp}", clientIp);
            return InvalidCredentialsProblem();
        }

        // A locked account gets the same answer and cost as a wrong password, and the attempt neither counts nor extends the lockout.
        if (user.LockoutEndUtc > now)
        {
            passwordHasher.VerifyAgainstDummyHash(request.Password);
            await RecordFailedAttemptAsync(db, user.Id, lockout, now, cancellationToken);
            logger.LogWarning("Login attempt on locked account {UserId} from {ClientIp}", user.Id, clientIp);
            return InvalidCredentialsProblem();
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            var lockedUntil = await RecordFailedAttemptAsync(db, user.Id, lockout, now, cancellationToken);
            if (lockedUntil is not null)
            {
                logger.LogWarning("Account locked {UserId} from {ClientIp} until {LockoutEndUtc}", user.Id, clientIp, lockedUntil);
            }
            else
            {
                logger.LogInformation("Failed login for {UserId} from {ClientIp}", user.Id, clientIp);
            }

            return InvalidCredentialsProblem();
        }

        if (user.FailedLoginAttempts != 0 || user.LockoutEndUtc is not null)
        {
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(u => u.FailedLoginAttempts, 0)
                        .SetProperty(u => u.LockoutEndUtc, (DateTime?)null),
                    cancellationToken);
        }

        var token = tokenService.CreateToken(user);
        return TypedResults.Ok(new AccessTokenResponse(token.AccessToken, "Bearer", token.ExpiresAtUtc));
    }

    // Every failure path runs these same two statements, matching no row for an unknown email or a locked account, so all failures cost the same database work.
    // Returns the lockout end when this attempt is the one that locked the account.
    private static async Task<DateTime?> RecordFailedAttemptAsync(
        CantinaDbContext db, Guid? userId, LockoutSettings lockout, DateTime now, CancellationToken cancellationToken)
    {
        var id = userId ?? Guid.Empty;
        var maxAttempts = lockout.MaxFailedAttempts;

        // Truncated to microseconds, the precision Postgres stores, so the read-back below can match it exactly.
        var lockoutEnd = now.AddMinutes(lockout.DurationMinutes);
        lockoutEnd = new DateTime(lockoutEnd.Ticks - (lockoutEnd.Ticks % 10), DateTimeKind.Utc);

        // One UPDATE increments the counter and, on the last allowed miss, locks and resets it, so concurrent misses cannot lose counts.
        // Every SET reads the row as it was before the statement, and the filter skips rows that are already locked.
        await db.Users
            .Where(u => u.Id == id && (u.LockoutEndUtc == null || u.LockoutEndUtc <= now))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        u => u.LockoutEndUtc,
                        u => u.FailedLoginAttempts + 1 >= maxAttempts ? lockoutEnd : u.LockoutEndUtc)
                    .SetProperty(
                        u => u.FailedLoginAttempts,
                        u => u.FailedLoginAttempts + 1 >= maxAttempts ? 0 : u.FailedLoginAttempts + 1),
                cancellationToken);

        var lockedByThisAttempt = await db.Users.AnyAsync(u => u.Id == id && u.LockoutEndUtc == lockoutEnd, cancellationToken);
        return lockedByThisAttempt ? lockoutEnd : null;
    }

    // Every failure returns the same body so callers cannot tell unknown, wrong and locked accounts apart.
    private static ProblemHttpResult InvalidCredentialsProblem() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: InvalidCredentials,
        detail: InvalidCredentials);
}
