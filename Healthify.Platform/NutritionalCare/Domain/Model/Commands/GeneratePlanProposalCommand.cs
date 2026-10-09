namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-10 - Generate Plan Proposal (IA-8) for a sustained deviation. Issued by the background worker after the
///     policy opened the item, never by a user. It attaches a proposal to the item and never touches a plan.
/// </summary>
/// <param name="ReviewItemId">The item the proposal is for.</param>
public record GeneratePlanProposalCommand(int ReviewItemId);
