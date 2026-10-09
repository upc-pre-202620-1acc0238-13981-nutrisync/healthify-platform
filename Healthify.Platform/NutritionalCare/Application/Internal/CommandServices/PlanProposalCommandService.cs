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

/// <summary>
///     NC-10 - Accept Plan Proposal (PR14.IA "Resolver asigna este plan tal cual y se lo publica a Ana. Ajustar abre el
///     plan para que lo edites antes de asignarlo.").
/// </summary>
/// <remarks>
///     Business rule: No Signal Or Algorithm Modifies The Plan Without An Explicit Action Of The Practitioner (NC-10).
///     This is the explicit action: it is reached from the practitioner's POST only, and no policy, worker or handler
///     resolves this service.
///     The order is the specification: 1 the edits (each to its error); 2 only a practitioner, the one whose inbox it
///     is, with the care link active; 3 the item and its proposal; 4 the state (the proposal still Proposed, the item
///     open, the version in force) and the calorie floor, the only hard rule a human edit meets; 5 the version is
///     created with F13 (<c>CreateAdjustedVersion</c>), the previous one superseded and the item resolved; 6 in one
///     transaction; 7 events after the commit, so the existing fan-out publishes the contract to the patient.
/// </remarks>
public class PlanProposalCommandService(
    IReviewItemRepository reviewItemRepository,
    INutritionPlanRepository planRepository,
    INutritionalDiagnosisRepository diagnosisRepository,
    IUnitOfWork unitOfWork,
    IIamContextFacade iamContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    PlanAdjustmentInputReader planAdjustmentInputReader,
    ICalorieFloorPolicy calorieFloorPolicy,
    IClinicalDateProvider clinicalDate,
    ILogger<PlanProposalCommandService> logger,
    IMediator mediator) : IPlanProposalCommandService
{
    public async Task<Result<PlanProposalAcceptanceOutcome, NutritionalCareError>> Handle(
        AcceptPlanProposalCommand command, CancellationToken cancellationToken = default)
    {
        // 1. The edits of PR14.IA-A, each to its own error, before anything loads.
        var edits = command.Edits;
        if (!command.AsIs && edits is null) return Failure(NutritionalCareError.PlanProposalEditsRequired);
        List<Guideline>? editedGuidelines = null;
        PatientFacingMessage? editedMessage = null;
        if (!command.AsIs)
        {
            if (PlanCatalogInput.TryGuidelineCodes(edits!.Guidelines, null, out var codes) is { } badGuideline)
                return Failure(badGuideline);
            editedGuidelines = codes;
            if (!NutritionPlanCommandService.TryPatientMessage(edits.PatientMessage, out editedMessage))
                return Failure(NutritionalCareError.InvalidPatientMessage);
            if (edits.EnergyKcal <= 0m || edits.ProteinG <= 0m || edits.CarbG <= 0m || edits.FatG <= 0m)
                return Failure(NutritionalCareError.PlanProposalOutOfSafetyBounds);
        }

        try
        {
            // 2. Authorization: only a practitioner assigns a plan.
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return Failure(NutritionalCareError.PractitionerOnly);

            // 3. The item and its proposal, in this practitioner's inbox.
            var reviewItem = await reviewItemRepository.FindByIdAsync(command.ReviewItemId, cancellationToken);
            if (reviewItem is null) return Failure(NutritionalCareError.ReviewItemNotFound);
            if (reviewItem.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);
            if (!await careRelationshipContextFacade.IsCareLinkActive(reviewItem.PatientId, command.PractitionerId,
                    cancellationToken))
                return Failure(NutritionalCareError.ActiveCareLinkRequired);
            if (!reviewItem.SignalType.IsSustainedDeviation)
                return Failure(NutritionalCareError.ReviewItemNotSustainedDeviation);
            var proposal = reviewItem.Proposal;
            if (proposal is null) return Failure(NutritionalCareError.PlanProposalNotFound);

            // 4. State guards.
            if (!proposal.IsProposed || !reviewItem.IsOpen)
                return Failure(NutritionalCareError.PlanProposalAlreadyDecided);

            var current = await planRepository.FindActiveByPatientIdAsync(reviewItem.PatientId, cancellationToken);
            if (current is null) return Failure(NutritionalCareError.PlanNotFound);
            if (current.PractitionerId != command.PractitionerId)
                return Failure(NutritionalCareError.PractitionerOnly);
            if (!current.IsPublished || current.IsSuperseded)
                return Failure(NutritionalCareError.PlanVersionAlreadySuperseded);

            var energy = command.AsIs ? proposal.ProposedEnergyKcal : edits!.EnergyKcal;
            var protein = command.AsIs ? proposal.ProposedProteinG : edits!.ProteinG;
            var carb = command.AsIs ? proposal.ProposedCarbG : edits!.CarbG;
            var fat = command.AsIs ? proposal.ProposedFatG : edits!.FatG;

            // Business rule: Calorie Floor (NC-10). The only hard rule an edit meets; the ±25 % band and the
            // coherence of the macros bound the AI, not the practitioner.
            var diagnosis = await diagnosisRepository.FindActiveByPatientIdAsync(reviewItem.PatientId,
                cancellationToken);
            var floor = calorieFloorPolicy.FloorFor(
                await planAdjustmentInputReader.BiologicalSexOfAsync(reviewItem.PatientId, diagnosis,
                    cancellationToken));
            if (!PlanAdjustmentSafety.IsAtOrAboveFloor(energy, floor))
                return Failure(NutritionalCareError.PlanProposalOutOfSafetyBounds);

            // 5. Mutate. Business rule: No Restriction From The AI (NC-10): the restrictions are those in force.
            var guidelines = GuidelinesOf(current, proposal.AddedGuidelines, proposal.RemovedGuidelines,
                editedGuidelines);
            var restrictions = current.Restrictions.Select(r => new DietaryRestriction(r)).ToList();
            var message = command.AsIs ? new PatientFacingMessage(proposal.PatientMessage) : editedMessage;
            // Business rule: Change Reason Required (3.6): "Ajuste por señal: desviación sostenida del {fecha}".
            var changeReason = ChangeReason.SignalAdjustment(
                clinicalDate.DateOf(reviewItem.CreatedAt ?? DateTimeOffset.UtcNow));

            var adjusted = current.CreateAdjustedVersion(
                new AdjustNutritionPlanCommand(current.Id.Value, command.PractitionerId, energy, protein, carb, fat,
                    guidelines.Select(g => g.ToString()).ToList(), current.Restrictions.ToList(), changeReason.Value,
                    null, message?.Value),
                changeReason, guidelines, restrictions, message);
            // Business rule: Previous Version Superseded Never Deleted (3.6).
            current.Supersede();

            // 6. Persist, all or nothing: the new version and the superseded one, then the item that says it was
            //    assigned. If anything fails, the version in force stays in force and the item stays open with its
            //    proposal.
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await planRepository.AddAsync(adjusted, ct);
                planRepository.Update(current);
                await unitOfWork.CompleteAsync(ct);

                reviewItem.ResolveByAssigningPlan(adjusted.Version, command.AsIs);
                reviewItemRepository.Update(reviewItem);
                await unitOfWork.CompleteAsync(ct);
                return adjusted.Id.Value;
            }, cancellationToken);

            // 7. After the commit. NutritionPlanAdjusted fires the existing policy that publishes ActiveTargetsUpdated
            //    (Intake, Care Relationship, Monitoring), with the message of NC-9.
            await mediator.PublishAsync(
                new NutritionPlanAdjusted(adjusted.Id.Value, adjusted.PatientId, adjusted.Version, changeReason.Value),
                cancellationToken);
            await mediator.PublishAsync(
                new PlanVersionSuperseded(current.Id.Value, current.PatientId, current.Version, adjusted.Version),
                cancellationToken);
            await mediator.PublishAsync(
                new ReviewItemResolved(reviewItem.Id.Value, reviewItem.PatientId, true), cancellationToken);
            await mediator.PublishAsync(
                new PlanProposalAccepted(reviewItem.Id.Value, reviewItem.PatientId, adjusted.Version, command.AsIs),
                cancellationToken);

            return new Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Success(
                new PlanProposalAcceptanceOutcome(reviewItem, adjusted));
        }
        catch (ArgumentException)
        {
            return Failure(NutritionalCareError.PlanProposalOutOfSafetyBounds);
        }
        catch (InvalidOperationException)
        {
            return Failure(NutritionalCareError.PlanProposalAlreadyDecided);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error accepting the plan proposal of review item {ReviewItemId}",
                command.ReviewItemId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     The guidelines of the new version. As is: those in force, minus the removed codes, plus the added ones. With
    ///     edits: the codes the practitioner left. Custom guidelines in force are kept either way.
    /// </summary>
    /// <remarks>DECISIÓN NC-10: PR14.IA-A edits catalog chips; "Otra indicación" of the version in force stays.</remarks>
    private static List<Guideline> GuidelinesOf(NutritionPlan current, IReadOnlyList<string> added,
        IReadOnlyList<string> removed, List<Guideline>? edited)
    {
        var codes = edited ?? current.Guidelines
            .Where(g => !g.IsCustom && !removed.Contains(g.Code!, StringComparer.Ordinal))
            .Concat(added.Select(Guideline.FromCode))
            .ToList();
        return codes.Concat(current.Guidelines.Where(g => g.IsCustom)).Distinct().ToList();
    }

    private static Result<PlanProposalAcceptanceOutcome, NutritionalCareError> Failure(NutritionalCareError error)
    {
        return new Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Failure(error);
    }
}
