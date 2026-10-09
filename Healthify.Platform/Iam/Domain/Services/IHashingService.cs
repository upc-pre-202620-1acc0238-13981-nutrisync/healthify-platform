using Healthify.Platform.Iam.Domain.Model.ValueObjects;

namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>
///     Password hashing and verification. The external service the event storming calls Auth Provider
///     is implemented inside the platform: there is no third-party identity provider.
/// </summary>
public interface IHashingService
{
    string Hash(Password password);

    /// <summary>Backs the business rule Valid Credentials Required (Subflow 1.2).</summary>
    bool Verify(string plainPassword, string passwordHash);
}
