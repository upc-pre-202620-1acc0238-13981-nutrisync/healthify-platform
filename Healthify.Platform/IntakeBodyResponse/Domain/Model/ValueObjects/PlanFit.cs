namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     A suggestion for "was this meal in your plan?", computed by this server from the published contract
///     (<c>ActiveTargetsCache</c>) and the day's diary, never by the AI. The app preselects it and the patient may
///     change it: what is logged is always the patient's own answer (<see cref="PlanAdherence" />).
/// </summary>
/// <remarks>
///     Descriptive, like the answer it suggests: it says what a meal looks like next to the plan, never that the
///     meal was wrong, and nothing computes a deviation or a penalty from it.
/// </remarks>
/// <param name="SuggestedAdherence"><see cref="PlanAdherence.InPlan" /> or <see cref="PlanAdherence.OffPlan" />.</param>
/// <param name="ViolatedRestrictions">The restriction codes of the plan the food seems to break; empty when none.</param>
/// <param name="Reason">Why: one of <see cref="PlanFitReason" />.</param>
/// <param name="EnergyKcal">The energy of the portion, rounded.</param>
/// <param name="RemainingKcal">What was left today before this meal, or null when the day was not given.</param>
public sealed record PlanFit(
    string SuggestedAdherence,
    IReadOnlyList<string> ViolatedRestrictions,
    string Reason,
    decimal EnergyKcal,
    decimal? RemainingKcal)
{
    public bool SuggestsInPlan => SuggestedAdherence == PlanAdherence.InPlan;
}

/// <summary>Why a <see cref="PlanFit" /> suggests what it suggests. The app turns each code into its own text.</summary>
public static class PlanFitReason
{
    /// <summary>Nothing in the plan speaks against it.</summary>
    public const string Fits = "Fits";

    /// <summary>The food seems to carry something a restriction of the plan excludes.</summary>
    public const string BreaksRestriction = "BreaksRestriction";

    /// <summary>The portion clearly goes beyond what was left today.</summary>
    public const string ExceedsRemaining = "ExceedsRemaining";
}

/// <summary>One food the patient is about to log: a catalog entry, a portion and the other names it goes by.</summary>
/// <param name="ReferenceFoodId">The catalog entry.</param>
/// <param name="Grams">The portion.</param>
/// <param name="AlsoKnownAs">
///     Other names of the same dish, checked against the restrictions too (the name the AI gave a photo: «Ceviche de
///     langostinos» resolved to «Ceviche»).
/// </param>
public sealed record PlanFitCandidate(int ReferenceFoodId, decimal Grams, IReadOnlyList<string>? AlsoKnownAs = null);
