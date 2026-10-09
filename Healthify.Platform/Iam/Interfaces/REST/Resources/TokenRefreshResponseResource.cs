namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>IAM-4. What a refresh returns: a new access token and the refresh token that replaces the one sent.</summary>
public record TokenRefreshResponseResource(string Token, string RefreshToken, DateTimeOffset ExpiresAt)
{
    /// <summary>New bearer token for the same session, with the same role claim.</summary>
    public string Token { get; init; } = Token;

    /// <summary>The next refresh token. The one sent is no longer valid; sending it again ends the session.</summary>
    public string RefreshToken { get; init; } = RefreshToken;

    /// <summary>When <see cref="Token" /> expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; } = ExpiresAt;
}
