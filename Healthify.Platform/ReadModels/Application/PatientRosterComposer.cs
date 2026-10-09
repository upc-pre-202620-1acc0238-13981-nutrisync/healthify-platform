using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

namespace Healthify.Platform.ReadModels.Application;

/// <summary>
///     RM-1. One patient of the roster (PR1 "Ana Flores · Vinculada desde 12 mar. 2026", "sin plan · Nueva").
/// </summary>
/// <param name="Link">The link and its status (Active or PendingConsent).</param>
/// <param name="Identity">The name, or null when Iam could not answer.</param>
/// <param name="CareStatus">Baseline, version in force, consultation in progress and open item; null when unknown.</param>
/// <param name="NextFollowUp">The next visit on the calendar, or null.</param>
public record PatientRosterEntry(
    RosterCareLinkItem Link,
    UserIdentityItem? Identity,
    PatientCareStatusItem? CareStatus,
    NextFollowUpItem? NextFollowUp)
{
    /// <summary>
    ///     "Nueva": no baseline yet or no published plan yet. The roster opens PAC-0 or PAC-1 from it.
    /// </summary>
    public bool IsNew => CareStatus is not { HasBaseline: true, ActivePlanVersion: not null };
}

/// <summary>
///     Composes the Patient Roster read model (RM-1) of one practitioner.
/// </summary>
/// <remarks>
///     One call per facade for the whole roster, never one per patient: the links first, then names, care
///     status and next visits for all their patients in one batch each. Every facade degrades on its own, and a
///     patient whose name or status could not be read is still listed.
/// </remarks>
public class PatientRosterComposer(
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IIamContextFacade iamContextFacade,
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IMonitoringContextFacade monitoringContextFacade)
{
    /// <summary>Assembles the roster, oldest link first. Never throws.</summary>
    public async Task<IReadOnlyList<PatientRosterEntry>> Compose(int practitionerId, CancellationToken ct = default)
    {
        var links = await careRelationshipContextFacade.GetRosterCareLinks(practitionerId, ct);
        if (links.Count == 0) return [];

        var patientIds = links.Select(l => l.PatientId).Distinct().ToList();
        var users = await iamContextFacade.GetUsersByIds(patientIds, ct);
        var statuses = await nutritionalCareContextFacade.GetCareStatusByPatientIds(practitionerId, patientIds, ct);
        var followUps = await monitoringContextFacade.GetNextFollowUpsByPatientIds(patientIds, ct);

        return links
            .Select(l => new PatientRosterEntry(l, users.GetValueOrDefault(l.PatientId),
                statuses.GetValueOrDefault(l.PatientId), followUps.GetValueOrDefault(l.PatientId)))
            .ToList();
    }
}
