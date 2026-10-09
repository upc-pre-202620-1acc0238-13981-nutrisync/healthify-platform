using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Application.Internal.CommandServices;

public class InvitationCommandService(
    IInvitationRepository invitationRepository,
    ICareLinkRepository careLinkRepository,
    IUnitOfWork unitOfWork,
    IIamContextFacade iamContextFacade,
    ILogger<InvitationCommandService> logger,
    IMediator mediator) : IInvitationCommandService
{
    /// <summary>Subflow 2.1 - Issue Invitation.</summary>
    public async Task<Result<Invitation, CareRelationshipError>> Handle(IssueInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Business rule: Practitioner Only (Subflow 2.1). Checked against the Iam ACL facade as
            // well as by the role attribute on the endpoint. The facade degrades to false, so an
            // identity lookup that fails refuses the invitation rather than granting it.
            if (!await iamContextFacade.IsPractitioner(command.IssuedBy, cancellationToken))
                return new Result<Invitation, CareRelationshipError>.Failure(
                    CareRelationshipError.PractitionerOnly);

            var invitation = new Invitation(command);

            await invitationRepository.AddAsync(invitation, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new InvitationIssued(invitation.Id.Value, invitation.IssuedBy, invitation.ExpiresAt),
                cancellationToken);

            return new Result<Invitation, CareRelationshipError>.Success(invitation);
        }
        catch (ArgumentException)
        {
            // Business rule: Expiration Date Required (Subflow 2.1)
            return new Result<Invitation, CareRelationshipError>.Failure(
                CareRelationshipError.ExpirationDateRequired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error issuing an invitation for practitioner {PractitionerId}",
                command.IssuedBy);
            return new Result<Invitation, CareRelationshipError>.Failure(
                CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>
    ///     Subflow 2.1 - Expire Invitation. Reached only from the time-driven policy
    ///     "When Expiration Date Reached". Re-running it over the same invitation is a no-op.
    /// </summary>
    public async Task<Result<Invitation, CareRelationshipError>> Handle(ExpireInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var invitation = await invitationRepository.FindByIdAsync(command.InvitationId, cancellationToken);
            if (invitation is null)
                return new Result<Invitation, CareRelationshipError>.Failure(
                    CareRelationshipError.InvitationNotFound);

            // Already expired: nothing to do, and no second event. Keeps the policy idempotent.
            if (invitation.IsExpired)
                return new Result<Invitation, CareRelationshipError>.Success(invitation);

            // Business rule: Redeemed Invitation Cannot Expire (Subflow 2.1)
            invitation.Expire(DateTimeOffset.UtcNow);

            invitationRepository.Update(invitation);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new InvitationExpired(invitation.Id.Value, invitation.IssuedBy, invitation.ExpiredAt!.Value),
                cancellationToken);

            return new Result<Invitation, CareRelationshipError>.Success(invitation);
        }
        catch (InvalidOperationException)
        {
            return new Result<Invitation, CareRelationshipError>.Failure(
                CareRelationshipError.RedeemedInvitationCannotExpire);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error expiring invitation {InvitationId}", command.InvitationId);
            return new Result<Invitation, CareRelationshipError>.Failure(
                CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>Subflow 2.2 - Redeem Invitation, and CR-1 - switching practitioners.</summary>
    /// <remarks>
    ///     The care link is not created here. Redeeming publishes Invitation Redeemed, and the policy
    ///     that reacts to it issues Establish Care Link, which is the only path into a care link.
    ///     Because PublishAsync awaits its handlers, the link exists by the time this method reads it
    ///     back to answer the caller.
    /// </remarks>
    public async Task<Result<InvitationRedemptionOutcome, CareRelationshipError>> Handle(
        RedeemInvitationCommand command, CancellationToken cancellationToken = default)
    {
        InvitationToken token;
        try
        {
            token = new InvitationToken(command.Token);
        }
        catch (ArgumentException)
        {
            return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                CareRelationshipError.InvitationNotValid);
        }

        try
        {
            var invitation = await invitationRepository.FindByTokenAsync(token, cancellationToken);
            if (invitation is null)
                return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                    CareRelationshipError.InvitationNotFound);

            // Business rule: Invitation Must Be Unused (Subflow 2.2)
            if (invitation.IsRedeemed)
                return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                    CareRelationshipError.InvitationAlreadyRedeemed);

            // Business rule: Invitation Must Be Valid (Subflow 2.2)
            if (!invitation.IsValidAt(DateTimeOffset.UtcNow))
                return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                    CareRelationshipError.InvitationExpired);

            // Business rule: Patient Cannot Self Link (Subflow 2.2), checked before the token is
            // burned so that a practitioner scanning their own QR code does not consume it.
            if (invitation.IssuedBy == command.PatientId)
                return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                    CareRelationshipError.PatientCannotSelfLink);

            // Business rule: One Active Link Per Patient (Subflow 2.2). Checked here as well as in
            // Establish Care Link, so a redemption that could not produce a link never burns the
            // single-use token. The unclosed lookup is the right one: a link awaiting consent
            // already occupies the slot.
            var previousLinkHadAiProcessing = false;
            var previousLink = await careLinkRepository.FindUnclosedByPatientIdAsync(
                command.PatientId, cancellationToken);

            if (previousLink is not null)
            {
                // Without the explicit confirmation of the switch, the answer is the conflict it
                // always was (CR-1).
                if (!command.ReplaceActiveLink)
                    return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                        CareRelationshipError.PatientAlreadyHasActiveLink);

                // Business rule: Switching Requires Another Practitioner (CR-1). Replacing a link
                // with an identical one would only revoke it; the token is left untouched.
                if (previousLink.PractitionerId == invitation.IssuedBy)
                    return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                        CareRelationshipError.AlreadyLinkedToThisPractitioner);

                // Business rule: Revoked Link Kept With Revocation Date (Subflow 2.5). The previous
                // practitioner loses access from this commit on, as in a consent withdrawal; the
                // diary and the self weigh-ins do not depend on the link and are kept.
                // Business rule: AI Processing Ends With The Link (CR-2). The new link starts without it.
                previousLinkHadAiProcessing = previousLink.ConsentAiProcessingGranted;
                previousLink.Revoke(RevocationReason.SwitchedPractitioner);
                careLinkRepository.Update(previousLink);
            }

            invitation.Redeem(DateTimeOffset.UtcNow);

            invitationRepository.Update(invitation);

            // One commit for both aggregates: the previous link is never revoked without the
            // invitation being redeemed, nor the other way round.
            await unitOfWork.CompleteAsync(cancellationToken);

            // The revocation is published first, as the specification orders it. Monitoring does not
            // depend on that order: on Care Link Established it closes a window still open for a
            // previous link before opening the new one, and Care Link Revoked only closes the window
            // of the revoked link.
            if (previousLink is not null)
                await mediator.PublishAsync(
                    new CareLinkRevoked(previousLink.Id.Value, previousLink.PatientId,
                        previousLink.PractitionerId, previousLink.RevokedAt!.Value,
                        previousLink.RevocationReason!.Value), cancellationToken);

            // CR-2: published before the new link exists, so the preferences go off and the AI content of the
            // patient is purged; granting AI consent on the new link turns them on again.
            if (previousLink is not null && previousLinkHadAiProcessing)
                await mediator.PublishAsync(
                    new AiProcessingConsentChanged(previousLink.PatientId, false, previousLink.RevokedAt!.Value),
                    cancellationToken);

            await mediator.PublishAsync(
                new InvitationRedeemed(invitation.Id.Value, command.PatientId, invitation.IssuedBy),
                cancellationToken);

            // Read back what the policy created, in this scope, to answer the caller with it. The
            // unclosed lookup is the right one here: the link exists but is not active yet, because
            // it starts inactive until the patient grants consent.
            var careLink = await careLinkRepository.FindUnclosedByPatientIdAsync(
                command.PatientId, cancellationToken);

            if (careLink is null)
                logger.LogError(
                    "Invitation {InvitationId} was redeemed but no care link was established for patient {PatientId}",
                    invitation.Id.Value, command.PatientId);

            return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Success(
                new InvitationRedemptionOutcome(invitation, careLink));
        }
        catch (InvalidOperationException)
        {
            return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                CareRelationshipError.InvitationAlreadyRedeemed);
        }
        catch (ArgumentException)
        {
            return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                CareRelationshipError.InvitationExpired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error redeeming an invitation for patient {PatientId}", command.PatientId);
            return new Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure(
                CareRelationshipError.UnexpectedError);
        }
    }
}
