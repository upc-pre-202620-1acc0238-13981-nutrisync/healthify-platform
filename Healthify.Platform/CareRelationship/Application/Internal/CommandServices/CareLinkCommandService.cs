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

public class CareLinkCommandService(
    ICareLinkRepository careLinkRepository,
    IUnitOfWork unitOfWork,
    IIamContextFacade iamContextFacade,
    ILogger<CareLinkCommandService> logger,
    IMediator mediator) : ICareLinkCommandService
{
    /// <summary>
    ///     Subflow 2.2 - Establish Care Link. Reached only from the policy "When Invitation
    ///     Redeemed": a patient can never create their own link.
    /// </summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(EstablishCareLinkCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Business rule: One Active Link Per Patient (Subflow 2.2)
            // TODO: hotspot. This read-then-write leaves a race window: two invitations redeemed for
            // the same patient at the same instant could both pass the check. MySQL has no partial
            // index, so a unique index on patient_id cannot be limited to unclosed rows, and adding
            // one unconditionally would forbid the legitimate second link after a discharge.
            // Assumed interpretation, the most conservative available without changing the model:
            // check plus the guard in Redeem Invitation. A real fix needs either a serialised
            // transaction or a generated column carrying the open/closed state.
            // Source: event storming, Subflow 2.2 hotspot.
            if (await careLinkRepository.ExistsUnclosedByPatientIdAsync(command.PatientId, cancellationToken))
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.PatientAlreadyHasActiveLink);

            // Switching practitioners (CR-1) never reaches this point with the previous link still
            // open: Redeem Invitation revokes it, with reason SwitchedPractitioner, in the same
            // commit that burns the token, and only when the patient explicitly confirmed it.
            // Without that confirmation the guard above keeps answering with a conflict.
            // Source: event storming, Subflow 2.5 hotspot 1, resolved by CR-1.

            var careLink = new CareLink(command);

            await careLinkRepository.AddAsync(careLink, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Integration event 1 of 13: Monitoring and Adherence opens the evaluation window.
            await mediator.PublishAsync(
                new CareLinkEstablished(careLink.Id.Value, careLink.PatientId, careLink.PractitionerId),
                cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (InvalidOperationException)
        {
            // Business rule: Patient Cannot Self Link (Subflow 2.2)
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.PatientCannotSelfLink);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error establishing a care link for patient {PatientId}", command.PatientId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>Subflow 2.3 - Grant Consent.</summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(GrantConsentCommand command,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Consent Scope Recorded (Subflow 2.3), validated before anything is loaded.
        if (string.IsNullOrWhiteSpace(command.Scope))
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.ConsentScopeRequired);

        try
        {
            var careLink = await careLinkRepository.FindByIdAsync(command.CareLinkId, cancellationToken);
            if (careLink is null || careLink.PatientId != command.PatientId)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotFound);

            if (careLink.IsDischarged)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.DischargedLinkCannotBeReactivated);
            if (careLink.IsRevoked)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkAlreadyRevoked);
            if (careLink.Consent is { IsGranted: true })
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.ConsentAlreadyGranted);

            careLink.GrantConsent(command);

            careLinkRepository.Update(careLink);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Stays inside this context: Care Link Status is queried synchronously by the other five
            // contexts, so no external policy reacts to consent being granted.
            await mediator.PublishAsync(
                new ConsentGranted(careLink.Id.Value, careLink.PatientId, careLink.PractitionerId,
                    careLink.Consent!.Scope, careLink.ConsentAiProcessingGranted), cancellationToken);

            // CR-2: the AI switch was turned on in the same consent. This one does cross the boundary.
            if (careLink.ConsentAiProcessingGranted)
                await mediator.PublishAsync(
                    new AiProcessingConsentChanged(careLink.PatientId, true,
                        careLink.ConsentAiProcessingDecidedAt!.Value), cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (ArgumentException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.ConsentScopeRequired);
        }
        catch (InvalidOperationException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.ConsentAlreadyGranted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error granting consent on care link {CareLinkId}", command.CareLinkId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>Subflow 2.5 - Withdraw Consent. No justification is asked for, or accepted.</summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(WithdrawConsentCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var careLink = await careLinkRepository.FindByIdAsync(command.CareLinkId, cancellationToken);
            if (careLink is null || careLink.PatientId != command.PatientId)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotFound);

            // Business rules: Consent Always Revocable, No Justification Required (Subflows 2.3, 2.5)
            // CR-2: withdrawing the whole consent also turns AI processing off.
            var aiProcessingWasGranted = careLink.ConsentAiProcessingGranted;
            careLink.WithdrawConsent();

            careLinkRepository.Update(careLink);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Consumed by this context own policy, which revokes the link.
            await mediator.PublishAsync(
                new ConsentWithdrawn(careLink.Id.Value, careLink.PatientId,
                    careLink.Consent!.WithdrawnAt!.Value), cancellationToken);

            // CR-2: the AI switch went off with it; the AI content of the patient is purged (§12-#14).
            if (aiProcessingWasGranted)
                await mediator.PublishAsync(
                    new AiProcessingConsentChanged(careLink.PatientId, false, careLink.ConsentWithdrawnAt!.Value),
                    cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (InvalidOperationException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.NoActiveConsent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error withdrawing consent on care link {CareLinkId}", command.CareLinkId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>
    ///     CR-2 - Change AI Processing Consent. Turning it on needs an active link; turning it off is always
    ///     possible. Asking for the state it already has is a no-op that publishes nothing.
    /// </summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(ChangeAiProcessingConsentCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var careLink = await careLinkRepository.FindByIdAsync(command.CareLinkId, cancellationToken);
            if (careLink is null || careLink.PatientId != command.PatientId)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotFound);

            // Business rule: AI Processing Needs Live Consent (CR-2)
            if (command.Granted && !careLink.IsActive)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    careLink.IsDischarged ? CareRelationshipError.DischargedLinkCannotBeReactivated
                    : careLink.IsRevoked ? CareRelationshipError.CareLinkAlreadyRevoked
                    : CareRelationshipError.CareLinkNotActive);

            var changed = command.Granted ? careLink.GrantAiProcessing() : careLink.RevokeAiProcessing();
            if (!changed) return new Result<CareLink, CareRelationshipError>.Success(careLink);

            careLinkRepository.Update(careLink);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Crosses the boundary: preferences (IA-1) follow it, and every context purges its AI content when it
            // goes off (§12-#14).
            await mediator.PublishAsync(
                new AiProcessingConsentChanged(careLink.PatientId, careLink.ConsentAiProcessingGranted,
                    careLink.ConsentAiProcessingDecidedAt!.Value), cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (InvalidOperationException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.CareLinkNotActive);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error changing the AI processing consent on care link {CareLinkId}",
                command.CareLinkId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>
    ///     Subflow 2.5 - Revoke Care Link. Reached only from the policy "When Consent Withdrawn".
    ///     There is deliberately no endpoint for it.
    /// </summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(RevokeCareLinkCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var careLink = await careLinkRepository.FindByIdAsync(command.CareLinkId, cancellationToken);
            if (careLink is null)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotFound);

            // Already revoked: no second event, so the policy stays idempotent.
            if (careLink.IsRevoked)
                return new Result<CareLink, CareRelationshipError>.Success(careLink);

            // Business rule: Revoked Link Kept With Revocation Date (Subflow 2.5). This command is
            // only issued by the consent withdrawal policy, so that is the reason it records.
            // Business rule: AI Processing Ends With The Link (CR-2). Normally already off: the withdrawal turned it
            // off and published it.
            var aiProcessingWasGranted = careLink.ConsentAiProcessingGranted;
            careLink.Revoke(RevocationReason.ConsentWithdrawn);

            careLinkRepository.Update(careLink);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Integration event 2 of 13: Monitoring and Adherence closes the evaluation window.
            await mediator.PublishAsync(
                new CareLinkRevoked(careLink.Id.Value, careLink.PatientId, careLink.PractitionerId,
                    careLink.RevokedAt!.Value, careLink.RevocationReason!.Value), cancellationToken);

            if (aiProcessingWasGranted)
                await mediator.PublishAsync(
                    new AiProcessingConsentChanged(careLink.PatientId, false, careLink.RevokedAt.Value),
                    cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (InvalidOperationException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.CareLinkAlreadyRevoked);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking care link {CareLinkId}", command.CareLinkId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>Subflow 2.5 - Discharge Patient.</summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(DischargePatientCommand command,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Clinical Reason Required (Subflow 2.5)
        ClinicalReason reason;
        try
        {
            reason = new ClinicalReason(command.ClinicalReason);
        }
        catch (ArgumentException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.ClinicalReasonRequired);
        }

        try
        {
            // The endpoint carries the role attribute; the facade is the second check, and it
            // degrades to false so a failed identity lookup refuses the discharge.
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.PractitionerOnly);

            var careLink = await careLinkRepository.FindByIdAsync(command.CareLinkId, cancellationToken);
            if (careLink is null)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotFound);
            if (careLink.PractitionerId != command.PractitionerId)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.PractitionerOnly);

            // Business rule: Discharged Link Never Reactivated (Subflow 2.5)
            // Business rule: AI Processing Ends With The Link (CR-2)
            var aiProcessingWasGranted = careLink.ConsentAiProcessingGranted;
            careLink.Discharge(reason);

            careLinkRepository.Update(careLink);
            await unitOfWork.CompleteAsync(cancellationToken);

            // TODO: hotspot. Does the practitioner lose retroactive access to the history when the
            // link ends, or only to new data? Assumed interpretation, the most conservative one:
            // the row is kept with its dates and Care Link History remains readable, while
            // IsCareLinkActive answers false from this moment on, so every other context stops
            // serving new data. Source: event storming, Subflow 2.5 hotspot 2.
            await mediator.PublishAsync(
                new TreatmentDischarged(careLink.Id.Value, careLink.PatientId, careLink.PractitionerId,
                    reason.Value, careLink.DischargedAt!.Value), cancellationToken);

            // CR-2: the AI consent ended with the treatment; preferences go off and AI content is purged (§12-#14).
            if (aiProcessingWasGranted)
                await mediator.PublishAsync(
                    new AiProcessingConsentChanged(careLink.PatientId, false, careLink.DischargedAt.Value),
                    cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (InvalidOperationException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.DischargedLinkCannotBeReactivated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error discharging care link {CareLinkId}", command.CareLinkId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>
    ///     Subflow 2.4 - Mark Targets Pending Acknowledgement. Reached only from the policy that
    ///     reacts to Active Targets Updated, published by Nutritional Care.
    /// </summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(
        MarkTargetsPendingAcknowledgementCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var careLink = await careLinkRepository.FindActiveByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (careLink is null)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotFound);

            // Already pending at this version or newer: no second event, so the policy is idempotent.
            if (careLink.PendingTargetsVersion >= command.PlanVersion)
                return new Result<CareLink, CareRelationshipError>.Success(careLink);

            // Business rule: One Pending Version At A Time (Subflow 2.4)
            careLink.MarkTargetsPending(command.PlanVersion);

            careLinkRepository.Update(careLink);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new TargetsPendingAcknowledgement(careLink.Id.Value, careLink.PatientId, command.PlanVersion),
                cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (InvalidOperationException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.PendingVersionAlreadyExists);
        }
        catch (ArgumentException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.PendingVersionAlreadyExists);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error marking targets pending for patient {PatientId}", command.PatientId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }

    /// <summary>Subflow 2.4 - Acknowledge Active Targets.</summary>
    public async Task<Result<CareLink, CareRelationshipError>> Handle(
        AcknowledgeActiveTargetsCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var careLink = await careLinkRepository.FindByIdAsync(command.CareLinkId, cancellationToken);
            if (careLink is null || careLink.PatientId != command.PatientId)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotFound);

            // Business rule: No Access Without Consent (Subflow 2.3)
            if (!careLink.IsActive)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.CareLinkNotActive);

            if (careLink.PendingTargetsVersion is null)
                return new Result<CareLink, CareRelationshipError>.Failure(
                    CareRelationshipError.NoPendingTargetsVersion);

            // Business rules: Acknowledged Version Not Newer Than Active, and Acknowledgement Does
            // Not Change The Plan (Subflow 2.4). Nothing below reaches Nutritional Care.
            careLink.AcknowledgeActiveTargets(command.PlanVersion);

            careLinkRepository.Update(careLink);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ActiveTargetsAcknowledged(careLink.Id.Value, careLink.PatientId, command.PlanVersion),
                cancellationToken);

            return new Result<CareLink, CareRelationshipError>.Success(careLink);
        }
        catch (ArgumentException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.AcknowledgedVersionNewerThanActive);
        }
        catch (InvalidOperationException)
        {
            return new Result<CareLink, CareRelationshipError>.Failure(
                CareRelationshipError.NoPendingTargetsVersion);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acknowledging targets on care link {CareLinkId}", command.CareLinkId);
            return new Result<CareLink, CareRelationshipError>.Failure(CareRelationshipError.UnexpectedError);
        }
    }
}
