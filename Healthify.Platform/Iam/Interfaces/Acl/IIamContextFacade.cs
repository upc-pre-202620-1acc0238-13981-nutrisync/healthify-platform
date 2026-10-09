namespace Healthify.Platform.Iam.Interfaces.Acl;

/// <summary>Flat DTO exposed to other bounded contexts - primitives only.</summary>
/// <remarks>
///     IAM-1 appends the given names and family names. Accounts created before IAM-1 carry the local part
///     of their email as given names and an empty family name.
/// </remarks>
public record UserIdentityItem(
    int UserId,
    string Email,
    string Role,
    string GivenNames = "",
    string FamilyNames = "")
{
    /// <summary>"Ana Flores". What every listing shows beside a patient.</summary>
    public string FullName => $"{GivenNames} {FamilyNames}".Trim();

    /// <summary>The letter of the avatar, or '?' when there is no name to take it from.</summary>
    public char Initial => GivenNames.Length > 0 ? char.ToUpperInvariant(GivenNames[0]) : '?';
}

/// <summary>
///     Public ACL contract of the Iam bounded context. Other contexts consume identity data through
///     this facade without coupling to the domain model: every parameter and return value is a
///     primitive or a primitives-only DTO declared here, never a command, aggregate or entity.
///     Every method degrades gracefully, returning null or false instead of throwing.
/// </summary>
/// <remarks>
///     Iam relates to the rest of the platform as a Conformist: the role claim travels inside the
///     session token, which is infrastructure. This facade covers the occasional server-side check
///     that needs the account behind an identifier.
/// </remarks>
public interface IIamContextFacade
{
    /// <summary>Returns null when the account does not exist or the lookup fails.</summary>
    Task<UserIdentityItem?> GetUserById(int userId, CancellationToken ct = default);

    /// <summary>
    ///     IAM-1. Several accounts in one query, keyed by id, so that a listing (roster, inbox, agenda) does not
    ///     read one account per row. Ids that do not exist are absent; empty when the lookup fails.
    /// </summary>
    Task<IReadOnlyDictionary<int, UserIdentityItem>> GetUsersByIds(IEnumerable<int> userIds,
        CancellationToken ct = default);

    /// <summary>
    ///     IAM-3. The interface language of the account (<c>es</c> or <c>en</c>), for the texts an AI
    ///     function of the owning context writes to that person. Null when the account does not exist or the lookup fails.
    /// </summary>
    Task<string?> GetPreferredLanguage(int userId, CancellationToken ct = default);

    /// <summary>Returns false when the account does not exist or the lookup fails.</summary>
    Task<bool> IsPractitioner(int userId, CancellationToken ct = default);

    /// <summary>Returns false when the account does not exist or the lookup fails.</summary>
    Task<bool> IsPatient(int userId, CancellationToken ct = default);
}
