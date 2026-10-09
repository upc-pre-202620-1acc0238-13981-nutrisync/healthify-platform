namespace Healthify.Platform.Iam.Interfaces.REST.Resources;

/// <summary>Payload of Subflow 1.1 - Account Registration.</summary>
public record SignUpResource(
    string Email,
    string Password,
    string Role,
    string? GivenNames = null,
    string? FamilyNames = null)
{
    /// <summary>Email address for the new account. Must be unique and a valid address.</summary>
    public string Email { get; init; } = Email;

    /// <summary>
    ///     Password. At least 8 characters, with an uppercase letter, a lowercase letter, a digit
    ///     and a special character.
    /// </summary>
    public string Password { get; init; } = Password;

    /// <summary>
    ///     Role declared at registration and immutable afterwards. Either Patient or Practitioner.
    /// </summary>
    public string Role { get; init; } = Role;

    /// <summary>IAM-1. Given names ("María José"). Required: up to 80 characters, no digits.</summary>
    public string? GivenNames { get; init; } = GivenNames;

    /// <summary>IAM-1. Family names ("Flores Quispe"). Required: up to 80 characters, no digits.</summary>
    public string? FamilyNames { get; init; } = FamilyNames;
}
