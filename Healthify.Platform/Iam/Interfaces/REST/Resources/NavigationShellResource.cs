namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>Read model: App Shell. Which shell the client mounts for a session.</summary>
public record NavigationShellResource(
    int SessionId,
    string? RoleClaim,
    string? NavigationShell,
    bool IsActive)
{
    /// <summary>Identifier of the session.</summary>
    public int SessionId { get; init; } = SessionId;

    /// <summary>
    ///     Role the session still grants, or null once it has been terminated: the claim is
    ///     discarded on sign out.
    /// </summary>
    public string? RoleClaim { get; init; } = RoleClaim;

    /// <summary>Shell to mount: PatientShell or PractitionerShell. Null while none is selected.</summary>
    public string? NavigationShell { get; init; } = NavigationShell;

    /// <summary>Whether the session is still open.</summary>
    public bool IsActive { get; init; } = IsActive;
}
