namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Accept Plan Proposal: the explicit action of the practitioner that turns a proposal into the version in
///     force. In one transaction it creates the adjusted version, supersedes the previous one and resolves the item.
/// </summary>
/// <param name="ReviewItemId">The item with the proposal.</param>
/// <param name="PractitionerId">The practitioner, from the token.</param>
/// <param name="AsIs">True: resolve by assigning this plan as is. False: with <paramref name="Edits" />.</param>
/// <param name="Edits">A, required when <paramref name="AsIs" /> is false.</param>
public record AcceptPlanProposalCommand(int ReviewItemId, int PractitionerId, bool AsIs, AdjustedPlanDto? Edits);
