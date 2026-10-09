using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>
///     NC-10. A practitioner assigned the plan an AI proposed, as is or with edits. Stays inside Nutritional Care; the
///     version reaches the patient through NutritionPlanAdjusted and its existing fan-out.
/// </summary>
/// <param name="ReviewItemId">The item.</param>
/// <param name="PatientId">Whose plan.</param>
/// <param name="PlanVersion">The version assigned.</param>
/// <param name="AcceptedAsIs">Whether it was assigned without edits.</param>
public record PlanProposalAccepted(int ReviewItemId, int PatientId, int PlanVersion, bool AcceptedAsIs)
    : DomainEventBase;
