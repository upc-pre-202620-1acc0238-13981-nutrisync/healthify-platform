using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;

namespace Healthify.Platform.Iam.Domain.Model.Aggregates;

/// <summary>
///     An account in the platform. It answers "who you are" and nothing else: being linked to a
///     practitioner, and with what consent, is the question CareRelationship answers.
/// </summary>
/// <remarks>
///     Registering grants access to nothing. A Patient without a Care Link sees no targets, has no
///     diary and cannot log. The real entry point is the invitation, not the registration.
/// </remarks>
public partial class User
{
    /// <summary>Business rule: Lockout After Five Failed Attempts (Subflow 1.2).</summary>
    private const int MaxFailedSignInAttempts = 5;

    /// <summary>Required by EF Core.</summary>
    protected User()
    {
    }

    public User(RegisterAccountCommand command, string passwordHash)
    {
        // Business rule: Role Declared At Registration (Iam, Subflow 1.1)
        if (string.IsNullOrWhiteSpace(command.Role))
            throw new InvalidOperationException("The role must be declared at registration.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new InvalidOperationException("A password hash is required to create an account.");

        Email = new Email(command.Email);
        Role = new Role(command.Role);

        // IAM-1. Required at registration; the value object rejects a missing part.
        var name = new PersonName(command.GivenNames, command.FamilyNames);
        GivenNames = name.GivenNames;
        FamilyNames = name.FamilyNames;
        PasswordHash = passwordHash;
        PreferredLanguage = PreferredLanguage.Default;
        FailedSignInAttempts = 0;
        LockedOutAt = null;
    }

    public UserId Id { get; private set; } = null!;
    public Email Email { get; private set; } = null!;

    /// <summary>IAM-1. "María José". Validated as a <see cref="PersonName" /> when it is written.</summary>
    /// <remarks>
    ///     NOTE: technical field. Stored as two flat columns instead of an owned <see cref="PersonName" />:
    ///     accounts created before IAM-1 were backfilled with the local part of their email (which may
    ///     contain digits) and an empty family name, and materialising the value object on every read would
    ///     throw for them.
    /// </remarks>
    public string GivenNames { get; private set; } = string.Empty;

    /// <summary>IAM-1. "Flores Quispe". Empty only on accounts created before IAM-1.</summary>
    public string FamilyNames { get; private set; } = string.Empty;

    /// <summary>IAM-1. "María José Flores Quispe", without a trailing space for pre-IAM-1 accounts.</summary>
    public string FullName => $"{GivenNames} {FamilyNames}".Trim();
    public string PasswordHash { get; private set; } = null!;

    /// <summary>Declared at registration and never mutated afterwards.</summary>
    public Role Role { get; private set; } = null!;

    /// <summary>IAM-3. The interface language of the account; <c>es</c> until changed.</summary>
    public PreferredLanguage PreferredLanguage { get; private set; } = PreferredLanguage.Default;

    public int FailedSignInAttempts { get; private set; }
    public DateTimeOffset? LockedOutAt { get; private set; }

    /// <summary>
    ///     A lockout was recorded and not cleared yet. Whether it still applies depends on the time:
    ///     <see cref="IsLockedOutAt" />.
    /// </summary>
    public bool IsLockedOut => LockedOutAt is not null;

    /// <summary>
    ///     Business rule: Lockout After Five Failed Attempts (Subflow 1.2), and Lockout Is Temporary (IAM-5): it
    ///     applies while <paramref name="now" /> is before <c>LockedOutAt + lockoutDuration</c>.
    /// </summary>
    public bool IsLockedOutAt(DateTimeOffset now, TimeSpan lockoutDuration)
    {
        return LockedOutAt is { } lockedOutAt && lockedOutAt + lockoutDuration > now;
    }

    /// <summary>
    ///     Records a rejected credential check and locks the account once the threshold is reached.
    /// </summary>
    /// <remarks>
    ///     IAM-5. A failure after an expired lockout starts a new count: the account gets five fresh attempts.
    /// </remarks>
    public void RegisterFailedSignInAttempt(DateTimeOffset now, TimeSpan lockoutDuration)
    {
        if (IsLockedOutAt(now, lockoutDuration)) return;

        // Business rule: Lockout Is Temporary (IAM-5)
        if (LockedOutAt is not null)
        {
            FailedSignInAttempts = 0;
            LockedOutAt = null;
        }

        FailedSignInAttempts++;

        // Business rule: Lockout After Five Failed Attempts (Iam, Subflow 1.2)
        if (FailedSignInAttempts >= MaxFailedSignInAttempts) LockedOutAt = now;
    }

    /// <summary>IAM-3. «El idioma se guarda en el dispositivo y en la cuenta».</summary>
    public void ChangePreferredLanguage(PreferredLanguage language)
    {
        PreferredLanguage = language ?? throw new ArgumentException("A language is required.", nameof(language));
    }

    /// <summary>Clears the failure counter after a credential check that succeeded.</summary>
    public void RegisterSuccessfulSignIn()
    {
        FailedSignInAttempts = 0;
        LockedOutAt = null;
    }

    /// <summary>Opens a session carrying this account immutable role claim.</summary>
    public UserSession StartSession()
    {
        // Business rule: Role Immutable Per Session (Iam, Subflow 1.2)
        // The claim is copied into the session at creation time and the session exposes no mutator
        // for it, so a role change can only be expressed by authenticating again.
        return new UserSession(Id.Value, Role);
    }
}
