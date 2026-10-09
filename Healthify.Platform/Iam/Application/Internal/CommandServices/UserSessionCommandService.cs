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

public class UserSessionCommandService(
    IUserRepository userRepository,
    IUserSessionRepository sessionRepository,
    IUnitOfWork unitOfWork,
    IHashingService hashingService,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    ILogger<UserSessionCommandService> logger,
    IMediator mediator,
    ISignInLockoutPolicy lockoutPolicy) : IUserSessionCommandService
{
    /// <summary>Subflow 1.2 - Sign In and Role Claim.</summary>
    public async Task<Result<SignInOutcome, IamError>> Handle(SignInCommand command,
        CancellationToken cancellationToken = default)
    {
        Email email;
        try
        {
            email = new Email(command.Email);
        }
        catch (ArgumentException)
        {
            // A malformed address cannot match an account. Reported as bad credentials rather than
            // as a validation error, so the endpoint does not disclose which addresses exist.
            return new Result<SignInOutcome, IamError>.Failure(IamError.InvalidCredentials);
        }

        try
        {
            var user = await userRepository.FindByEmailAsync(email, cancellationToken);
            if (user is null)
                return new Result<SignInOutcome, IamError>.Failure(IamError.InvalidCredentials);

            // Business rule: Lockout After Five Failed Attempts (Subflow 1.2) and Lockout Is Temporary (IAM-5)
            var now = refreshTokenService.Now();
            var lockoutDuration = lockoutPolicy.LockoutDuration();
            if (user.IsLockedOutAt(now, lockoutDuration))
                return new Result<SignInOutcome, IamError>.Failure(IamError.AccountLocked);

            // Business rule: Valid Credentials Required (Subflow 1.2)
            if (!hashingService.Verify(command.Password, user.PasswordHash))
            {
                user.RegisterFailedSignInAttempt(now, lockoutDuration);
                userRepository.Update(user);
                await unitOfWork.CompleteAsync(cancellationToken);

                return new Result<SignInOutcome, IamError>.Failure(
                    user.IsLockedOutAt(now, lockoutDuration) ? IamError.AccountLocked : IamError.InvalidCredentials);
            }

            user.RegisterSuccessfulSignIn();
            userRepository.Update(user);

            // Business rule: Role Immutable Per Session (Subflow 1.2). The aggregate copies its role
            // into the session; the session exposes no mutator for it.
            var session = user.StartSession();

            // IAM-4. The session is born with a refresh token; only its hash is stored. A client that
            // ignores it keeps working exactly as before, with the access token alone.
            var refreshToken = refreshTokenService.Issue();
            session.RotateRefreshToken(refreshToken.Hash, refreshToken.ExpiresAt, refreshTokenService.Now());

            await sessionRepository.AddAsync(session, cancellationToken);

            await unitOfWork.CompleteAsync(cancellationToken);

            var token = tokenService.GenerateToken(user, session);
            var expiresAt = tokenService.ExpiresAt(refreshTokenService.Now());

            // Published after the commit. SessionStarted and RoleClaimIssued both stay inside Iam;
            // the second one is consumed by this context own navigation-shell policy.
            await mediator.PublishAsync(
                new SessionStarted(session.Id.Value, user.Id.Value, session.StartedAt), cancellationToken);
            await mediator.PublishAsync(
                new RoleClaimIssued(session.Id.Value, user.Id.Value, session.RoleClaim.Value), cancellationToken);

            return new Result<SignInOutcome, IamError>.Success(
                new SignInOutcome(user, session, token, refreshToken.Token, expiresAt));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error signing in email {Email}", command.Email);
            return new Result<SignInOutcome, IamError>.Failure(IamError.UnexpectedError);
        }
    }

    /// <summary>IAM-4 - Refresh Session.</summary>
    /// <remarks>
    ///     Same session, same role claim: a refresh issues a new access token for the session that already
    ///     exists, so Role Immutable Per Session (Subflow 1.2) still holds. Every use rotates the refresh token.
    ///     Business rule: Refresh Retry Within Grace Is Not Reuse (IAM-4). The token just rotated away from,
    ///     presented again within <c>TokenSettings:RefreshReuseGraceSeconds</c> while its successor is still
    ///     unused, gets the very same pair back: the successor is derived from it and the access token is issued
    ///     at the rotation instant with an identifier derived from the successor. Nothing is written.
    ///     Business rule: Refresh Token Reuse Ends The Session (IAM-4). The rotated token outside that window, or
    ///     once its successor has been used (it is then the token rotated two steps back), terminates the
    ///     session, which also invalidates whatever the copy was rotated into. Every failure is the same 401.
    ///     Concurrency: the session row is updated only if its refresh token hash is still the one read. Of two
    ///     simultaneous refreshes one rotates; the other loses the update, reads the row again and replays.
    ///     DECISIÓN IAM-4: a token rotated more than two steps back is simply unknown: refused, session kept.
    /// </remarks>
    public async Task<Result<SignInOutcome, IamError>> Handle(RefreshSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Input. The token is never logged.
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return new Result<SignInOutcome, IamError>.Failure(IamError.RefreshTokenInvalid);

        var tokenHash = refreshTokenService.Hash(command.RefreshToken);
        var successor = refreshTokenService.Successor(command.RefreshToken);

        try
        {
            // 2. Load the session the token is current for, or was rotated away from.
            var session = await sessionRepository.FindByRefreshTokenHashAsync(tokenHash, cancellationToken)
                          ?? await sessionRepository.FindByPreviousRefreshTokenHashAsync(tokenHash, cancellationToken);

            // The token's successor was itself rotated already: the token is two steps back, which is reuse.
            if (session is null)
                return await RejectAsReuseAsync(
                    await sessionRepository.FindByPreviousRefreshTokenHashAsync(successor.Hash, cancellationToken),
                    cancellationToken);

            var user = await userRepository.FindByIdAsync(session.UserId, cancellationToken);
            if (user is null || user.IsLockedOutAt(refreshTokenService.Now(), lockoutPolicy.LockoutDuration()))
                return new Result<SignInOutcome, IamError>.Failure(IamError.RefreshTokenInvalid);

            // 3. State guards, evaluated again after a lost concurrency conflict.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var now = refreshTokenService.Now();

                if (session.AcceptsRefreshToken(tokenHash, now))
                {
                    // 4. Rotate: the presented token becomes the one whose reuse is detected.
                    session.RotateRefreshToken(successor.Hash, successor.ExpiresAt, now);

                    // 5. Persist, conditional on the hash that was read.
                    sessionRepository.Update(session);
                    try
                    {
                        await unitOfWork.CompleteAsync(cancellationToken);
                    }
                    catch (ConcurrencyConflictException)
                    {
                        // Another request rotated this token first. Read what it wrote; it is a replay now.
                        await sessionRepository.ReloadAsync(session, cancellationToken);
                        continue;
                    }

                    return Issued(user, session, successor.Token);
                }

                // Business rule: Refresh Retry Within Grace Is Not Reuse (IAM-4).
                if (session.CanReplayRotation(tokenHash, successor.Hash, now, refreshTokenService.ReuseGrace()))
                    return Issued(user, session, successor.Token);

                // Business rule: Refresh Token Reuse Ends The Session (IAM-4).
                if (session.IsRotatedRefreshToken(tokenHash))
                    return await RejectAsReuseAsync(session, cancellationToken);

                break; // expired, or the session ended
            }

            return new Result<SignInOutcome, IamError>.Failure(IamError.RefreshTokenInvalid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing a session");
            return new Result<SignInOutcome, IamError>.Failure(IamError.UnexpectedError);
        }
    }

    /// <summary>
    ///     IAM-4. The pair that goes with the current refresh token: the same strings every time it is built, so
    ///     the rotation and its replays answer identically.
    /// </summary>
    private Result<SignInOutcome, IamError> Issued(User user, UserSession session, string refreshToken)
    {
        var issuedAt = session.RefreshTokenRotatedAt!.Value;
        var tokenId = refreshTokenService.Hash("jti:" + session.RefreshTokenHash)[..32];
        var token = tokenService.GenerateToken(user, session, issuedAt, tokenId);

        return new Result<SignInOutcome, IamError>.Success(new SignInOutcome(user, session, token, refreshToken,
            tokenService.ExpiresAt(issuedAt)));
    }

    /// <summary>IAM-4. Terminates a session whose rotated token came back; an unknown token is only refused.</summary>
    private async Task<Result<SignInOutcome, IamError>> RejectAsReuseAsync(UserSession? rotated,
        CancellationToken cancellationToken)
    {
        if (rotated is not { IsActive: true })
            return new Result<SignInOutcome, IamError>.Failure(IamError.RefreshTokenInvalid);

        // Business rule: Refresh Token Reuse Ends The Session (IAM-4). Retried once if a refresh of the same
        // session committed in between.
        for (var attempt = 0; ; attempt++)
        {
            rotated.Terminate();
            sessionRepository.Update(rotated);
            try
            {
                await unitOfWork.CompleteAsync(cancellationToken);
                break;
            }
            catch (ConcurrencyConflictException) when (attempt == 0)
            {
                await sessionRepository.ReloadAsync(rotated, cancellationToken);
                if (!rotated.IsActive)
                    return new Result<SignInOutcome, IamError>.Failure(IamError.RefreshTokenInvalid);
            }
        }

        logger.LogWarning("Refresh token reuse detected: session {SessionId} of user {UserId} was terminated",
            rotated.Id.Value, rotated.UserId);

        await mediator.PublishAsync(new RefreshTokenReuseDetected(rotated.Id.Value, rotated.UserId),
            cancellationToken);
        await mediator.PublishAsync(
            new SessionTerminated(rotated.Id.Value, rotated.UserId, rotated.TerminatedAt!.Value), cancellationToken);

        return new Result<SignInOutcome, IamError>.Failure(IamError.RefreshTokenInvalid);
    }

    /// <summary>
    ///     Subflow 1.2 - Select Navigation Shell. Reached only from the policy that reacts to
    ///     Role Claim Issued. There is no endpoint for it.
    /// </summary>
    public async Task<Result<UserSession, IamError>> Handle(SelectNavigationShellCommand command,
        CancellationToken cancellationToken = default)
    {
        NavigationShell shell;
        try
        {
            shell = new NavigationShell(command.NavigationShell);
        }
        catch (ArgumentException)
        {
            return new Result<UserSession, IamError>.Failure(IamError.RoleChangeRequiresReAuthentication);
        }

        try
        {
            var session = await sessionRepository.FindByIdAsync(command.SessionId, cancellationToken);
            if (session is null)
                return new Result<UserSession, IamError>.Failure(IamError.SessionNotFound);

            // Asking the aggregate for its state, so that a terminated session reports the right
            // error. The aggregate refuses the operation as well; this only tells the two
            // InvalidOperationException cases apart before they are thrown.
            if (!session.IsActive)
                return new Result<UserSession, IamError>.Failure(IamError.SessionAlreadyTerminated);

            // Rules One Shell Per Session and Role Change Requires Re Authentication live in the
            // aggregate; the exception type tells the two apart.
            session.SelectNavigationShell(shell);

            sessionRepository.Update(session);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new NavigationShellSelected(session.Id.Value, session.UserId, shell.Value), cancellationToken);

            return new Result<UserSession, IamError>.Success(session);
        }
        catch (ArgumentException)
        {
            // Business rule: Role Change Requires Re Authentication (Subflow 1.2)
            return new Result<UserSession, IamError>.Failure(IamError.RoleChangeRequiresReAuthentication);
        }
        catch (InvalidOperationException)
        {
            // Business rule: One Shell Per Session (Subflow 1.2)
            return new Result<UserSession, IamError>.Failure(IamError.ShellAlreadySelectedForSession);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error selecting navigation shell for session {SessionId}", command.SessionId);
            return new Result<UserSession, IamError>.Failure(IamError.UnexpectedError);
        }
    }

    /// <summary>Subflow 1.3 - Sign Out.</summary>
    public async Task<Result<UserSession, IamError>> Handle(SignOutCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await sessionRepository.FindByIdAsync(command.SessionId, cancellationToken);

            // A session that belongs to someone else is reported as absent rather than as forbidden,
            // so the endpoint does not disclose which session identifiers exist.
            if (session is null || session.UserId != command.UserId)
                return new Result<UserSession, IamError>.Failure(IamError.SessionNotFound);

            // Business rule: Role Claim Discarded On Sign Out (Subflow 1.3)
            session.Terminate();

            sessionRepository.Update(session);
            try
            {
                await unitOfWork.CompleteAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException)
            {
                // IAM-4: a refresh of this session rotated its token in between. Read it again and sign out
                // that; a second conflict, or a session that ended meanwhile, reports as usual.
                await sessionRepository.ReloadAsync(session, cancellationToken);
                session.Terminate();
                sessionRepository.Update(session);
                await unitOfWork.CompleteAsync(cancellationToken);
            }

            await mediator.PublishAsync(
                new SessionTerminated(session.Id.Value, session.UserId, session.TerminatedAt!.Value),
                cancellationToken);

            return new Result<UserSession, IamError>.Success(session);
        }
        catch (InvalidOperationException)
        {
            return new Result<UserSession, IamError>.Failure(IamError.SessionAlreadyTerminated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error signing out session {SessionId}", command.SessionId);
            return new Result<UserSession, IamError>.Failure(IamError.UnexpectedError);
        }
    }
}
