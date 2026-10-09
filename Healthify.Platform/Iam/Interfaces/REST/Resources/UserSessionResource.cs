namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>A session in the account history. Read model: Session Context.</summary>
public record UserSessionResource(
    int SessionId,
    int UserId,
    string RoleClaim,
    string? NavigationShell,
    DateTimeOffset StartedAt,
    DateTimeOffset? TerminatedAt,
    bool IsActive)
{
    /// <summary>Identifier of the session.</summary>
    public int SessionId { get; init; } = SessionId;

    /// <summary>Identifier of the owning account.</summary>
    public int UserId { get; init; } = UserId;

    /// <summary>Role claim this session carried. Immutable for its whole lifetime.</summary>
    public string RoleClaim { get; init; } = RoleClaim;

    /// <summary>Shell selected for this session, or null while none has been selected.</summary>
    public string? NavigationShell { get; init; } = NavigationShell;

    /// <summary>Moment the session started.</summary>
    public DateTimeOffset StartedAt { get; init; } = StartedAt;

    /// <summary>Moment the session was terminated, or null while it is still open.</summary>
    public DateTimeOffset? TerminatedAt { get; init; } = TerminatedAt;

    /// <summary>Whether the session is still open.</summary>
    public bool IsActive { get; init; } = IsActive;
}
