namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>IAM-2. <c>200</c> of <c>POST /authentication/password-reset-verifications</c>.</summary>
/// <param name="ResetToken">Sets the new password once. Keep it in memory only.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
public record PasswordResetVerificationResponseResource(string ResetToken, DateTimeOffset ExpiresAt);
