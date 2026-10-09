using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     NC-8. One published plan version as the patient may read it (PT4, PT4.1).
/// </summary>
/// <remarks>
///     Business rule: Diagnosis And Basis Never Leave The Context (Subflow 3.5). Built from the aggregate on
///     purpose, field by field: there is no diagnosis, calculation basis, proposal, override reason or change
///     reason here, so the patient resource cannot be "enriched" with them by accident.
/// </remarks>
public record PatientPlanVersion(
    int Version,
    DateTimeOffset PublishedAt,
    bool IsActive,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<Guideline> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<string> LegacyRestrictions,
    IReadOnlyList<PlanChange> ChangesFromPrevious,
    string? PatientMessage);
