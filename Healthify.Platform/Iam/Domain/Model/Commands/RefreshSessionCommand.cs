namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>
///     IAM-4 - Refresh Session. Exchanges the current refresh token for a new access token and a new refresh
///     token; the presented one stops being valid. Reusing a rotated token terminates the session.
/// </summary>
/// <param name="RefreshToken">The opaque token the client received. Never logged.</param>
public record RefreshSessionCommand(string? RefreshToken);
