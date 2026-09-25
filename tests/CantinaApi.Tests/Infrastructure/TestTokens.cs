using CantinaApi.Common.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CantinaApi.Tests.Infrastructure;

// Mints tokens outside the API so tests can craft expired or foreign-signed ones.
public static class TestTokens
{
    public static string Create(
        TestUser user,
        string role,
        DateTime notBeforeUtc,
        DateTime expiresUtc,
        string signingKey = CustomWebApplicationFactory.SigningKey) =>
        new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = CustomWebApplicationFactory.Issuer,
            Audience = CustomWebApplicationFactory.Audience,
            IssuedAt = notBeforeUtc,
            NotBefore = notBeforeUtc,
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(JwtTokenService.CreateSigningKey(signingKey), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.Subject] = user.Id.ToString(),
                [ClaimNames.Email] = user.Email,
                [ClaimNames.Name] = user.Name,
                [ClaimNames.Role] = role,
                [ClaimNames.TokenId] = Guid.NewGuid().ToString(),
            },
        });
}
