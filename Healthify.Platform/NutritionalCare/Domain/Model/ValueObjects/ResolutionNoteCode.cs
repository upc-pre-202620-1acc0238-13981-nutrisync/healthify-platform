namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     X-2. Who wrote the resolution note of a review item, and what it says when the system wrote it. The client words
///     a system code in the reader's language from <see cref="ResolutionNoteData" />; <c>resolution_note</c> keeps the
///     Spanish sentence as the legacy fallback. A note the practitioner writes is <see cref="Custom" />: clinical text,
///     kept as written and never translated.
/// </summary>
public static class ResolutionNoteCode
{
    /// <summary>Written by the practitioner when resolving (PR14 "Nota").</summary>
    public const string Custom = "Custom";

    /// <summary>NC-10. "Plan v2 asignado (propuesta IA aceptada tal cual)". Data: <c>planVersion</c>.</summary>
    public const string PlanAssignedAsIs = "PlanAssignedAsIs";

    /// <summary>NC-10. "Plan v2 asignado (propuesta IA aceptada con ediciones)". Data: <c>planVersion</c>.</summary>
    public const string PlanAssignedWithEdits = "PlanAssignedWithEdits";

    /// <summary>
    ///     NC-10. Resolved without assigning the AI proposal and without a note of the practitioner: the proposal was
    ///     discarded and the plan stayed as it was. No sentence was ever written for it (the note is null), so there is
    ///     no legacy text. No data.
    /// </summary>
    public const string ProposalDiscarded = "ProposalDiscarded";

    /// <summary>The closed list, for the client.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Custom, PlanAssignedAsIs, PlanAssignedWithEdits, ProposalDiscarded];
}

/// <summary>
///     X-2. Parameters of a <see cref="ResolutionNoteCode" />, stored as JSON in <c>review_items.resolution_note_data</c>:
///     <c>{ "planVersion": 2 }</c>.
/// </summary>
/// <param name="PlanVersion">PlanAssignedAsIs and PlanAssignedWithEdits: the version assigned.</param>
public sealed record ResolutionNoteData(int? PlanVersion);
