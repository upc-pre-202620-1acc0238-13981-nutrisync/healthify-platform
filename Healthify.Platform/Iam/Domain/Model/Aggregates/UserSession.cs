using Healthify.Platform.Iam.Domain.Model.ValueObjects;

namespace Healthify.Platform.Iam.Domain.Model.Aggregates;

/// <summary>
///     One authenticated session. It carries the role claim that the rest of the platform reads from
///     the token, and the navigation shell the client mounts because of it.
/// </summary>
public partial class UserSession
{
    /// <summary>Required by EF Core.</summary>
    protected UserSession()
    {
    }

    /// <summary>Created through <see cref="User.StartSession" />, never directly.</summary>
    internal UserSession(int userId, Role roleClaim)
    {
        if (userId <= 0) throw new InvalidOperationException("A session requires the owning user identifier.");

        UserId = userId;
        RoleClaim = roleClaim;
        NavigationShell = null;
        StartedAt = DateTimeOffset.UtcNow;
        TerminatedAt = null;
    }

    public SessionId Id { get; private set; } = null!;

    /// <summary>Identifier of the owning account. Same bounded context, separate aggregate root.</summary>
    public int UserId { get; private set; }

    /// <summary>
    ///     Business rule: Role Immutable Per Session (Subflow 1.2). Assigned once by the constructor;
    ///     the aggregate deliberately exposes no way to change it.
    /// </summary>
    public Role RoleClaim { get; private set; } = null!;

    public NavigationShell? NavigationShell { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? TerminatedAt { get; private set; }

    public bool IsActive => TerminatedAt is null;

    /// <summary>
    ///     IAM-4. SHA-256 (hex) of the refresh token currently valid for this session. The token itself is never
    ///     stored. Null on sessions opened before IAM-4 and after sign-out.
    /// </summary>
    public string? RefreshTokenHash { get; private set; }

    /// <summary>IAM-4. When the current refresh token stops being accepted (30 days by default).</summary>
    public DateTimeOffset? RefreshTokenExpiresAt { get; private set; }

    /// <summary>
    ///     IAM-4. SHA-256 (hex) of the refresh token this session rotated away from. Presenting it again is
    ///     reuse: somebody else holds a copy, and the session is terminated.
    /// </summary>
    public string? PreviousRefreshTokenHash { get; private set; }

    /// <summary>
    ///     IAM-4. When the current refresh token was issued, to the second. It is the issue time of the access
    ///     token that went with it, which is what lets a retried refresh get the very same pair back.
    /// </summary>
    public DateTimeOffset? RefreshTokenRotatedAt { get; private set; }

    /// <summary>IAM-4. Whether <paramref name="tokenHash" /> is the current, unexpired refresh token.</summary>
    public bool AcceptsRefreshToken(string tokenHash, DateTimeOffset now)
    {
        return IsActive && RefreshTokenHash is not null && RefreshTokenHash == tokenHash
               && RefreshTokenExpiresAt > now;
    }

    /// <summary>IAM-4. Whether <paramref name="tokenHash" /> is the refresh token this session already rotated.</summary>
    public bool IsRotatedRefreshToken(string tokenHash)
    {
        return PreviousRefreshTokenHash is not null && PreviousRefreshTokenHash == tokenHash;
    }

    /// <summary>
    ///     IAM-4. Business rule: Refresh Retry Within Grace Is Not Reuse. The token just rotated away from is
    ///     presented again within <paramref name="grace" /> of the rotation, and the token it was rotated into
    ///     (<paramref name="successorHash" />) is still the current one, unused: the client lost the answer and
    ///     retried, or two requests raced. The rotation is replayed, not repeated.
    /// </summary>
    /// <param name="tokenHash">Hash of the presented token.</param>
    /// <param name="successorHash">Hash of the token the presented one rotates into.</param>
    /// <param name="now">The instant of the retry.</param>
    /// <param name="grace">How long after a rotation a retry is still a retry.</param>
    public bool CanReplayRotation(string tokenHash, string successorHash, DateTimeOffset now, TimeSpan grace)
    {
        return IsActive
               && IsRotatedRefreshToken(tokenHash)
               && RefreshTokenHash == successorHash
               && RefreshTokenRotatedAt is { } rotatedAt
               && now - rotatedAt <= grace
               && RefreshTokenExpiresAt > now;
    }

    /// <summary>
    ///     IAM-4. Issues a new refresh token for this session and invalidates the current one, which is kept
    ///     only as the rotated token, so that its reuse can be recognised.
    /// </summary>
    /// <param name="newTokenHash">SHA-256 (hex) of the new token.</param>
    /// <param name="expiresAt">When the new token stops being accepted.</param>
    /// <param name="rotatedAt">When it is issued; now when omitted. Kept to the second, as a token keeps it.</param>
    public void RotateRefreshToken(string newTokenHash, DateTimeOffset expiresAt, DateTimeOffset? rotatedAt = null)
    {
        // Business rule: Role Claim Discarded On Sign Out (Subflow 1.3). A terminated session issues nothing.
        if (!IsActive) throw new InvalidOperationException("A terminated session cannot issue a refresh token.");
        if (string.IsNullOrWhiteSpace(newTokenHash))
            throw new ArgumentException("The refresh token hash is required.", nameof(newTokenHash));
        if (newTokenHash == RefreshTokenHash)
            throw new ArgumentException("A rotation must issue a different token.", nameof(newTokenHash));

        var issuedAt = (rotatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();

        PreviousRefreshTokenHash = RefreshTokenHash;
        RefreshTokenHash = newTokenHash;
        RefreshTokenExpiresAt = expiresAt;
        RefreshTokenRotatedAt = issuedAt.AddTicks(-(issuedAt.Ticks % TimeSpan.TicksPerSecond));
    }

    /// <summary>
    ///     The role this session still grants. Business rule: Role Claim Discarded On Sign Out
    ///     (Subflow 1.3) - once terminated the session grants no role, while the record of which role
    ///     it carried is preserved for audit.
    /// </summary>
    public Role? ActiveRoleClaim => IsActive ? RoleClaim : null;

    /// <summary>Subflow 1.2 - Select Navigation Shell.</summary>
    public void SelectNavigationShell(NavigationShell shell)
    {
        // Business rule: Role Claim Discarded On Sign Out (Iam, Subflow 1.3)
        if (!IsActive)
            throw new InvalidOperationException("A terminated session cannot select a navigation shell.");

        // Business rule: One Shell Per Session (Iam, Subflow 1.2)
        if (NavigationShell is not null)
            throw new InvalidOperationException("This session already has a navigation shell.");

        // Business rule: Role Change Requires Re Authentication (Iam, Subflow 1.2)
        // The shell must match the claim frozen at sign-in. Wanting the other shell means
        // authenticating again, which is exactly what makes the role immutable per session.
        if (!shell.MatchesRole(RoleClaim))
            throw new ArgumentException(
                "The navigation shell does not match the role claim of this session.", nameof(shell));

        NavigationShell = shell;
    }

    /// <summary>Subflow 1.3 - Sign Out.</summary>
    public void Terminate()
    {
        // Business rule: Role Claim Discarded On Sign Out (Iam, Subflow 1.3)
        if (!IsActive) throw new InvalidOperationException("This session has already been terminated.");

        TerminatedAt = DateTimeOffset.UtcNow;

        // IAM-4: sign-out deletes the refresh token. The rotated hash goes too: once nothing can be
        // refreshed there is nothing left to detect.
        RefreshTokenHash = null;
        RefreshTokenExpiresAt = null;
        PreviousRefreshTokenHash = null;
        RefreshTokenRotatedAt = null;
    }
}
