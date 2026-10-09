using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Services;
using Microsoft.IdentityModel.Tokens;

namespace Healthify.Platform.Iam.Infrastructure.Tokens.JWT;

/// <summary>
///     Issues the HMAC-SHA256 session token. Its parameters mirror exactly what the bearer handler
///     in the composition root validates.
/// </summary>
public class JwtTokenService(IConfiguration configuration) : ITokenService
{
    /// <summary>IAM-3. Claim with the preferred language of the account (es or en).</summary>
    public const string LanguageClaimType = "lang";

    public string GenerateToken(User user, UserSession session)
    {
        return GenerateToken(user, session, DateTimeOffset.UtcNow, Guid.NewGuid().ToString());
    }

    public string GenerateToken(User user, UserSession session, DateTimeOffset issuedAt, string tokenId)
    {
        var settings = configuration.GetSection("TokenSettings");

        var secret = settings["Secret"]
                     ?? throw new InvalidOperationException("TokenSettings:Secret is not configured.");
        var issuer = settings["Issuer"] ?? "healthify-platform";
        var audience = settings["Audience"] ?? "healthify-clients";
        var expiresInMinutes = ExpiresInMinutes(settings);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);

        // Business rule: Role Immutable Per Session (Subflow 1.2). The role is stamped into the token
        // once; changing it means authenticating again and obtaining a different token.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.Value.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email.Value),
            new("email", user.Email.Value),
            new(ClaimTypes.Role, session.RoleClaim.Value),
            new("sessionId", session.Id.Value.ToString()),
            new(LanguageClaimType, user.PreferredLanguage.Value),
            new(JwtRegisteredClaimNames.Jti, tokenId)
        };

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore: issuedAt.UtcDateTime,
            expires: issuedAt.UtcDateTime.AddMinutes(expiresInMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTimeOffset ExpiresAt(DateTimeOffset issuedAt)
    {
        return issuedAt.AddMinutes(ExpiresInMinutes(configuration.GetSection("TokenSettings")));
    }

    private static int ExpiresInMinutes(IConfigurationSection settings)
    {
        return int.TryParse(settings["ExpiresInMinutes"], out var minutes) ? minutes : 1440;
    }
}
