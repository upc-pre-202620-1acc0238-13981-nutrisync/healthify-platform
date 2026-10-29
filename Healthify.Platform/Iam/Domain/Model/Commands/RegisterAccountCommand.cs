namespace Healthify.Platform.Iam.Domain.Model.Commands;

/// <summary>Subflow 1.1 - Account Registration. Registering grants access to nothing on its own.</summary>
/// <remarks>
///     Given names and family names are required: the person name is validated first and a missing part
///     reports NameRequired. Their default values exist only so older positional callers still compile.
/// </remarks>
public record RegisterAccountCommand(
    string Email,
    string Password,
    string Role,
    string? GivenNames = null,
    string? FamilyNames = null);
