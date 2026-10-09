namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>Subflow 1.1 - Account Registration. Registering grants access to nothing on its own.</summary>
/// <remarks>
///     IAM-1 appends the given names and family names. They are required (the person name is validated
///     first and missing parts report NameRequired); the defaults only keep the positional contract
///     additive.
/// </remarks>
public record RegisterAccountCommand(
    string Email,
    string Password,
    string Role,
    string? GivenNames = null,
    string? FamilyNames = null);
