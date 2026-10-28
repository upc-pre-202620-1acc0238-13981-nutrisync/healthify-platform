using Healthify.Platform.Iam.Domain.Model.ValueObjects;

namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>IAM-2. Sends the transactional emails of Iam. The adapter lives in the infrastructure layer.</summary>
public interface IEmailService
{
    /// <summary>Emails the reset code, in the language of the account.</summary>
    /// <param name="to">The account email.</param>
    /// <param name="code">The six digits. The implementation must not log it when the email is sent.</param>
    /// <param name="validFor">How long the code is accepted, to tell the reader.</param>
    /// <param name="language">The interface language of the account.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task SendPasswordResetCodeAsync(Email to, string code, TimeSpan validFor, PreferredLanguage language,
        CancellationToken cancellationToken = default);
}
