using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;

/// <summary>NC-8. Takes the patient DTO, never the aggregate.</summary>
public static class PatientPlanVersionResourceAssembler
{
    public static PatientPlanVersionResource ToResource(PatientPlanVersion version)
    {
        return new PatientPlanVersionResource(
            version.Version,
            version.PublishedAt,
            version.IsActive,
            version.EnergyKcal,
            version.ProteinG,
            version.CarbG,
            version.FatG,
            version.Guidelines.Select(g => new GuidelineItemResource(g.Code, g.Custom)).ToList(),
            version.Restrictions,
            version.ChangesFromPrevious
                .Select(c => new PlanChangeResource(c.Type, c.Code, c.Custom, c.Macro, c.From, c.To)).ToList(),
            version.PatientMessage,
            version.LegacyRestrictions);
    }
}
