using Cortex.Mediator;
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

public class NutritionPlanCommandService(
    INutritionPlanRepository planRepository,
    INutritionalDiagnosisRepository diagnosisRepository,
    INutritionalAssessmentRepository assessmentRepository,
    IUnitOfWork unitOfWork,
    IBmrCalculator bmrCalculator,
    ILogger<NutritionPlanCommandService> logger,
    IMediator mediator) : INutritionPlanCommandService
{
    /// <summary>
    ///     Subflow 3.3 - Propose Targets. Deterministic arithmetic on parameters a human chose.
    /// </summary>
    public async Task<Result<NutritionPlan, NutritionalCareError>> Handle(ProposeTargetsCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Value object validation. Business rule: Complete Calculation Basis Required (Subflow 3.3).
        //    Each component reports its own error, so the practitioner learns which parameter is wrong.
        if (TargetCalculation.TryValidate(command, out var parameters) is { } invalid) return Failure(invalid);

        try
        {
            // 2. Business rule: Active Diagnosis Required (Subflow 3.3). No plan exists without a
            //    diagnosis that grounds it.
            var diagnosis = await diagnosisRepository.FindActiveByPatientIdAsync(command.PatientId,
                cancellationToken);
            if (diagnosis is null)
                return Failure(NutritionalCareError.ActiveDiagnosisRequired);
            if (diagnosis.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);

            // The anthropometry the equations read comes from the assessment behind the diagnosis.
            var assessment = await assessmentRepository.FindByIdAsync(diagnosis.AssessmentId,
                cancellationToken);
            var measurement = assessment?.LatestMeasurement;
            if (assessment is null || measurement is null)
                return Failure(NutritionalCareError.ClinicalMeasurementRequired);

            // 3. The arithmetic, in the order the rules state it.
            if (TargetCalculation.TryCalculate(bmrCalculator, parameters!, assessment, measurement, out var basis,
                    out var proposal) is { } notCalculable)
                return Failure(notCalculable);

            var nextVersion = await planRepository.GetLatestVersionAsync(command.PatientId,
                cancellationToken) + 1;

            var plan = new NutritionPlan(command.PatientId, command.PractitionerId, diagnosis.Id.Value,
                nextVersion, basis!, proposal!);

            await planRepository.AddAsync(plan, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Stays inside this context: the calculation basis is professional information, and the
            // patient receives the result rather than the procedure.
            await mediator.PublishAsync(
                new TargetsProposed(plan.Id.Value, plan.PatientId, proposal!.EnergyKcal, proposal.ProteinG,
                    proposal.CarbG, proposal.FatG), cancellationToken);

            return new Result<NutritionPlan, NutritionalCareError>.Success(plan);
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.IncompleteCalculationBasis);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error proposing targets for patient {PatientId}", command.PatientId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     Subflow 3.4 - Prescribe Targets. One command, and exactly one of two events.
    /// </summary>
    public async Task<Result<NutritionPlan, NutritionalCareError>> Handle(PrescribeTargetsCommand command,
        CancellationToken cancellationToken = default)
    {
        PrescriptionOutcome outcome;
        try
        {
            outcome = new PrescriptionOutcome(command.Outcome);
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.PlanNotInExpectedState);
        }

        // Business rule: Override Requires Reason (Subflow 3.4), checked before anything loads.
        if (outcome.IsOverridden && string.IsNullOrWhiteSpace(command.OverrideReason))
            return Failure(NutritionalCareError.OverrideReasonRequired);

        try
        {
            var plan = await planRepository.FindByIdAsync(command.PlanId, cancellationToken);
            if (plan is null) return Failure(NutritionalCareError.PlanNotFound);
            if (plan.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);
            if (plan.IsPrescribed) return Failure(NutritionalCareError.PlanNotInExpectedState);

            // Business rule: Previous Proposal Required (Subflow 3.4). Accepting means signing the
            // numbers the arithmetic produced; overriding means replacing them and saying why.
            var prescribed = TargetCalculation.BuildPrescription(plan, outcome, command.EnergyKcal, command.ProteinG,
                command.CarbG, command.FatG, command.OverrideReason);

            plan.PrescribeTargets(prescribed);

            planRepository.Update(plan);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Exactly one of the two, never both.
            if (outcome.IsOverridden)
                await mediator.PublishAsync(
                    new TargetsOverridden(plan.Id.Value, plan.PatientId, prescribed.EnergyKcal,
                        prescribed.OverrideReason!.Value), cancellationToken);
            else
                await mediator.PublishAsync(
                    new TargetsAcceptedAsProposed(plan.Id.Value, plan.PatientId, prescribed.EnergyKcal),
                    cancellationToken);

            return new Result<NutritionPlan, NutritionalCareError>.Success(plan);
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.OverrideReasonRequired);
        }
        catch (InvalidOperationException)
        {
            return Failure(NutritionalCareError.PlanNotInExpectedState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error prescribing targets on plan {PlanId}", command.PlanId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>Subflow 3.5 - Publish Nutrition Plan. Clinical phase 3.</summary>
    public async Task<Result<NutritionPlan, NutritionalCareError>> Handle(
        PublishNutritionPlanCommand command, CancellationToken cancellationToken = default)
    {
        // 1. NC-6: guidelines and restrictions, each to its own error, before anything loads.
        if (PlanCatalogInput.TryGuidelines(command.Guidelines, command.CustomGuidelines, out var guidelines) is
            { } badGuideline) return Failure(badGuideline);
        if (PlanCatalogInput.TryRestrictions(command.Restrictions, out var restrictions) is { } badRestriction)
            return Failure(badRestriction);
        // NC-9: the optional message for the patient.
        if (!TryPatientMessage(command.PatientMessage, out var patientMessage))
            return Failure(NutritionalCareError.InvalidPatientMessage);

        try
        {
            var plan = await planRepository.FindByIdAsync(command.PlanId, cancellationToken);
            if (plan is null) return Failure(NutritionalCareError.PlanNotFound);
            if (plan.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);
            if (!plan.IsPrescribed) return Failure(NutritionalCareError.PreviousProposalRequired);
            if (plan.IsPublished) return Failure(NutritionalCareError.PlanNotInExpectedState);

            // Business rule: No Plan Without Diagnosis (Subflow 3.5)
            // NC-7: the draft of a consultation reads a diagnosis that is still pending (or was discarded); it is
            // published only by its consultation (step 4), together with that diagnosis.
            var diagnosis = await diagnosisRepository.FindByIdAsync(plan.DiagnosisId, cancellationToken);
            if (diagnosis is null || diagnosis.IsPending || diagnosis.IsDiscarded)
                return Failure(NutritionalCareError.PlanRequiresDiagnosis);

            // Business rule: One Active Version Per Patient (Subflow 3.5)
            var active = await planRepository.FindActiveByPatientIdAsync(plan.PatientId, cancellationToken);
            if (active is not null && active.Id.Value != plan.Id.Value)
                return Failure(NutritionalCareError.PatientAlreadyHasActivePlanVersion);

            plan.SetPatientMessage(patientMessage);
            plan.PublishWith(guidelines, restrictions);
            // NC-8: "Qué cambió en esta versión", against the last version published before this one (none for
            // the first version: the stand-alone path only publishes when there is no active version).
            var previous = (await planRepository.ListByPatientIdAsync(plan.PatientId, cancellationToken))
                .Where(p => p.IsPublished && p.Version < plan.Version)
                .MaxBy(p => p.Version);
            plan.RecordChangesFromPrevious(previous);

            planRepository.Update(plan);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Consumed by this context own policy, which publishes the active targets contract.
            await mediator.PublishAsync(
                new NutritionPlanPublished(plan.Id.Value, plan.PatientId, plan.Version,
                    plan.PublishedAt!.Value), cancellationToken);

            return new Result<NutritionPlan, NutritionalCareError>.Success(plan);
        }
        catch (InvalidOperationException)
        {
            return Failure(NutritionalCareError.PlanNotInExpectedState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error publishing plan {PlanId}", command.PlanId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     Subflows 3.5 and 3.6 - Publish Active Targets. Reached only from a policy. This is the one
    ///     place where something about a plan leaves this bounded context, and it carries the reduced
    ///     contract only.
    /// </summary>
    public async Task<Result<NutritionPlan, NutritionalCareError>> Handle(
        PublishActiveTargetsCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await planRepository.FindByIdAsync(command.PlanId, cancellationToken);
            if (plan is null) return Failure(NutritionalCareError.PlanNotFound);
            if (!plan.IsPublished) return Failure(NutritionalCareError.PlanNotInExpectedState);

            // Business rule: Calculation Basis Always Recorded (Subflow 3.5). Structurally guaranteed
            // by the constructor; asserted here because publishing is the last chance to notice.
            if (plan.CalculationBasis is null) return Failure(NutritionalCareError.CalculationBasisRequired);

            var targets = plan.PrescribedTargets!;

            // Business rule: Contract Carries Targets Guidelines And Restrictions Only, and
            // Diagnosis And Basis Never Leave The Context (Subflow 3.5). Everything the payload
            // carries is below; the diagnosis, the rationale and the calculation basis are not in it.
            await mediator.PublishAsync(
                new ActiveTargetsUpdated(
                    plan.PatientId,
                    plan.Version,
                    plan.PublishedAt!.Value,
                    new DailyTargets(targets.EnergyKcal, targets.ProteinG, targets.CarbG, targets.FatG),
                    plan.Guidelines.Select(g => g.ToString()).ToList(),
                    plan.Restrictions,
                    plan.Guidelines.Select(g => new GuidelineItem(g.Code, g.Custom)).ToList(),
                    plan.LegacyRestrictions,
                    plan.ChangesFromPrevious?.Select(c =>
                        new PlanChangeItem(c.Type, c.Code, c.Custom, c.Macro, c.From, c.To)).ToList(),
                    plan.PatientMessage),
                cancellationToken);

            return new Result<NutritionPlan, NutritionalCareError>.Success(plan);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error publishing active targets for plan {PlanId}", command.PlanId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>Subflow 3.6 - Adjust Nutrition Plan, between visits.</summary>
    public async Task<Result<NutritionPlan, NutritionalCareError>> Handle(
        AdjustNutritionPlanCommand command, CancellationToken cancellationToken = default)
    {
        // Business rule: Change Reason Required (Subflow 3.6), checked before anything loads.
        ChangeReason changeReason;
        try
        {
            changeReason = new ChangeReason(command.ChangeReason);
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.ChangeReasonRequired);
        }

        // NC-6: guidelines and restrictions, each to its own error, before anything loads.
        if (PlanCatalogInput.TryGuidelines(command.Guidelines, command.CustomGuidelines, out var guidelines) is
            { } badGuideline) return Failure(badGuideline);
        if (PlanCatalogInput.TryRestrictions(command.Restrictions, out var restrictions) is { } badRestriction)
            return Failure(badRestriction);
        // NC-9: the optional message for the patient.
        if (!TryPatientMessage(command.PatientMessage, out var patientMessage))
            return Failure(NutritionalCareError.InvalidPatientMessage);

        try
        {
            var current = await planRepository.FindByIdAsync(command.PlanId, cancellationToken);
            if (current is null) return Failure(NutritionalCareError.PlanNotFound);
            if (current.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);
            if (!current.IsPublished) return Failure(NutritionalCareError.PlanNotInExpectedState);
            if (current.IsSuperseded) return Failure(NutritionalCareError.PlanVersionAlreadySuperseded);

            var adjusted = current.CreateAdjustedVersion(command, changeReason, guidelines, restrictions,
                patientMessage);
            if (adjusted.DroppedLegacyRestrictions.Count > 0)
                // The texts stay on both rows (kept on the old one, recorded on the new one); the log keeps
                // only the count, no clinical text.
                logger.LogInformation(
                    "Plan version {Version} of patient {PatientId} left out {Count} legacy restrictions not mapped to a code",
                    adjusted.Version, adjusted.PatientId, adjusted.DroppedLegacyRestrictions.Count);

            // Business rule: Previous Version Superseded Never Deleted (Subflow 3.6). The old row
            // stays exactly as it was, dated, so Plan Version History remains readable.
            current.Supersede();

            await planRepository.AddAsync(adjusted, cancellationToken);
            planRepository.Update(current);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new NutritionPlanAdjusted(adjusted.Id.Value, adjusted.PatientId, adjusted.Version,
                    changeReason.Value), cancellationToken);
            await mediator.PublishAsync(
                new PlanVersionSuperseded(current.Id.Value, current.PatientId, current.Version,
                    adjusted.Version), cancellationToken);

            return new Result<NutritionPlan, NutritionalCareError>.Success(adjusted);
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.ChangeReasonRequired);
        }
        catch (InvalidOperationException)
        {
            return Failure(NutritionalCareError.PlanNotInExpectedState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adjusting plan {PlanId}", command.PlanId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>NC-9. The optional message for the patient: none when absent or blank, false when too long.</summary>
    internal static bool TryPatientMessage(string? value, out PatientFacingMessage? message)
    {
        try
        {
            message = PatientFacingMessage.FromOptional(value);
            return true;
        }
        catch (ArgumentException)
        {
            message = null;
            return false;
        }
    }

    private static Result<NutritionPlan, NutritionalCareError> Failure(NutritionalCareError error)
    {
        return new Result<NutritionPlan, NutritionalCareError>.Failure(error);
    }
}
