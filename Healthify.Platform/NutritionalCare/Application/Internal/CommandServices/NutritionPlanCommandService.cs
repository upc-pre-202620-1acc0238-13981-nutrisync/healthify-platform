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
    IUnitOfWork unitOfWork,
    ILogger<NutritionPlanCommandService> logger,
    IMediator mediator) : INutritionPlanCommandService
{
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

        // Guidelines and restrictions, each to its own error, before anything loads.
        if (PlanCatalogInput.TryGuidelines(command.Guidelines, command.CustomGuidelines, out var guidelines) is
            { } badGuideline) return Failure(badGuideline);
        if (PlanCatalogInput.TryRestrictions(command.Restrictions, out var restrictions) is { } badRestriction)
            return Failure(badRestriction);
        // The optional message for the patient.
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

    /// <summary>The optional message for the patient: none when absent or blank, false when too long.</summary>
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
