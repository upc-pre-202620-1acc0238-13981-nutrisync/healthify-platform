using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Services;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Healthify.Platform.Iam.Infrastructure.Hashing.BCrypt;

/// <summary>
///     BCrypt implementation of <see cref="IHashingService" />. This is the whole of the Auth
///     Provider the event storming draws: no third-party identity service is involved.
/// </summary>
public class BCryptHashingService : IHashingService
{
    public string Hash(Password password)
    {
        return BCryptNet.HashPassword(password.Value);
    }

    public bool Verify(string plainPassword, string passwordHash)
    {
        if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(passwordHash)) return false;

        try
        {
            return BCryptNet.Verify(plainPassword, passwordHash);
        }
        catch
        {
            // A malformed stored hash must read as a failed check, never as an exception that
            // would leak the state of the account.
            return false;
        }
    }
}
