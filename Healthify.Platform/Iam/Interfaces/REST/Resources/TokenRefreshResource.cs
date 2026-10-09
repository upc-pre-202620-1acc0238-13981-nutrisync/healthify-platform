namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>IAM-4. Body of <c>POST /authentication/token-refreshes</c>.</summary>
public record TokenRefreshResource(string? RefreshToken)
{
    /// <summary>The refresh token received at sign-in or at the previous refresh. Valid for one use.</summary>
    public string? RefreshToken { get; init; } = RefreshToken;
}
