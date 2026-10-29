namespace Healthify.Platform.Iam.Application.Internal;

/// <summary>
///     Application DTO carrying what a successful code verification produced. Not a domain type and not an
///     HTTP resource.
/// </summary>
/// <param name="ResetToken">Handed to the client once; only its hash is stored.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
public record PasswordResetVerification(string ResetToken, DateTimeOffset ExpiresAt);
