using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;

/// <summary>
///     Subflows 5.7 and 5.8.
/// </summary>
/// <remarks>
///     Invariant 2 of this bounded context: the index is shown to the patient first, and it only
///     reaches the practitioner after three weeks in Alert. Both halves live in the aggregate, so
///     this service cannot reorder them even by accident.
///     Invariant 3: an escalated signal never modifies a plan. This class does not import, inject or
///     reference anything about plans, and the event it publishes opens an item in a human inbox.
///     TODO: UNCALIBRATED - the threshold of the Consistency Index is the largest technical risk in
///     the project (technical document section 10.2, risk 1). Parameter:
///     Monitoring:ConsistencyAlertThreshold, shipped as 1.5 kilograms per week. That number is
///     derived, not validated: it sits just above the 1.36 kg per week a fully adherent patient
///     scores under the largest deficit DeficitStrategy admits, so no adherent patient is ever
///     flagged by it. It is a floor to escalate from, not a calibration, and the calibration is the
///     distribution of the value carried on ConsistencyIndexRecomputed across the cohort, which this
///     service publishes on every recompute whatever the threshold happens to be. At zero the key is
///     read as unset: the index is computed and shown, no state leaves Normal, and nothing escalates.
/// </remarks>
public class ConsistencyIndexCommandService(
    IConsistencyIndexRepository consistencyIndexRepository,
    IEvaluationWindowRepository evaluationWindowRepository,
    IIntakeContextFacade intakeContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<ConsistencyIndexCommandService> logger,
    IMediator mediator) : IConsistencyIndexCommandService
{
    /// <summary>How many days of the smoothed trend the index is computed over.</summary>
    private const int TrendDays = 90;

    /// <summary>Subflow 5.7 - Recompute Consistency Index.</summary>
    public async Task<Result<ConsistencyIndex, MonitoringError>> Handle(
        RecomputeConsistencyIndexCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var window = await evaluationWindowRepository.FindOpenByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (window is null) return Failure(MonitoringError.EvaluationWindowNotFound);

            // The weight series is the smoothed trend of the readings the patient takes at home,
            // read through the published contract of the context that owns them. The clinical
            // anthropometry series on the window is deliberately not used here: Two Series Never
            // Merged (Subflow 5.3) applies to this calculation as much as to that one.
            var trend = await intakeContextFacade.GetWeightTrendPoints(command.PatientId, TrendDays,
                cancellationToken);

            var weightSeries = trend.Select(p => (p.Date, ValueKg: p.SmoothedValueKg)).ToList();
            var intakeSeries = window.DailyComplianceSeries;

            // Business rules: Both Series Required and No Index Without Both Series (Monitoring and
            // Adherence, Subflow 5.7).
            if (!ConsistencyIndex.CanCompute(weightSeries, intakeSeries))
                return Failure(MonitoringError.BothSeriesRequired);

            var index = await consistencyIndexRepository.FindByPatientIdAsync(command.PatientId,
                cancellationToken);

            if (index is null)
            {
                index = new ConsistencyIndex(command.PatientId);
                await consistencyIndexRepository.AddAsync(index, cancellationToken);
            }
            else
            {
                consistencyIndexRepository.Update(index);
            }

            var alertRaised = index.Recompute(weightSeries, intakeSeries, ConfiguredThreshold());

            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ConsistencyIndexRecomputed(index.PatientId, index.Value, index.State.Value),
                cancellationToken);

            // Business rule: Patient First Always (Monitoring and Adherence, Subflow 5.7). The only
            // subscriber of this event asks the patient. Nothing tells anybody else.
            if (alertRaised)
                await mediator.PublishAsync(new ConsistencyAlertRaised(index.PatientId, index.Value),
                    cancellationToken);

            return Success(index);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not recompute the consistency index of patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>Subflow 5.7 - Prompt Patient.</summary>
    public async Task<Result<ConsistencyIndex, MonitoringError>> Handle(PromptPatientCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var index = await consistencyIndexRepository.FindByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (index is null) return Failure(MonitoringError.BothSeriesRequired);

            // Business rules: Patient First Always, Invitation Tone Never Accusation and Prompt Date
            // Recorded (Monitoring and Adherence, Subflow 5.7). The date is recorded because it is
            // the precondition of the escalation; the tone lives in the localized message the client
            // renders, which is the only place a tone can live.
            // MA-7: it records that the prompt was issued; the patient seeing it is a separate acknowledgement.
            if (!index.PromptPatient()) return Success(index);

            consistencyIndexRepository.Update(index);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new PatientPromptedAboutConsistency(index.PatientId, index.PromptIssuedAt!.Value),
                cancellationToken);

            return Success(index);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not prompt patient {PatientId} about their consistency index",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>
    ///     MA-7 - Acknowledge Consistency Prompt. The app painted the card in PT3: from now on the alert counts as
    ///     shown to the patient. Repeating it keeps the first moment.
    /// </summary>
    public async Task<Result<ConsistencyIndex, MonitoringError>> Handle(
        AcknowledgeConsistencyPromptCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            // 3. Load. Without an index there is no prompt either.
            var index = await consistencyIndexRepository.FindByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (index is null) return Failure(MonitoringError.ConsistencyPromptNotIssued);

            // 4. State guard. Business rule: Only An Issued Prompt Is Acknowledged (MA-7).
            if (index.ShownToPatientAt is not null) return Success(index);
            if (index.PromptIssuedAt is null) return Failure(MonitoringError.ConsistencyPromptNotIssued);

            // 5-6. Business rule: Prompt Date Recorded (Subflow 5.7), now the moment the patient saw it.
            index.MarkShownToPatient(DateTimeOffset.UtcNow);
            consistencyIndexRepository.Update(index);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. After the commit.
            await mediator.PublishAsync(
                new ConsistencyPromptAcknowledged(index.PatientId, index.ShownToPatientAt!.Value), cancellationToken);
            return Success(index);
        }
        catch (InvalidOperationException)
        {
            return Failure(MonitoringError.ConsistencyPromptNotIssued);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record that patient {PatientId} saw their consistency prompt",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>
    ///     MA-7. Days the patient has, after seeing the prompt, before the alert reaches the practitioner.
    ///     Parameter: Monitoring:EscalationDaysAfterPatientAcknowledgement.
    /// </summary>
    /// <remarks>
    ///     DECISIÓN MA-7: the MD asks for "N días desde ese acuse" without a number; 7 days (one week of logging after
    ///     the card) is the default in code, configurable.
    /// </remarks>
    public static int AcknowledgementDays(IConfiguration configuration)
    {
        return configuration.GetValue<int?>("Monitoring:EscalationDaysAfterPatientAcknowledgement") ?? 7;
    }

    /// <summary>Subflow 5.8 - Escalate To Practitioner.</summary>
    public async Task<Result<ConsistencyIndex, MonitoringError>> Handle(
        EscalateToPractitionerCommand command, CancellationToken cancellationToken = default)
    {
        var threshold = ConfiguredThreshold();

        // Business rule: Alert Threshold (Monitoring and Adherence, Subflow 5.7), the uncalibrated
        // one. Nothing is ever raised against a patient on the strength of a number nobody has
        // validated, so an unset threshold refuses the escalation instead of guessing at it.
        if (threshold <= 0m) return Failure(MonitoringError.ConsistencyThresholdNotConfigured);

        try
        {
            var index = await consistencyIndexRepository.FindByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (index is null) return Failure(MonitoringError.BothSeriesRequired);

            // Business rule: Patient Prompt Required Before Escalation (Monitoring and Adherence,
            // Subflow 5.8). Checked here so the caller gets a named answer, and enforced again
            // inside the aggregate so that no other caller can skip it.
            if (index.ShownToPatientAt is null)
                return Failure(MonitoringError.PatientPromptRequiredBeforeEscalation);

            // Business rule: Three Weeks In Alert Required (Monitoring and Adherence, Subflow 5.8)
            var escalationWeeks = configuration.GetValue<int?>("Monitoring:ConsistencyEscalationWeeks")
                                  ?? 3;
            if (!index.ThreeWeeksInAlertElapsed(escalationWeeks, DateTimeOffset.UtcNow))
                return Failure(MonitoringError.ThreeWeeksInAlertRequired);

            // MA-7: and N days since the patient saw the prompt, so they had time to answer it.
            if (!index.ShownToPatientAtLeast(AcknowledgementDays(configuration), DateTimeOffset.UtcNow))
                return Failure(MonitoringError.PatientAcknowledgementTooRecent);

            var careLink = await careRelationshipContextFacade.GetActiveCareLinkByPatientId(
                command.PatientId, cancellationToken);
            if (careLink is null) return Failure(MonitoringError.ActiveCareLinkRequired);

            var evidence = index.Evidence();
            // X-2: the same evidence as numbers, read before escalating (the state and dates do not change).
            var state = index.State.Value;
            var weeksInAlert = index.WeeksInAlertAt(DateTimeOffset.UtcNow);

            if (!index.Escalate()) return Success(index);

            consistencyIndexRepository.Update(index);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Integration event 13 of 13. Business rule: Escalation Notifies Never Modifies The Plan
            // (Monitoring and Adherence, Subflow 5.8). The payload carries a value and a sentence of
            // evidence, and there is nothing on it a plan could be changed from.
            await mediator.PublishAsync(
                new AlertEscalatedToPractitioner(index.PatientId, careLink.PractitionerId, index.Value,
                    evidence, state, index.AlertSinceAt, index.ShownToPatientAt, weeksInAlert), cancellationToken);

            return Success(index);
        }
        catch (InvalidOperationException)
        {
            return Failure(MonitoringError.PatientPromptRequiredBeforeEscalation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not escalate the consistency alert of patient {PatientId}",
                command.PatientId);
            return Failure(MonitoringError.UnexpectedError);
        }
    }

    /// <summary>
    ///     TODO: UNCALIBRATED - the threshold of the Consistency Index is the largest technical risk
    ///     in the project (technical document section 10.2, risk 1). Parameter:
    ///     Monitoring:ConsistencyAlertThreshold, shipped as 1.5. Zero means not configured, and is
    ///     what an absent key falls back to.
    /// </summary>
    private decimal ConfiguredThreshold()
    {
        return configuration.GetValue<decimal?>("Monitoring:ConsistencyAlertThreshold") ?? 0m;
    }

    private static Result<ConsistencyIndex, MonitoringError> Success(ConsistencyIndex index)
    {
        return new Result<ConsistencyIndex, MonitoringError>.Success(index);
    }

    private static Result<ConsistencyIndex, MonitoringError> Failure(MonitoringError error)
    {
        return new Result<ConsistencyIndex, MonitoringError>.Failure(error);
    }
}
