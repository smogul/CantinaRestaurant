using System.Text;
using CantinaApi.Data.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CantinaApi.Common.Auth;

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly JwtOptions _options = options.Value;
    private readonly SigningCredentials _signingCredentials = new(CreateSigningKey(options.Value.SigningKey), SecurityAlgorithms.HmacSha256);

    public static SymmetricSecurityKey CreateSigningKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public IssuedToken CreateToken(User user)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddMinutes(_options.LifetimeMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAtUtc,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.Subject] = user.Id.ToString(),
                [ClaimNames.Email] = user.Email,
                [ClaimNames.Name] = user.Name,
                [ClaimNames.Role] = user.Role.ToString(),
                [ClaimNames.TokenId] = Guid.NewGuid().ToString(),
            },
        });

        return new IssuedToken(token, expiresAtUtc);
    }
}
