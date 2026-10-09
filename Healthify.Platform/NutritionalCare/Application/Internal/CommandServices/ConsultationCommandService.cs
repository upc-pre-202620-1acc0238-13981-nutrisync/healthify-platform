using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

public class ConsultationCommandService(
    IConsultationRepository consultationRepository,
    IPatientBaselineRepository baselineRepository,
    INutritionalAssessmentRepository assessmentRepository,
    INutritionalDiagnosisRepository diagnosisRepository,
    INutritionPlanRepository planRepository,
    IUnitOfWork unitOfWork,
    IIamContextFacade iamContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IClinicalDateProvider clinicalDate,
    IBmrCalculator bmrCalculator,
    IDefaultTargetParametersPolicy targetParametersPolicy,
    ILogger<ConsultationCommandService> logger,
    IMediator mediator) : IConsultationCommandService
{
    /// <summary>NC-2 (minimal) - Start a consultation (PAC-1 "Iniciar consulta").</summary>
    public async Task<Result<Consultation, NutritionalCareError>> Handle(StartConsultationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 2. Business rules: Practitioner Only Measures, Active Care Link Required (Subflow 3.1)
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return new Result<Consultation, NutritionalCareError>.Failure(NutritionalCareError.PractitionerOnly);
            if (!await careRelationshipContextFacade.IsCareLinkActive(command.PatientId, command.PractitionerId,
                    cancellationToken))
                return new Result<Consultation, NutritionalCareError>.Failure(
                    NutritionalCareError.ActiveCareLinkRequired);

            // Business rule: Baseline Required (NC-2). The consultation reads age, sex and height from it.
            if (await baselineRepository.FindByPatientIdAsync(command.PatientId, cancellationToken) is null)
                return new Result<Consultation, NutritionalCareError>.Failure(NutritionalCareError.BaselineRequired);

            // Business rule: One Consultation In Progress Per Patient (NC-2). The unique index on the
            // generated column in_progress_patient_id backs this check when two requests race.
            if (await consultationRepository.FindInProgressByPatientIdAsync(command.PatientId,
                    cancellationToken) is not null)
                return new Result<Consultation, NutritionalCareError>.Failure(
                    NutritionalCareError.ConsultationAlreadyInProgress);

            // "Primera consulta": no consultation was completed and no assessment predates this one.
            var isFirst = !await consultationRepository.ExistsCompletedForPatientAsync(command.PatientId,
                              cancellationToken)
                          && !await assessmentRepository.ExistsForPatientAsync(command.PatientId, cancellationToken);

            var consultation = new Consultation(command, isFirst);

            await consultationRepository.AddAsync(consultation, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ConsultationStarted(consultation.Id.Value, consultation.PatientId, consultation.PractitionerId,
                    consultation.ScheduledFollowUpId), cancellationToken);

            return new Result<Consultation, NutritionalCareError>.Success(consultation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting a consultation for patient {PatientId}", command.PatientId);
            return new Result<Consultation, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-3 - Step 1 of the consultation (EV-2 "Continuar a diagnóstico"). Creates a new assessment,
    ///     takes its measurement with the baseline height, closes it and attaches it to the consultation.
    /// </summary>
    public async Task<Result<ConsultationMeasurementOutcome, NutritionalCareError>> Handle(
        RecordConsultationMeasurementCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Value objects, each to its own error, before anything is loaded.
        if (StructuredAssessmentInput.CheckPlausibleMeasurement(command.WeightKg, command.WaistCm,
                command.BodyFatPercentage) is { } implausible)
            return Failure(implausible);
        if (StructuredAssessmentInput.TryProtocolChecks(command.ProtocolChecks, out var checklist) is { } noChecks)
            return Failure(noChecks);
        // Required in the consultation: an empty value is rejected like an unknown one.
        if (StructuredAssessmentInput.TryActivityLevel(command.ActivityLevel ?? string.Empty,
                out var activityLevel) is { } badLevel)
            return Failure(badLevel);
        if (StructuredAssessmentInput.TryEatingHabits(command.Habits, out var habits) is { } badHabits)
            return Failure(badHabits);
        if (StructuredAssessmentInput.TryBiochemistry(command.Biochemistry, out var biochemistry) is { } badLab)
            return Failure(badLab);

        try
        {
            // 2. Business rule: Practitioner Only Measures (Subflow 3.1)
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return Failure(NutritionalCareError.PractitionerOnly);

            // 3. Load the consultation. Only the practitioner leading it can save its steps.
            var consultation = await consultationRepository.FindByIdAsync(command.ConsultationId, cancellationToken);
            if (consultation is null) return Failure(NutritionalCareError.ConsultationNotFound);
            if (consultation.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);

            // Business rule: Active Care Link Required (Subflow 3.1). Asked again at every step: consent
            // can be withdrawn while a consultation is paused.
            if (!await careRelationshipContextFacade.IsCareLinkActive(consultation.PatientId,
                    command.PractitionerId, cancellationToken))
                return Failure(NutritionalCareError.ActiveCareLinkRequired);

            // 4. State guards.
            if (!consultation.IsInProgress) return Failure(NutritionalCareError.ConsultationNotInProgress);

            var baseline = await baselineRepository.FindByPatientIdAsync(consultation.PatientId, cancellationToken);
            if (baseline is null) return Failure(NutritionalCareError.BaselineRequired);

            // NC-7: a diagnosis issued on the previous assessment of this consultation is left out; step 2 is
            // issued again on the new one. It was pending, so the active diagnosis was never touched.
            var stalePending = await PendingDiagnosisOf(consultation, cancellationToken);

            // 5. Mutate. DECISIÓN §12-#13: age, sex, height and medical history are a snapshot of the
            //    baseline on the practice's date. Business rule: Correction Creates A New Assessment (Subflow 3.1): going
            //    back to step 1 supersedes the assessment already attached, it never edits it.
            var today = clinicalDate.Today();
            var assessment = NutritionalAssessment.ForConsultation(consultation.Id.Value, command.PractitionerId,
                baseline, today, activityLevel!, habits, biochemistry, consultation.AssessmentId);
            var measurement = assessment.TakeStructuredMeasurement(command.WeightKg, baseline.Height, checklist!,
                command.BodyFatPercentage, command.WaistCm);
            assessment.Close();

            // 6. Persist, all or nothing. Two saves because the consultation needs the identifier the
            //    database assigns to the assessment, inside one transaction so a failure in the second
            //    leaves neither the closed assessment nor the consultation change behind.
            stalePending?.Discard();
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await assessmentRepository.AddAsync(assessment, ct);
                if (stalePending is not null) diagnosisRepository.Update(stalePending);
                await unitOfWork.CompleteAsync(ct);

                consultation.AttachAssessment(assessment.Id.Value);
                consultationRepository.Update(consultation);
                await unitOfWork.CompleteAsync(ct);
                return assessment.Id.Value;
            }, cancellationToken);

            // 7. Events after the final commit, the same ones the stand-alone endpoints publish.
            await mediator.PublishAsync(
                new NutritionalAssessmentRecorded(assessment.Id.Value, assessment.PatientId,
                    assessment.PractitionerId), cancellationToken);
            // Integration event 3 of 13: Monitoring appends an anthropometry point.
            await mediator.PublishAsync(
                new ClinicalMeasurementTaken(assessment.Id.Value, assessment.PatientId, measurement.WeightKg,
                    measurement.TakenAt), cancellationToken);
            await mediator.PublishAsync(
                new AssessmentClosed(assessment.Id.Value, assessment.PatientId, assessment.ClosedAt!.Value),
                cancellationToken);

            return new Result<ConsultationMeasurementOutcome, NutritionalCareError>.Success(
                new ConsultationMeasurementOutcome(consultation, assessment));
        }
        catch (InvalidOperationException)
        {
            return Failure(NutritionalCareError.ConsultationNotInProgress);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording the measurement of consultation {ConsultationId}",
                command.ConsultationId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-4 - Step 2 of the consultation (EV-3 "Continuar a metas"). Issues the coded diagnosis on the
    ///     assessment of step 1, pending until step 4 publishes the plan (NC-7).
    /// </summary>
    public async Task<Result<ConsultationDiagnosisOutcome, NutritionalCareError>> Handle(
        IssueConsultationDiagnosisCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Value objects, each to its own error, before anything is loaded.
        DiagnosisCode code;
        try
        {
            code = new DiagnosisCode(command.Code);
        }
        catch (ArgumentException)
        {
            return DiagnosisFailure(NutritionalCareError.UnknownDiagnosisCode);
        }

        DiagnosisSource source;
        try
        {
            source = new DiagnosisSource(command.Source);
        }
        catch (ArgumentException)
        {
            return DiagnosisFailure(NutritionalCareError.InvalidDiagnosisSource);
        }

        // Business rule: Accepted Suggestion Is Traceable (NC-4).
        if (source.IsAiSuggestionAccepted &&
            (command.AiGenerationId is null or <= 0 || string.IsNullOrWhiteSpace(command.Rationale)))
            return DiagnosisFailure(NutritionalCareError.InvalidDiagnosisSource);
        // NOTE: hotspot. The AiGenerationId is kept as sent, not checked against ai_generations: IAiGenerationLog
        // (Shared) has no lookup by id, and the audit row may be purged (retention, consent withdrawn) while the
        // diagnosis stays, so the id is a trace, not a foreign key.

        // Business rule: Clinical Rationale Required (Subflow 3.2). A written rationale must fit the record.
        if (!string.IsNullOrWhiteSpace(command.Rationale))
            try
            {
                _ = new ClinicalRationale(command.Rationale);
            }
            catch (ArgumentException)
            {
                return DiagnosisFailure(NutritionalCareError.ClinicalRationaleRequired);
            }

        try
        {
            // 2. Authorization: only a practitioner issues a diagnosis (Subflow 3.2).
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return DiagnosisFailure(NutritionalCareError.PractitionerOnly);

            // 3. Load the consultation. Only the practitioner leading it can save its steps.
            var consultation = await consultationRepository.FindByIdAsync(command.ConsultationId, cancellationToken);
            if (consultation is null) return DiagnosisFailure(NutritionalCareError.ConsultationNotFound);
            if (consultation.PractitionerId != command.PractitionerId)
                return DiagnosisFailure(NutritionalCareError.PractitionerOnly);

            // Business rule: Active Care Link Required (Subflow 3.2), asked again at every step.
            if (!await careRelationshipContextFacade.IsCareLinkActive(consultation.PatientId,
                    command.PractitionerId, cancellationToken))
                return DiagnosisFailure(NutritionalCareError.ActiveCareLinkRequired);

            // 4. State guards. Business rule: Steps In Order (NC-2): the diagnosis reads step 1.
            if (!consultation.IsInProgress) return DiagnosisFailure(NutritionalCareError.ConsultationNotInProgress);
            if (consultation.AssessmentId is null)
                return DiagnosisFailure(NutritionalCareError.ConsultationStepOutOfOrder);

            var assessment = await assessmentRepository.FindByIdAsync(consultation.AssessmentId.Value,
                cancellationToken);
            var measurement = assessment?.LatestMeasurement;
            if (assessment is null || measurement is null)
                return DiagnosisFailure(NutritionalCareError.ClinicalMeasurementRequired);
            // Business rule: Closed Assessment Required (Subflow 3.2). Step 1 closes it; asserted anyway.
            if (!assessment.IsClosed) return DiagnosisFailure(NutritionalCareError.ClosedAssessmentRequired);

            // 5. Mutate. Business rule: Consultation Supersedes Previous Diagnosis (NC-4), applied at step 4
            //    (NC-7): the diagnosis of this consultation stays pending and the active one is untouched until
            //    the plan is published, so the active diagnosis and the active plan always change together and a
            //    discarded consultation leaves nothing behind. One pending diagnosis per consultation: repeating
            //    the step discards the previous one (the row is kept).
            var previousPending = await PendingDiagnosisOf(consultation, cancellationToken);
            previousPending?.Discard();

            var diagnosis = NutritionalDiagnosis.FromConsultation(consultation.PatientId, command.PractitionerId,
                assessment.Id.Value, code, source, command.AiGenerationId, command.Rationale, measurement,
                consultation.Id.Value);

            // 6. Persist, all or nothing: the discarded pending diagnosis, the new one and the consultation step.
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                if (previousPending is not null) diagnosisRepository.Update(previousPending);
                await diagnosisRepository.AddAsync(diagnosis, ct);
                await unitOfWork.CompleteAsync(ct);

                consultation.AttachDiagnosis(diagnosis.Id.Value);
                consultationRepository.Update(consultation);
                await unitOfWork.CompleteAsync(ct);
                return diagnosis.Id.Value;
            }, cancellationToken);

            // 7. No event: NutritionalDiagnosisIssued and NutritionalDiagnosisSuperseded follow the commit of step 4
            //    (NC-7), when this diagnosis becomes the active one.
            return new Result<ConsultationDiagnosisOutcome, NutritionalCareError>.Success(
                new ConsultationDiagnosisOutcome(consultation, diagnosis));
        }
        catch (ArgumentException)
        {
            return DiagnosisFailure(NutritionalCareError.ClinicalRationaleRequired);
        }
        catch (InvalidOperationException)
        {
            return DiagnosisFailure(NutritionalCareError.ConsultationNotInProgress);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error issuing the diagnosis of consultation {ConsultationId}",
                command.ConsultationId);
            return DiagnosisFailure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-5 - Step 3 of the consultation (EV-4), ProposeTargetsForConsultation. Calculates the draft plan
    ///     version with the default parameters, or the ones the practitioner changed, on the assessment of
    ///     step 1 (age, sex, height and weight) and the diagnosis of step 2.
    /// </summary>
    public async Task<Result<ConsultationTargetProposalOutcome, NutritionalCareError>> Handle(
        ProposeConsultationTargetsCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Value objects: each parameter the practitioner changed, to its own error, before anything loads.
        //    The complete set is validated again once merged with the defaults.
        if (CheckChangedParameters(command.Parameters) is { } invalidParameter)
            return ProposalFailure(invalidParameter);

        try
        {
            // 2. Authorization: only a practitioner proposes targets (Subflow 3.3).
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return ProposalFailure(NutritionalCareError.PractitionerOnly);

            // 3. Load the consultation. Only the practitioner leading it can save its steps.
            var consultation = await consultationRepository.FindByIdAsync(command.ConsultationId, cancellationToken);
            if (consultation is null) return ProposalFailure(NutritionalCareError.ConsultationNotFound);
            if (consultation.PractitionerId != command.PractitionerId)
                return ProposalFailure(NutritionalCareError.PractitionerOnly);
            if (!await careRelationshipContextFacade.IsCareLinkActive(consultation.PatientId,
                    command.PractitionerId, cancellationToken))
                return ProposalFailure(NutritionalCareError.ActiveCareLinkRequired);

            // 4. State guards. Business rule: Steps In Order (NC-2): targets read steps 1 and 2.
            if (!consultation.IsInProgress) return ProposalFailure(NutritionalCareError.ConsultationNotInProgress);
            if (consultation.AssessmentId is null || consultation.DiagnosisId is null)
                return ProposalFailure(NutritionalCareError.ConsultationStepOutOfOrder);

            // Business rule: Active Diagnosis Required (Subflow 3.3), the one of this consultation: pending until
            // step 4 publishes the plan (NC-7), or active when it was issued before NC-7.
            var diagnosis = await diagnosisRepository.FindByIdAsync(consultation.DiagnosisId.Value,
                cancellationToken);
            if (diagnosis is null || !(diagnosis.IsActive || diagnosis.IsPendingFor(consultation.Id.Value)))
                return ProposalFailure(NutritionalCareError.ActiveDiagnosisRequired);

            // NC-5: age, sex and height come from the assessment of this consultation (step 1), explicitly,
            // not from "the assessment behind the active diagnosis" as the stand-alone endpoint reads them.
            var assessment = await assessmentRepository.FindByIdAsync(consultation.AssessmentId.Value,
                cancellationToken);
            var measurement = assessment?.LatestMeasurement;
            if (assessment is null || measurement is null)
                return ProposalFailure(NutritionalCareError.ClinicalMeasurementRequired);
            if (assessment.ActivityLevel is null) return ProposalFailure(NutritionalCareError.InvalidActivityLevel);

            var defaults = targetParametersPolicy.For(consultation.PatientId, command.PractitionerId,
                diagnosis.Code, assessment.ActivityLevel, measurement.WeightKg);
            var parameters = Merge(defaults, command.Parameters);
            if (TargetCalculation.TryValidate(parameters, out var validated) is { } invalid)
                return ProposalFailure(invalid);
            if (TargetCalculation.TryCalculate(bmrCalculator, validated!, assessment, measurement, out var basis,
                    out var proposal) is { } notCalculable)
                return ProposalFailure(notCalculable);

            // 5. Mutate: recalculate the draft of this consultation, or create it as version N+1.
            var draft = consultation.PlanId is null
                ? null
                : await planRepository.FindByIdAsync(consultation.PlanId.Value, cancellationToken);
            NutritionPlan plan;
            // NC-2: the draft was prescribed on a diagnosis this consultation has since replaced (step 1 or 2
            // repeated). Its signed numbers read stale steps, so it is discarded and a new draft takes its place
            // with the same version number: a discarded draft never was a version.
            var staleDraft = draft is { IsPrescribed: true, IsPublished: false } &&
                             draft.DiagnosisId != diagnosis.Id.Value
                ? draft
                : null;
            if (draft is not null && staleDraft is null)
            {
                // NC-5: only an unprescribed draft can be recalculated (NutritionPlan.Recalculate).
                if (draft.IsPrescribed || draft.IsPublished)
                    return ProposalFailure(NutritionalCareError.PlanNotInExpectedState);
                draft.Recalculate(diagnosis.Id.Value, basis!, proposal!);
                consultation.MarkTargetsSaved();
                plan = draft;

                // 6. Persist: one save covers both changes.
                planRepository.Update(plan);
                consultationRepository.Update(consultation);
                await unitOfWork.CompleteAsync(cancellationToken);
            }
            else
            {
                var nextVersion = staleDraft?.Version ??
                                  await planRepository.GetLatestVersionAsync(consultation.PatientId,
                                      cancellationToken) + 1;
                plan = new NutritionPlan(consultation.PatientId, command.PractitionerId, diagnosis.Id.Value,
                    nextVersion, basis!, proposal!);
                staleDraft?.Discard();

                // 6. Persist, all or nothing: the consultation needs the identifier of the new draft.
                await unitOfWork.ExecuteInTransactionAsync(async ct =>
                {
                    await planRepository.AddAsync(plan, ct);
                    if (staleDraft is not null) planRepository.Update(staleDraft);
                    await unitOfWork.CompleteAsync(ct);

                    consultation.AttachPlanDraft(plan.Id.Value);
                    consultationRepository.Update(consultation);
                    await unitOfWork.CompleteAsync(ct);
                    return plan.Id.Value;
                }, cancellationToken);
            }

            // 7. Event after the commit. Stays inside this context, like the calculation basis.
            await mediator.PublishAsync(
                new TargetsProposed(plan.Id.Value, plan.PatientId, proposal!.EnergyKcal, proposal.ProteinG,
                    proposal.CarbG, proposal.FatG), cancellationToken);

            var inputs = new TargetInputsSummary(assessment.BiologicalSex.Value, assessment.AgeYears,
                measurement.HeightCm, measurement.WeightKg, assessment.ActivityLevel.Value);
            // NC-6: the draft shows the legacy restrictions of the version in force, so the practitioner can
            // map them to a code before publishing.
            var active = await planRepository.FindActiveByPatientIdAsync(consultation.PatientId, cancellationToken);
            return new Result<ConsultationTargetProposalOutcome, NutritionalCareError>.Success(
                new ConsultationTargetProposalOutcome(consultation, plan, inputs,
                    active?.LegacyRestrictions.ToList() ?? []));
        }
        catch (ArgumentException)
        {
            return ProposalFailure(NutritionalCareError.IncompleteCalculationBasis);
        }
        catch (InvalidOperationException)
        {
            return ProposalFailure(NutritionalCareError.PlanNotInExpectedState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error proposing the targets of consultation {ConsultationId}",
                command.ConsultationId);
            return ProposalFailure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-5 - Step 3 of the consultation (EV-4): "Aceptar metas" or "Escribir mis propios valores" on the
    ///     draft plan version.
    /// </summary>
    public async Task<Result<ConsultationTargetsOutcome, NutritionalCareError>> Handle(
        PrescribeConsultationTargetsCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Value objects.
        PrescriptionOutcome outcome;
        try
        {
            outcome = new PrescriptionOutcome(command.Outcome);
        }
        catch (ArgumentException)
        {
            return TargetsFailure(NutritionalCareError.PlanNotInExpectedState);
        }

        // DECISIÓN §12-#1: the override reason stays required in the consultation too (Override Requires
        // Reason, Subflow 3.4). EV-4 gets a short mandatory field; it is clinical traceability.
        if (outcome.IsOverridden && string.IsNullOrWhiteSpace(command.OverrideReason))
            return TargetsFailure(NutritionalCareError.OverrideReasonRequired);

        try
        {
            // 2. Authorization.
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return TargetsFailure(NutritionalCareError.PractitionerOnly);

            // 3. Load the consultation and its draft.
            var consultation = await consultationRepository.FindByIdAsync(command.ConsultationId, cancellationToken);
            if (consultation is null) return TargetsFailure(NutritionalCareError.ConsultationNotFound);
            if (consultation.PractitionerId != command.PractitionerId)
                return TargetsFailure(NutritionalCareError.PractitionerOnly);
            if (!await careRelationshipContextFacade.IsCareLinkActive(consultation.PatientId,
                    command.PractitionerId, cancellationToken))
                return TargetsFailure(NutritionalCareError.ActiveCareLinkRequired);

            // 4. State guards. Business rule: Previous Proposal Required (Subflow 3.4).
            if (!consultation.IsInProgress) return TargetsFailure(NutritionalCareError.ConsultationNotInProgress);
            if (consultation.PlanId is null) return TargetsFailure(NutritionalCareError.ConsultationStepOutOfOrder);
            var plan = await planRepository.FindByIdAsync(consultation.PlanId.Value, cancellationToken);
            if (plan is null) return TargetsFailure(NutritionalCareError.PlanNotFound);
            if (plan.IsPrescribed || plan.IsPublished)
                return TargetsFailure(NutritionalCareError.PlanNotInExpectedState);

            // 5. Mutate.
            var prescribed = TargetCalculation.BuildPrescription(plan, outcome, command.EnergyKcal, command.ProteinG,
                command.CarbG, command.FatG, command.OverrideReason);
            plan.PrescribeTargets(prescribed);
            consultation.MarkTargetsSaved();

            // 6. Persist: one save covers both changes.
            planRepository.Update(plan);
            consultationRepository.Update(consultation);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 7. Exactly one of the two events, never both, as on the stand-alone endpoint.
            if (outcome.IsOverridden)
                await mediator.PublishAsync(
                    new TargetsOverridden(plan.Id.Value, plan.PatientId, prescribed.EnergyKcal,
                        prescribed.OverrideReason!.Value), cancellationToken);
            else
                await mediator.PublishAsync(
                    new TargetsAcceptedAsProposed(plan.Id.Value, plan.PatientId, prescribed.EnergyKcal),
                    cancellationToken);

            return new Result<ConsultationTargetsOutcome, NutritionalCareError>.Success(
                new ConsultationTargetsOutcome(consultation, plan));
        }
        catch (ArgumentException)
        {
            return TargetsFailure(NutritionalCareError.OverrideReasonRequired);
        }
        catch (InvalidOperationException)
        {
            return TargetsFailure(NutritionalCareError.PlanNotInExpectedState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error prescribing the targets of consultation {ConsultationId}",
                command.ConsultationId);
            return TargetsFailure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-7 - Step 4 of the consultation (EV-5 "Publicar y cerrar consulta"), PublishFromConsultation. In one
    ///     transaction: the diagnosis of the consultation replaces the active one, the prescribed draft replaces
    ///     the version in force, and the consultation closes. The stand-alone publication keeps its 409.
    /// </summary>
    public async Task<Result<ConsultationPublicationOutcome, NutritionalCareError>> Handle(
        PublishFromConsultationCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Value objects, each to its own error, before anything is loaded: the Idempotency-Key (NC-2) and the
        //    NC-6 catalogs.
        IdempotencyKey? idempotencyKey = null;
        if (command.IdempotencyKey is not null)
            try
            {
                idempotencyKey = new IdempotencyKey(command.IdempotencyKey);
            }
            catch (ArgumentException)
            {
                return PublicationFailure(NutritionalCareError.InvalidIdempotencyKey);
            }

        if (PlanCatalogInput.TryGuidelineCodes(command.Guidelines, command.CustomGuidelines, out var guidelines) is
            { } badGuideline) return PublicationFailure(badGuideline);
        if (PlanCatalogInput.TryRestrictions(command.Restrictions, out var restrictions) is { } badRestriction)
            return PublicationFailure(badRestriction);
        // NC-9: the optional message for the patient.
        if (!NutritionPlanCommandService.TryPatientMessage(command.PatientMessage, out var patientMessage))
            return PublicationFailure(NutritionalCareError.InvalidPatientMessage);

        try
        {
            // 2. Authorization: only a practitioner publishes (Subflow 3.5).
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return PublicationFailure(NutritionalCareError.PractitionerOnly);

            // 3. Load the consultation. Only the practitioner leading it can save its steps.
            var consultation = await consultationRepository.FindByIdAsync(command.ConsultationId, cancellationToken);
            if (consultation is null) return PublicationFailure(NutritionalCareError.ConsultationNotFound);
            if (consultation.PractitionerId != command.PractitionerId)
                return PublicationFailure(NutritionalCareError.PractitionerOnly);
            if (!await careRelationshipContextFacade.IsCareLinkActive(consultation.PatientId,
                    command.PractitionerId, cancellationToken))
                return PublicationFailure(NutritionalCareError.ActiveCareLinkRequired);

            // NC-2, EV-5.E "Volver a intentarlo": the same key repeats the publication already done. Same result,
            // nothing written and nothing published again, so a retry after a timeout never creates another version.
            if (consultation.IsReplayOf(idempotencyKey)) return await Replay(consultation, cancellationToken);

            // 4. State guards. Business rule: Steps In Order (NC-2): the publication reads steps 1 to 3, and
            //    each one is grounded on the previous one.
            if (!consultation.IsInProgress)
                return PublicationFailure(NutritionalCareError.ConsultationNotInProgress);
            if (consultation.AssessmentId is null || consultation.DiagnosisId is null || consultation.PlanId is null)
                return PublicationFailure(NutritionalCareError.ConsultationStepOutOfOrder);

            var diagnosis = await diagnosisRepository.FindByIdAsync(consultation.DiagnosisId.Value,
                cancellationToken);
            if (diagnosis is null || diagnosis.AssessmentId != consultation.AssessmentId)
                return PublicationFailure(NutritionalCareError.ConsultationStepOutOfOrder);
            var isPending = diagnosis.IsPendingFor(consultation.Id.Value);
            if (!isPending && !diagnosis.IsActive)
                return PublicationFailure(NutritionalCareError.ActiveDiagnosisRequired);

            var plan = await planRepository.FindByIdAsync(consultation.PlanId.Value, cancellationToken);
            if (plan is null) return PublicationFailure(NutritionalCareError.PlanNotFound);
            // The targets were calculated on an earlier diagnosis of this consultation: step 3 comes again.
            if (plan.DiagnosisId != diagnosis.Id.Value)
                return PublicationFailure(NutritionalCareError.ConsultationStepOutOfOrder);
            // Business rule: Previous Proposal Required (Subflow 3.4): only prescribed targets are published.
            if (!plan.IsPrescribed) return PublicationFailure(NutritionalCareError.PreviousProposalRequired);
            if (plan.IsPublished) return PublicationFailure(NutritionalCareError.PlanNotInExpectedState);

            // 5. Mutate. Business rules: One Active Diagnosis Per Patient (Subflow 3.2) and One Active Version Per
            //    Patient (Subflow 3.5), read as "never two active at once": from a consultation the active ones are
            //    replaced, never rejected, inside one transaction. MySQL has no partial index; One Consultation In
            //    Progress Per Patient (unique generated column) serializes the consultations of a patient, which
            //    leaves only the stand-alone endpoints racing against this one, the window info/00 §12.3 documents.
            var previousDiagnosis = isPending
                ? await diagnosisRepository.FindActiveByPatientIdAsync(consultation.PatientId, cancellationToken)
                : null;
            previousDiagnosis?.Supersede();
            if (isPending) diagnosis.Activate();

            var previousPlan = await planRepository.FindActiveByPatientIdAsync(consultation.PatientId,
                cancellationToken);
            // Business rule: Previous Version Superseded Never Deleted (Subflow 3.6).
            previousPlan?.Supersede();

            // Business rule: Change Reason Required (Subflow 3.6), written for the practitioner: the date of the
            // consultation on the practice's calendar.
            if (plan.Version > 1) plan.SetChangeReason(ChangeReason.NewConsultation(clinicalDate.Today()));
            plan.SetPatientMessage(patientMessage);
            plan.PublishWith(guidelines, restrictions);
            // NC-8: "Qué cambió en esta versión", against the version this one replaces.
            plan.RecordChangesFromPrevious(previousPlan);
            if (previousPlan is not null)
            {
                plan.RecordLegacyRestrictionsLeftOut(previousPlan);
                if (plan.DroppedLegacyRestrictions.Count > 0)
                    // Only the count, no clinical text.
                    logger.LogInformation(
                        "Plan version {Version} of patient {PatientId} left out {Count} legacy restrictions not mapped to a code",
                        plan.Version, plan.PatientId, plan.DroppedLegacyRestrictions.Count);
            }

            consultation.Complete(plan.Version, idempotencyKey);

            // 6. Persist, all or nothing: one save inside one transaction. If it fails, the previous diagnosis and
            //    version stay active, the diagnosis stays pending and the consultation stays in progress.
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                if (previousDiagnosis is not null) diagnosisRepository.Update(previousDiagnosis);
                diagnosisRepository.Update(diagnosis);
                if (previousPlan is not null) planRepository.Update(previousPlan);
                planRepository.Update(plan);
                consultationRepository.Update(consultation);
                await unitOfWork.CompleteAsync(ct);
                return plan.Id.Value;
            }, cancellationToken);

            // 7. Events after the commit. The diagnosis events stay inside this context; NutritionPlanPublished
            //    fires the existing policy that publishes ActiveTargetsUpdated (Intake, Care Relationship,
            //    Monitoring); ConsultationCompleted is the integration event of NC-2.
            if (previousDiagnosis is not null)
                await mediator.PublishAsync(
                    new NutritionalDiagnosisSuperseded(previousDiagnosis.Id.Value, previousDiagnosis.PatientId),
                    cancellationToken);
            if (isPending)
                await mediator.PublishAsync(
                    new NutritionalDiagnosisIssued(diagnosis.Id.Value, diagnosis.PatientId, diagnosis.AssessmentId),
                    cancellationToken);
            if (previousPlan is not null)
                await mediator.PublishAsync(
                    new PlanVersionSuperseded(previousPlan.Id.Value, previousPlan.PatientId, previousPlan.Version,
                        plan.Version), cancellationToken);
            await mediator.PublishAsync(
                new NutritionPlanPublished(plan.Id.Value, plan.PatientId, plan.Version, plan.PublishedAt!.Value),
                cancellationToken);
            await mediator.PublishAsync(
                new ConsultationCompleted(consultation.Id.Value, consultation.PatientId, consultation.PractitionerId,
                    plan.Version, consultation.CompletedAt!.Value, consultation.ScheduledFollowUpId),
                cancellationToken);

            return new Result<ConsultationPublicationOutcome, NutritionalCareError>.Success(
                new ConsultationPublicationOutcome(consultation, plan, diagnosis));
        }
        catch (ArgumentException)
        {
            return PublicationFailure(NutritionalCareError.TooManyCustomGuidelines);
        }
        catch (InvalidOperationException)
        {
            return PublicationFailure(NutritionalCareError.PlanNotInExpectedState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error publishing the plan of consultation {ConsultationId}",
                command.ConsultationId);
            return PublicationFailure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>NC-2. The result of a completed publication, read again for a request that repeats it.</summary>
    private async Task<Result<ConsultationPublicationOutcome, NutritionalCareError>> Replay(Consultation consultation,
        CancellationToken cancellationToken)
    {
        var plan = await planRepository.FindByIdAsync(consultation.PlanId!.Value, cancellationToken);
        var diagnosis = await diagnosisRepository.FindByIdAsync(consultation.DiagnosisId!.Value, cancellationToken);
        if (plan is null || diagnosis is null) return PublicationFailure(NutritionalCareError.UnexpectedError);
        return new Result<ConsultationPublicationOutcome, NutritionalCareError>.Success(
            new ConsultationPublicationOutcome(consultation, plan, diagnosis, true));
    }

    /// <summary>
    ///     NC-2 - EV-5 before publishing: keep the chosen restrictions and guidelines with the consultation, so
    ///     "Lo que escribiste no se perdió" holds even if the app closes (EV-5.E).
    /// </summary>
    public async Task<Result<Consultation, NutritionalCareError>> Handle(
        SaveConsultationPublicationDraftCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Value objects: the same NC-6 catalogs the publication checks, each to its own error.
        if (PlanCatalogInput.TryGuidelineCodes(command.Guidelines, command.CustomGuidelines, out _) is
            { } badGuideline) return ConsultationFailure(badGuideline);
        if (PlanCatalogInput.TryRestrictions(command.Restrictions, out var restrictions) is { } badRestriction)
            return ConsultationFailure(badRestriction);
        // NC-9: the message for the patient, kept with the draft as the publication would take it.
        if (!NutritionPlanCommandService.TryPatientMessage(command.PatientMessage, out var patientMessage))
            return ConsultationFailure(NutritionalCareError.InvalidPatientMessage);

        try
        {
            var draft = new PublicationDraft(restrictions.Select(r => r.Value), command.Guidelines,
                command.CustomGuidelines, patientMessage?.Value);

            // 2. Authorization.
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return ConsultationFailure(NutritionalCareError.PractitionerOnly);

            // 3. Load the consultation. Only the practitioner leading it can save its steps.
            var consultation = await consultationRepository.FindByIdAsync(command.ConsultationId, cancellationToken);
            if (consultation is null) return ConsultationFailure(NutritionalCareError.ConsultationNotFound);
            if (consultation.PractitionerId != command.PractitionerId)
                return ConsultationFailure(NutritionalCareError.PractitionerOnly);
            if (!await careRelationshipContextFacade.IsCareLinkActive(consultation.PatientId,
                    command.PractitionerId, cancellationToken))
                return ConsultationFailure(NutritionalCareError.ActiveCareLinkRequired);

            // 4. State guards. Business rule: Steps In Order (NC-2): the publication follows the targets.
            if (!consultation.IsInProgress) return ConsultationFailure(NutritionalCareError.ConsultationNotInProgress);
            if (consultation.PlanId is null) return ConsultationFailure(NutritionalCareError.ConsultationStepOutOfOrder);

            // 5. Mutate and 6. persist. Nothing is published: the draft is not part of the clinical record.
            consultation.SavePublicationDraft(draft);
            consultationRepository.Update(consultation);
            await unitOfWork.CompleteAsync(cancellationToken);

            return new Result<Consultation, NutritionalCareError>.Success(consultation);
        }
        catch (ArgumentException)
        {
            return ConsultationFailure(NutritionalCareError.TooManyCustomGuidelines);
        }
        catch (InvalidOperationException)
        {
            return ConsultationFailure(NutritionalCareError.ConsultationNotInProgress);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving the publication draft of consultation {ConsultationId}",
                command.ConsultationId);
            return ConsultationFailure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     NC-2 (P2) - "Descartar" a consultation in progress. What it saved stays as history; its pending diagnosis
    ///     and its unpublished draft are discarded in the same transaction. The active diagnosis and the version in
    ///     force are not touched: they change only when a consultation publishes (NC-7).
    /// </summary>
    public async Task<Result<Consultation, NutritionalCareError>> Handle(AbandonConsultationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 2. Authorization.
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return ConsultationFailure(NutritionalCareError.PractitionerOnly);

            // 3. Load the consultation. Only the practitioner leading it can discard it. No care link check:
            //    discarding writes nothing about the patient and must stay possible after a revocation.
            var consultation = await consultationRepository.FindByIdAsync(command.ConsultationId, cancellationToken);
            if (consultation is null) return ConsultationFailure(NutritionalCareError.ConsultationNotFound);
            if (consultation.PractitionerId != command.PractitionerId)
                return ConsultationFailure(NutritionalCareError.PractitionerOnly);

            // 4. State guard.
            if (!consultation.IsInProgress) return ConsultationFailure(NutritionalCareError.ConsultationNotInProgress);

            // 5. Mutate.
            var pending = await PendingDiagnosisOf(consultation, cancellationToken);
            pending?.Discard();
            var draft = consultation.PlanId is null
                ? null
                : await planRepository.FindByIdAsync(consultation.PlanId.Value, cancellationToken);
            if (draft is { IsPublished: false, IsDiscarded: false }) draft.Discard();
            else draft = null;
            consultation.Abandon();

            // 6. Persist, all or nothing: one save. No event: nothing active changed and nobody listens.
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                if (pending is not null) diagnosisRepository.Update(pending);
                if (draft is not null) planRepository.Update(draft);
                consultationRepository.Update(consultation);
                await unitOfWork.CompleteAsync(ct);
                return consultation.Id.Value;
            }, cancellationToken);

            return new Result<Consultation, NutritionalCareError>.Success(consultation);
        }
        catch (InvalidOperationException)
        {
            return ConsultationFailure(NutritionalCareError.ConsultationNotInProgress);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error discarding consultation {ConsultationId}", command.ConsultationId);
            return ConsultationFailure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>NC-7. The pending diagnosis this consultation issued in step 2, if it still has one.</summary>
    private async Task<NutritionalDiagnosis?> PendingDiagnosisOf(Consultation consultation,
        CancellationToken cancellationToken)
    {
        if (consultation.DiagnosisId is null) return null;
        var diagnosis = await diagnosisRepository.FindByIdAsync(consultation.DiagnosisId.Value, cancellationToken);
        return diagnosis is not null && diagnosis.IsPendingFor(consultation.Id.Value) ? diagnosis : null;
    }

    /// <summary>Validates, one by one, the parameters "Cambiar parámetros" changed.</summary>
    private static NutritionalCareError? CheckChangedParameters(TargetParametersDto? p)
    {
        if (p is null) return null;
        try
        {
            if (p.Equation is not null) _ = new Equation(p.Equation);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.UnsupportedEquation;
        }

        try
        {
            var kind = p.ReferenceWeightKind ?? ReferenceWeight.Actual;
            // An ideal or adjusted weight is a number the practitioner gives; only the actual one is measured.
            if (p.ReferenceWeightKg is null && !kind.Equals(ReferenceWeight.Actual, StringComparison.OrdinalIgnoreCase))
                return NutritionalCareError.InvalidReferenceWeight;
            if (p.ReferenceWeightKind is not null || p.ReferenceWeightKg is not null)
                _ = new ReferenceWeight(kind, p.ReferenceWeightKg ?? 70m);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidReferenceWeight;
        }

        try
        {
            if (p.DeficitKind is not null) _ = new DeficitStrategy(p.DeficitKind, p.DeficitValue ?? 0m);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidDeficitStrategy;
        }

        if (p.ActivityFactor is < 1.0m or > 2.5m) return NutritionalCareError.InvalidActivityFactor;
        if (p.ProteinGramsPerKg is <= 0m or > 4m || p.FatPercentOfEnergy is < 10m or > 60m)
            return NutritionalCareError.IncompleteCalculationBasis;
        return null;
    }

    private static ProposeTargetsCommand Merge(ProposeTargetsCommand defaults, TargetParametersDto? p)
    {
        if (p is null) return defaults;
        return defaults with
        {
            Equation = p.Equation ?? defaults.Equation,
            ReferenceWeightKind = p.ReferenceWeightKind ?? defaults.ReferenceWeightKind,
            ReferenceWeightKg = p.ReferenceWeightKg ?? defaults.ReferenceWeightKg,
            ActivityFactor = p.ActivityFactor ?? defaults.ActivityFactor,
            DeficitKind = p.DeficitKind ?? defaults.DeficitKind,
            DeficitValue = p.DeficitValue ?? defaults.DeficitValue,
            ProteinGramsPerKg = p.ProteinGramsPerKg ?? defaults.ProteinGramsPerKg,
            FatPercentOfEnergy = p.FatPercentOfEnergy ?? defaults.FatPercentOfEnergy
        };
    }

    private static Result<Consultation, NutritionalCareError> ConsultationFailure(NutritionalCareError error)
    {
        return new Result<Consultation, NutritionalCareError>.Failure(error);
    }

    private static Result<ConsultationPublicationOutcome, NutritionalCareError> PublicationFailure(
        NutritionalCareError error)
    {
        return new Result<ConsultationPublicationOutcome, NutritionalCareError>.Failure(error);
    }

    private static Result<ConsultationTargetProposalOutcome, NutritionalCareError> ProposalFailure(
        NutritionalCareError error)
    {
        return new Result<ConsultationTargetProposalOutcome, NutritionalCareError>.Failure(error);
    }

    private static Result<ConsultationTargetsOutcome, NutritionalCareError> TargetsFailure(
        NutritionalCareError error)
    {
        return new Result<ConsultationTargetsOutcome, NutritionalCareError>.Failure(error);
    }

    private static Result<ConsultationDiagnosisOutcome, NutritionalCareError> DiagnosisFailure(
        NutritionalCareError error)
    {
        return new Result<ConsultationDiagnosisOutcome, NutritionalCareError>.Failure(error);
    }

    private static Result<ConsultationMeasurementOutcome, NutritionalCareError> Failure(NutritionalCareError error)
    {
        return new Result<ConsultationMeasurementOutcome, NutritionalCareError>.Failure(error);
    }
}
