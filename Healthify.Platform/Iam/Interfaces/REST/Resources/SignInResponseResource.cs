namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>Read model: Session Context. What a successful sign-in returns.</summary>
public record SignInResponseResource(
    int UserId,
    string Email,
    string Role,
    int SessionId,
    string Token,
    DateTimeOffset StartedAt,
    string GivenNames = "",
    string FamilyNames = "",
    string PreferredLanguage = "es",
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null)
{
    /// <summary>Identifier of the authenticated account.</summary>
    public int UserId { get; init; } = UserId;

    /// <summary>Email address of the authenticated account.</summary>
    public string Email { get; init; } = Email;

    /// <summary>Role claim frozen for this session: Patient or Practitioner.</summary>
    public string Role { get; init; } = Role;

    /// <summary>Identifier of the session that was opened.</summary>
    public int SessionId { get; init; } = SessionId;

    /// <summary>Bearer token. Carries the subject, the email, the role claim and the session id.</summary>
    public string Token { get; init; } = Token;

    /// <summary>Moment the session started.</summary>
    public DateTimeOffset StartedAt { get; init; } = StartedAt;

    /// <summary>IAM-1. Given names, so the app greets "Hola, María" without another call.</summary>
    public string GivenNames { get; init; } = GivenNames;

    /// <summary>IAM-1. Family names. Empty on accounts created before IAM-1.</summary>
    public string FamilyNames { get; init; } = FamilyNames;

    /// <summary>IAM-3. Interface language of the account: es or en.</summary>
    public string PreferredLanguage { get; init; } = PreferredLanguage;

    /// <summary>
    ///     IAM-4. Exchange it at <c>POST /authentication/token-refreshes</c> for a new token before or after
    ///     <see cref="ExpiresAt" />. Valid for one use and 30 days. Clients that ignore it keep working.
    /// </summary>
    public string? RefreshToken { get; init; } = RefreshToken;

    /// <summary>IAM-4. When <see cref="Token" /> expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; } = ExpiresAt;
}
