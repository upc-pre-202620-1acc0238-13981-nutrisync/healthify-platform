using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;

/// <summary>Subflow 5.10 - Record Referral.</summary>
public class ReferralCommandService(
    IReferralRepository referralRepository,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IUnitOfWork unitOfWork,
    ILogger<ReferralCommandService> logger,
    IMediator mediator) : IReferralCommandService
{
    public async Task<Result<Referral, MonitoringError>> Handle(RecordReferralCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Business rule: Active Care Link Required (Monitoring and Adherence, Subflow 5.10).
            // The single question this platform asks about access, answered where it is owned.
            if (!await careRelationshipContextFacade.IsCareLinkActive(command.PatientId,
                    command.PractitionerId, cancellationToken))
                return Failure(MonitoringError.ActiveCareLinkRequired);

            // Business rule: Specialty And Reason Required (Monitoring and Adherence, Subflow 5.10).
            // Both are value objects that refuse to exist empty, so the aggregate cannot be built
            // without them.
            var referral = new Referral(command);

            await referralRepository.AddAsync(referral, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ReferralRecorded(referral.Id.Value, referral.PatientId, referral.Specialty.Value),
                cancellationToken);

            return new Result<Referral, MonitoringError>.Success(referral);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.SpecialtyAndReasonRequired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record a referral for patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>RM-4 - Close Referral (DECISIÓN §12-#8: Open/Closed with manual closure).</summary>
    public async Task<Result<Referral, MonitoringError>> Handle(CloseReferralCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 3. Load. A referral another practitioner recorded does not exist for this one.
            var referral = await referralRepository.FindByIdAsync(command.ReferralId, cancellationToken);
            if (referral is null || referral.IssuedBy != command.PractitionerId)
                return Failure(MonitoringError.ReferralNotFound);

            // 4. State guard. Business rule: A Referral Is Closed Once (RM-4).
            if (!referral.IsOpen) return Failure(MonitoringError.ReferralAlreadyClosed);

            // 5-6. Mutate and persist.
            referral.Close(DateTimeOffset.UtcNow);
            referralRepository.Update(referral);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. After the commit.
            await mediator.PublishAsync(new ReferralClosed(referral.Id.Value, referral.PatientId), cancellationToken);
            return new Result<Referral, MonitoringError>.Success(referral);
        }
        catch (InvalidOperationException)
        {
            return Failure(MonitoringError.ReferralAlreadyClosed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not close referral {ReferralId}", command.ReferralId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    private static Result<Referral, MonitoringError> Failure(MonitoringError error)
    {
        return new Result<Referral, MonitoringError>.Failure(error);
    }
}

/// <summary>
///     Subflow 5.10 - Schedule Follow Up and Flag Missed Follow Up.
/// </summary>
/// <remarks>
///     Business rule: Missed Visit Does Not Close The Care Link (Subflow 5.10). This service does not
///     inject anything that could revoke a link or discharge a patient, and the event it publishes
///     for a missed visit has no subscriber anywhere. Somebody who could not make it on Tuesday is
///     still somebody's patient on Wednesday.
/// </remarks>
public class ScheduledFollowUpCommandService(
    IScheduledFollowUpRepository scheduledFollowUpRepository,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IFollowUpCalendar followUpCalendar,
    IUnitOfWork unitOfWork,
    ILogger<ScheduledFollowUpCommandService> logger,
    IMediator mediator) : IScheduledFollowUpCommandService
{
    /// <summary>Subflow 5.10 - Schedule Follow Up.</summary>
    public async Task<Result<ScheduledFollowUp, MonitoringError>> Handle(ScheduleFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Value objects, each one to its own error (MA-2, MA-5). The aggregate validates the moment again.
        // Business rule: A Visit Is Scheduled For The Future (MA-5).
        if (command.ScheduledFor <= DateTimeOffset.UtcNow) return Failure(MonitoringError.ScheduledForMustBeInFuture);

        try
        {
            _ = PreparationInstruction.ListOf(command.Preparation);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.UnknownPreparationInstruction);
        }

        try
        {
            _ = new ConsultationModality(command.Modality);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.UnknownConsultationModality);
        }

        try
        {
            if (!await careRelationshipContextFacade.IsCareLinkActive(command.PatientId,
                    command.PractitionerId, cancellationToken))
                return Failure(MonitoringError.ActiveCareLinkRequired);

            // Business rule: One Active Scheduled Visit Per Patient (Monitoring and Adherence,
            // Subflow 5.10).
            // TODO: race condition - MySQL has no partial index, so "at most one row in state
            // Scheduled per patient" cannot be expressed as a unique index. Two requests arriving at
            // the same instant could both pass this check.
            if (await scheduledFollowUpRepository.FindScheduledByPatientIdAsync(command.PatientId,
                    cancellationToken) is not null)
                return Failure(MonitoringError.PatientAlreadyHasActiveScheduledFollowUp);

            var followUp = new ScheduledFollowUp(command);

            await scheduledFollowUpRepository.AddAsync(followUp, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new FollowUpScheduled(followUp.Id.Value, followUp.PatientId, followUp.ScheduledFor),
                cancellationToken);

            return new Result<ScheduledFollowUp, MonitoringError>.Success(followUp);
        }
        catch (ArgumentException)
        {
            // MA-5: the moment passed between the check above and the aggregate. It used to report 404.
            return Failure(MonitoringError.ScheduledForMustBeInFuture);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not schedule a follow up for patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.10 - Flag Missed Follow Up.</summary>
    public async Task<Result<ScheduledFollowUp, MonitoringError>> Handle(
        FlagMissedFollowUpCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var followUp = await scheduledFollowUpRepository.FindByIdAsync(command.FollowUpId,
                cancellationToken);
            if (followUp is null) return Failure(MonitoringError.ScheduledFollowUpNotFound);

            // Business rule: Missed Visit Does Not Close The Care Link (Monitoring and Adherence,
            // Subflow 5.10). One state moves on one row, and nothing else in the platform is touched.
            if (!followUp.MarkMissed(DateTimeOffset.UtcNow))
                return new Result<ScheduledFollowUp, MonitoringError>.Success(followUp);

            scheduledFollowUpRepository.Update(followUp);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(new FollowUpMissed(followUp.Id.Value, followUp.PatientId),
                cancellationToken);

            return new Result<ScheduledFollowUp, MonitoringError>.Success(followUp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not flag follow up {FollowUpId} as missed", command.FollowUpId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>MA-5 - Cancel Follow Up. Only the practitioner whose agenda the visit is on.</summary>
    /// <remarks>
    ///     No care link is asked: the visit is on this practitioner's agenda, and a visit has to stay cancellable
    ///     after the link ended.
    /// </remarks>
    public async Task<Result<ScheduledFollowUp, MonitoringError>> Handle(CancelFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Value objects: the reason, short, or the default one.
        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? ScheduledFollowUp.CancelledByPractitionerReason
            : command.Reason.Trim();
        if (reason.Length > ScheduledFollowUp.CancellationReasonMaxLength)
            return Failure(MonitoringError.CancellationReasonTooLong);

        try
        {
            // 3. Load. The visit of another practitioner's agenda does not exist for this one.
            var followUp = await scheduledFollowUpRepository.FindByIdAsync(command.FollowUpId, cancellationToken);
            if (followUp is null || followUp.PractitionerId != command.PractitionerId)
                return Failure(MonitoringError.ScheduledFollowUpNotFound);

            // 4. State guard. Business rule: Only A Scheduled Visit Changes (MA-5).
            if (!followUp.IsScheduled) return Failure(MonitoringError.FollowUpNotScheduled);

            // 5-6. Mutate and persist.
            followUp.Cancel(reason);
            scheduledFollowUpRepository.Update(followUp);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. Publish after the commit.
            await mediator.PublishAsync(new FollowUpCancelled(followUp.Id.Value, followUp.PatientId, reason),
                cancellationToken);
            return new Result<ScheduledFollowUp, MonitoringError>.Success(followUp);
        }
        catch (InvalidOperationException)
        {
            return Failure(MonitoringError.FollowUpNotScheduled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not cancel follow up {FollowUpId}", command.FollowUpId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>MA-5 - Reschedule Follow Up. Only the practitioner whose agenda the visit is on.</summary>
    /// <remarks>
    ///     The pre-visit check in (MA-4) is a separate row keyed by the visit, so it is kept; whether it can still
    ///     be edited follows the new moment.
    /// </remarks>
    public async Task<Result<ScheduledFollowUp, MonitoringError>> Handle(RescheduleFollowUpCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Value objects, each one to its own error.
        // Business rule: A Visit Is Scheduled For The Future (MA-5).
        if (command.ScheduledFor <= DateTimeOffset.UtcNow) return Failure(MonitoringError.ScheduledForMustBeInFuture);
        try
        {
            _ = PreparationInstruction.ListOf(command.Preparation);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.UnknownPreparationInstruction);
        }

        try
        {
            // 3. Load. The visit of another practitioner's agenda does not exist for this one.
            var followUp = await scheduledFollowUpRepository.FindByIdAsync(command.FollowUpId, cancellationToken);
            if (followUp is null || followUp.PractitionerId != command.PractitionerId)
                return Failure(MonitoringError.ScheduledFollowUpNotFound);

            // 4. State guard. Business rule: Only A Scheduled Visit Changes (MA-5).
            if (!followUp.IsScheduled) return Failure(MonitoringError.FollowUpNotScheduled);

            // 5-6. Mutate and persist.
            followUp.Reschedule(command.ScheduledFor, command.Preparation);
            scheduledFollowUpRepository.Update(followUp);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. Publish after the commit.
            await mediator.PublishAsync(
                new FollowUpRescheduled(followUp.Id.Value, followUp.PatientId, followUp.ScheduledFor),
                cancellationToken);
            return new Result<ScheduledFollowUp, MonitoringError>.Success(followUp);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.ScheduledForMustBeInFuture);
        }
        catch (InvalidOperationException)
        {
            return Failure(MonitoringError.FollowUpNotScheduled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not reschedule follow up {FollowUpId}", command.FollowUpId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>
    ///     MA-2 - Complete Follow Up. The visit a published consultation started from, or else the visit of
    ///     the same patient with the same practitioner on the same local day, becomes Completed. That is
    ///     what keeps the missed-visit policy from flagging a visit that did happen.
    /// </summary>
    public async Task<Result<ScheduledFollowUp, MonitoringError>> Handle(
        CompleteFollowUpFromConsultationCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            // 3. Load: the visit the consultation names, which has to be this pair's; otherwise the one of
            //    the same local day.
            ScheduledFollowUp? followUp;
            if (command.ScheduledFollowUpId is { } followUpId)
            {
                followUp = await scheduledFollowUpRepository.FindByIdAsync(followUpId, cancellationToken);
                if (followUp is not null && (followUp.PatientId != command.PatientId ||
                                             followUp.PractitionerId != command.PractitionerId))
                    followUp = null;
            }
            else
            {
                var (from, to) = followUpCalendar.LocalDayOf(command.CompletedAt);
                followUp = await scheduledFollowUpRepository.FindOpenForDayAsync(command.PatientId,
                    command.PractitionerId, from, to, cancellationToken);
            }

            if (followUp is null) return Failure(MonitoringError.ScheduledFollowUpNotFound);

            // 4. State guard: a cancelled visit is not reopened by a consultation.
            if (followUp.State.IsCancelled) return Failure(MonitoringError.FollowUpNotScheduled);

            // 5. Mutate. Completing twice is a no-op, so a redelivered event changes nothing.
            if (!followUp.MarkCompleted(command.ConsultationId, command.CompletedAt))
                return new Result<ScheduledFollowUp, MonitoringError>.Success(followUp);

            // 6. Persist. No event: nothing in the platform reacts to a completed visit yet.
            scheduledFollowUpRepository.Update(followUp);
            await unitOfWork.CompleteAsync(cancellationToken);

            return new Result<ScheduledFollowUp, MonitoringError>.Success(followUp);
        }
        catch (InvalidOperationException)
        {
            return Failure(MonitoringError.FollowUpNotScheduled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not complete the follow up of consultation {ConsultationId}",
                command.ConsultationId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>
    ///     CR-4 - The treatment ended (discharge, PR18) or moved to another practitioner (CR-1): its visits still
    ///     ahead leave the agenda (PR17.0-Alta). Past visits are history and stay as they are.
    /// </summary>
    public async Task<Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>> Handle(
        CancelFollowUpsForPatientCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Value objects: the reason.
        if (string.IsNullOrWhiteSpace(command.Reason) ||
            command.Reason.Trim().Length > ScheduledFollowUp.CancellationReasonMaxLength)
            return new Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>.Failure(
                MonitoringError.CancellationReasonTooLong);
        var reason = command.Reason.Trim();

        try
        {
            // 3. Load the visits still ahead: Scheduled, and later than now.
            var now = DateTimeOffset.UtcNow;
            var candidates = command.PractitionerId is { } practitionerId
                ? await scheduledFollowUpRepository.ListOpenByPatientAndPractitionerAsync(command.PatientId,
                    practitionerId, cancellationToken)
                : await scheduledFollowUpRepository.ListByPatientIdAsync(command.PatientId,
                    new FollowUpState(FollowUpState.Scheduled), cancellationToken);
            // 4. State guard. Business rule: Only A Scheduled Visit Changes (MA-5).
            var future = candidates.Where(f => f.IsScheduled && f.ScheduledFor > now).ToList();
            if (future.Count == 0) return new Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>.Success([]);

            // 5-6. Mutate and persist, all in one save.
            foreach (var followUp in future)
            {
                followUp.Cancel(reason);
                scheduledFollowUpRepository.Update(followUp);
            }

            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. Publish after the commit.
            foreach (var followUp in future)
                await mediator.PublishAsync(new FollowUpCancelled(followUp.Id.Value, followUp.PatientId, reason),
                    cancellationToken);

            return new Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>.Success(future);
        }
        catch (InvalidOperationException)
        {
            return new Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>.Failure(
                MonitoringError.FollowUpNotScheduled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not cancel the future visits of patient {PatientId}", command.PatientId);
            return new Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>.Failure(
                MonitoringError.UnexpectedError);
        }
    }

    private static Result<ScheduledFollowUp, MonitoringError> Failure(MonitoringError error)
    {
        return new Result<ScheduledFollowUp, MonitoringError>.Failure(error);
    }
}

/// <summary>
///     MA-4 - Submit Pre Visit Check In.
/// </summary>
/// <remarks>
///     Business rule: Check In Raises No Signal (MA-4). This service injects no window, deviation or consistency
///     index, and the event it publishes has no subscriber: a check in that says "Hard" is a message to a person,
///     not evidence against the patient.
/// </remarks>
public class PreVisitCheckInCommandService(
    IPreVisitCheckInRepository checkInRepository,
    IScheduledFollowUpRepository scheduledFollowUpRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PreVisitCheckInCommandService> logger,
    IMediator mediator) : IPreVisitCheckInCommandService
{
    public async Task<Result<PreVisitCheckInView, MonitoringError>> Handle(SubmitPreVisitCheckInCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Value objects, each one to its own error.
        try
        {
            _ = new PlanFeeling(command.Feeling);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.FeelingRequired);
        }

        try
        {
            _ = CheckInDifficulty.ListOf(command.Difficulties);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.UnknownCheckInDifficulty);
        }

        IReadOnlyList<PatientQuestion> questions;
        try
        {
            questions = PreVisitCheckIn.QuestionsOf(command.Questions);
        }
        catch (ArgumentException)
        {
            return Failure(MonitoringError.InvalidCheckInQuestion);
        }

        // Business rule: At Most Three Questions (MA-4).
        if (PreVisitCheckIn.ExceedsQuestionLimit(questions)) return Failure(MonitoringError.TooManyQuestions);

        try
        {
            // 3. Load. Business rule: Only The Patient Writes (MA-4). The visit of another patient does not exist
            //    for this one.
            var followUp = await scheduledFollowUpRepository.FindByIdAsync(command.FollowUpId, cancellationToken);
            if (followUp is null || followUp.PatientId != command.PatientId)
                return Failure(MonitoringError.ScheduledFollowUpNotFound);

            // 4. State guards: the visit is on the calendar, and its hour has not arrived.
            if (!followUp.IsScheduled) return Failure(MonitoringError.FollowUpNotScheduled);
            var now = timeProvider.GetUtcNow();
            // Business rule: Check In Editable Until The Visit (MA-4).
            if (!followUp.AcceptsCheckInAt(now)) return Failure(MonitoringError.CheckInLocked);

            // 5. Mutate. Business rule: One Check In Per Visit (MA-4): the second answer edits the first.
            var checkIn = await checkInRepository.FindByFollowUpIdAsync(followUp.Id.Value, cancellationToken);
            if (checkIn is null)
            {
                checkIn = new PreVisitCheckIn(command, followUp, now);
                await checkInRepository.AddAsync(checkIn, cancellationToken);
            }
            else
            {
                checkIn.Edit(command, followUp, now);
                checkInRepository.Update(checkIn);
            }

            // 6. Persist.
            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. Publish after the commit. Internal, for metrics; nothing subscribes.
            await mediator.PublishAsync(
                new PreVisitCheckInSubmitted(followUp.Id.Value, followUp.PatientId, followUp.PractitionerId),
                cancellationToken);

            return new Result<PreVisitCheckInView, MonitoringError>.Success(
                new PreVisitCheckInView(checkIn, PreVisitCheckIn.IsLockedFor(followUp, now)));
        }
        catch (InvalidOperationException)
        {
            // The aggregate reasserts the hour of the visit; reaching here means it arrived in between.
            return Failure(MonitoringError.CheckInLocked);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save the check in of follow up {FollowUpId}", command.FollowUpId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    private static Result<PreVisitCheckInView, MonitoringError> Failure(MonitoringError error)
    {
        return new Result<PreVisitCheckInView, MonitoringError>.Failure(error);
    }
}
