using Cortex.Mediator;
using Healthify.Platform.Iam.Application.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.Events;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.Iam.Application.Internal.CommandServices;

/// <summary>
///     IAM-2. Password recovery without a session: a six-digit code goes to the account email and sets a new
///     password once.
/// </summary>
public class PasswordResetCommandService(
    IUserRepository userRepository,
    IUserSessionRepository sessionRepository,
    IPasswordResetCodeRepository resetCodeRepository,
    IUnitOfWork unitOfWork,
    IHashingService hashingService,
    IPasswordResetCodeService resetCodeService,
    IEmailService emailService,
    ILogger<PasswordResetCommandService> logger,
    IMediator mediator) : IPasswordResetCommandService
{
    private static readonly Result<bool, IamError> Accepted = new Result<bool, IamError>.Success(true);

    private static readonly Result<bool, IamError> CodeInvalid =
        new Result<bool, IamError>.Failure(IamError.PasswordResetCodeInvalid);

    /// <summary>IAM-2 - Request Password Reset.</summary>
    public async Task<Result<bool, IamError>> Handle(RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. A malformed email cannot have an account. Same answer as an unknown one.
        Email email;
        try
        {
            email = new Email(command.Email ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return Accepted;
        }

        try
        {
            // 2. Business rule: Recovery Does Not Reveal Accounts (IAM-2).
            var user = await userRepository.FindByEmailAsync(email, cancellationToken);
            if (user is null) return Accepted;

            // 3. A code issued a moment ago is not replaced: asking again does not flood the inbox.
            var now = resetCodeService.Now();
            var latest = await resetCodeRepository.FindLatestByUserIdAsync(user.Id.Value, cancellationToken);
            if (latest is not null && latest.IssuedAt + resetCodeService.ResendCooldown() > now) return Accepted;

            // 4. Business rule: One Reset Code Per Account (IAM-2). The previous codes go in the same save.
            await resetCodeRepository.RemoveByUserIdAsync(user.Id.Value, cancellationToken);
            var issued = resetCodeService.Issue(user.Id.Value);
            await resetCodeRepository.AddAsync(
                new PasswordResetCode(user.Id.Value, issued.Hash, issued.IssuedAt, issued.ExpiresAt),
                cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 5. Send only after the commit: an email never carries a code the server does not know.
            await emailService.SendPasswordResetCodeAsync(user.Email, issued.Code, issued.ExpiresAt - issued.IssuedAt,
                user.PreferredLanguage, cancellationToken);

            return Accepted;
        }
        catch (Exception ex)
        {
            // 6. Safety net. Still accepted, so a failure does not tell an existing account apart. The code is never
            //    logged.
            logger.LogError(ex, "Error requesting a password reset for email {Email}", email.Value);
            return Accepted;
        }
    }

    /// <summary>IAM-2 - Reset Password.</summary>
    public async Task<Result<bool, IamError>> Handle(ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Business rule: Strong Password Required (Subflow 1.1). Checked first, so a weak password does not spend
        //    an attempt of the code.
        Password password;
        try
        {
            password = new Password(command.NewPassword ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return new Result<bool, IamError>.Failure(IamError.WeakPassword);
        }

        Email email;
        try
        {
            email = new Email(command.Email ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return CodeInvalid;
        }

        if (string.IsNullOrWhiteSpace(command.Code)) return CodeInvalid;

        try
        {
            // 2. Every refusal below is the same error: unknown account, no code, expired, used or out of attempts.
            var user = await userRepository.FindByEmailAsync(email, cancellationToken);
            if (user is null) return CodeInvalid;

            var now = resetCodeService.Now();
            var resetCode = await resetCodeRepository.FindLatestByUserIdAsync(user.Id.Value, cancellationToken);
            if (resetCode is null || !resetCode.IsUsableAt(now)) return CodeInvalid;

            // 3. Business rule: Reset Code Attempts Are Limited (IAM-2).
            if (!resetCodeService.Matches(user.Id.Value, command.Code.Trim(), resetCode.CodeHash))
            {
                resetCode.RegisterFailedAttempt();
                resetCodeRepository.Update(resetCode);
                await unitOfWork.CompleteAsync(cancellationToken);
                return CodeInvalid;
            }

            // 4. Set the password and consume the code.
            user.ResetPassword(hashingService.Hash(password));
            userRepository.Update(user);
            resetCode.MarkAsUsed(now);
            resetCodeRepository.Update(resetCode);

            // 5. Business rule: Password Reset Ends Every Session (IAM-2). Whoever knew the old password loses the
            //    sessions and refresh tokens opened with it.
            var terminated = await TerminateOpenSessionsAsync(user.Id.Value, cancellationToken);

            // 6. Publish, always after the commit.
            foreach (var session in terminated)
                await mediator.PublishAsync(
                    new SessionTerminated(session.Id.Value, session.UserId, session.TerminatedAt!.Value),
                    cancellationToken);

            return new Result<bool, IamError>.Success(true);
        }
        catch (Exception ex)
        {
            // 7. Safety net. Neither the code nor the password is ever logged.
            logger.LogError(ex, "Error resetting the password for email {Email}", email.Value);
            return new Result<bool, IamError>.Failure(IamError.UnexpectedError);
        }
    }

    /// <summary>
    ///     Terminates the open sessions and commits, together with the changes already tracked. A refresh that
    ///     rotates one of them in between is a concurrency conflict (IAM-4): the sessions are read again and the
    ///     save is retried once.
    /// </summary>
    private async Task<IReadOnlyList<UserSession>> TerminateOpenSessionsAsync(int userId,
        CancellationToken cancellationToken)
    {
        var sessions = (await sessionRepository.ListByUserIdAsync(userId, cancellationToken)).ToList();
        var terminated = new List<UserSession>();
        foreach (var session in sessions.Where(s => s.IsActive))
        {
            session.Terminate();
            sessionRepository.Update(session);
            terminated.Add(session);
        }

        try
        {
            await unitOfWork.CompleteAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            foreach (var session in terminated)
            {
                await sessionRepository.ReloadAsync(session, cancellationToken);
                if (!session.IsActive) continue;
                session.Terminate();
                sessionRepository.Update(session);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }

        return terminated;
    }
}
