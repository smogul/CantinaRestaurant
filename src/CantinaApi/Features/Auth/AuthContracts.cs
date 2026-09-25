using System.ComponentModel.DataAnnotations;
using CantinaApi.Data.Entities;

namespace CantinaApi.Features.Auth;

// No role property, so a role sent by the client is dropped during binding.
public sealed record RegisterRequest(
    [property: Required, MaxLength(User.NameMaxLength)] string Name,
    [property: Required, EmailAddress, MaxLength(User.EmailMaxLength)] string Email,
    [property: Required, StringLength(RegisterRequest.PasswordMaxLength, MinimumLength = RegisterRequest.PasswordMinLength),
        RegularExpression(@"^(?=.*\p{L})(?=.*\d).*$", ErrorMessage = "The Password field must contain at least one letter and one digit.")]
    string Password)
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
}

// Login only checks presence so the rules for valid passwords are not revealed to guessers.
public sealed record LoginRequest(
    [property: Required] string Email,
    [property: Required] string Password);

public sealed record UserResponse(Guid Id, string Name, string Email, UserRole Role);

public sealed record AccessTokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);
