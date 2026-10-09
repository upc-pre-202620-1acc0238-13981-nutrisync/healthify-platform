namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>
///     An account as the platform exposes it. Read model: Welcome Screen. The password hash and the
///     lockout counter never leave the context.
/// </summary>
public record UserResource(
    int UserId,
    string Email,
    string Role,
    DateTimeOffset? CreatedAt,
    string GivenNames = "",
    string FamilyNames = "",
    string FullName = "",
    string PreferredLanguage = "es")
{
    /// <summary>Identifier of the account.</summary>
    public int UserId { get; init; } = UserId;

    /// <summary>Email address of the account.</summary>
    public string Email { get; init; } = Email;

    /// <summary>Role held by the account: Patient or Practitioner.</summary>
    public string Role { get; init; } = Role;

    /// <summary>Moment the account was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; } = CreatedAt;

    /// <summary>IAM-1. Given names of the person ("María José").</summary>
    public string GivenNames { get; init; } = GivenNames;

    /// <summary>IAM-1. Family names of the person ("Flores Quispe"). Empty on accounts created before IAM-1.</summary>
    public string FamilyNames { get; init; } = FamilyNames;

    /// <summary>IAM-1. Given names and family names together ("María José Flores Quispe").</summary>
    public string FullName { get; init; } = FullName;

    /// <summary>IAM-3. Interface language of the account: es or en.</summary>
    public string PreferredLanguage { get; init; } = PreferredLanguage;
}
