using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Events;

/// <summary>The daily numbers of the published contract, grouped exactly as the contract defines them.</summary>
public record DailyTargets(decimal EnergyKcal, decimal ProteinG, decimal CarbG, decimal FatG);

/// <summary>
///     NC-6. One guideline of the published contract: a catalog code (the client translates it) or a custom
///     text (clinical data, never translated). Exactly one of the two is set.
/// </summary>
public record GuidelineItem(string? Code, string? Custom);

/// <summary>
///     NC-8. One line of "Qué cambió en esta versión": a type (GuidelineAdded, GuidelineRemoved, RestrictionAdded,
///     RestrictionRemoved, EnergyChanged, MacroChanged, NoTargetChanges) and what it is about. Only targets,
///     guidelines and restrictions: what the patient already receives.
/// </summary>
public record PlanChangeItem(string Type, string? Code, string? Custom, string? Macro, decimal? From, decimal? To);

/// <summary>
///     The Published Language of this bounded context, and the only thing about a plan that ever
///     reaches the patient.
/// </summary>
/// <remarks>
///     Integration events 4, 5 and 6 of 13: the same event feeds three policies, in
///     Intake and Body Response (Subflow 4.1), Monitoring and Adherence (Subflow 5.2) and
///     Care Relationship (Subflow 2.4).
///     Business rule: Contract Carries Targets Guidelines And Restrictions Only, and
///     Diagnosis And Basis Never Leave The Context (Subflow 3.5). The payload below is the whole
///     contract: patient, version, validity, the daily targets, the guidelines and the restrictions.
///     There is no diagnosis here, no rationale, no equation, no reference weight, no activity
///     factor and no deficit. A patient does not need to read their diagnosis in order to log what
///     they ate, and exposing it would open a clinical problem this platform does not manage.
///     NC-6 extends the contract, it does not replace it: <c>Guidelines</c> keeps the original list of
///     strings (a code, or the custom text); <c>GuidelineItems</c> says which is which; <c>Restrictions</c>
///     carries <c>DietaryRestriction</c> codes; <c>LegacyRestrictions</c> the free text restrictions written
///     before the closed list, which the patient keeps seeing.
///     NC-8 extends it again: <c>ChangesFromPrevious</c> ("Qué cambió", null for versions published before NC-8)
///     and <c>PatientMessage</c> (NC-9).
/// </remarks>
public record ActiveTargetsUpdated(
    int PatientId,
    int PlanVersion,
    DateTimeOffset ValidFrom,
    DailyTargets DailyTargets,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<GuidelineItem>? GuidelineItems = null,
    IReadOnlyList<string>? LegacyRestrictions = null,
    IReadOnlyList<PlanChangeItem>? ChangesFromPrevious = null,
    string? PatientMessage = null) : DomainEventBase;
