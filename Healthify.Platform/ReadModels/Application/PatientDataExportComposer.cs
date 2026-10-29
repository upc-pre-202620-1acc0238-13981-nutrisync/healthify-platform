using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

namespace Healthify.Platform.ReadModels.Application;

/// <summary>
///     Data export. Everything a patient may download about themselves, as the ACL contracts publish it.
/// </summary>
/// <remarks>
///     Business rule: the patient never receives a diagnosis, rationale, calculation basis, BMI or BMI category
///     (ethical rule 3). None of the contracts read here carries them, and the composer does not call the
///     practitioner-only methods (<c>GetActiveDiagnosis</c>, <c>GetClinicalEvaluations</c>). Nothing about another
///     patient is read: every call is keyed by this patient, and the only other people named are their own
///     practitioners.
/// </remarks>
/// <param name="PatientId">Whose data.</param>
/// <param name="Profile">The account, or null when Iam could not answer.</param>
/// <param name="Diary">The whole diary, oldest first.</param>
/// <param name="WeighIns">Every home weigh in, oldest first.</param>
/// <param name="PlanVersions">The published plan versions, oldest first.</param>
/// <param name="FollowUps">Every visit on the calendar, oldest first.</param>
/// <param name="Consultations">The completed consultations, most recent first.</param>
/// <param name="CareLinks">Every care link, oldest first.</param>
/// <param name="PractitionerNames">Full name of each practitioner named above, by id.</param>
public record PatientDataExportComposition(
    int PatientId,
    AccountProfileItem? Profile,
    IReadOnlyList<DiaryExportEntryItem> Diary,
    IReadOnlyList<SelfWeighInExportItem> WeighIns,
    IReadOnlyList<PlanVersionHistoryItem> PlanVersions,
    IReadOnlyList<FollowUpHistoryItem> FollowUps,
    IReadOnlyList<CompletedConsultationItem> Consultations,
    IReadOnlyList<CareLinkHistoryItem> CareLinks,
    IReadOnlyDictionary<int, string> PractitionerNames);

/// <summary>Data export. The downloadable copy: a ZIP of CSV files and its suggested name.</summary>
/// <param name="FileName">healthify-mis-datos-&lt;yyyy-MM-dd&gt;.zip</param>
/// <param name="Content">The ZIP archive.</param>
public record PatientDataExportFile(string FileName, byte[] Content);

/// <summary>
///     Composes the patient data export from the ACL contracts of the five contexts, and nothing else, then hands it
///     to <see cref="PatientDataExportWriter" />.
/// </summary>
/// <remarks>
///     Every section degrades on its own: a facade that cannot answer leaves its file with headers only. Each contract
///     is read once for the whole history (no read per day or per row), and the practitioner names come from one batch
///     read of Iam.
/// </remarks>
public class PatientDataExportComposer(
    IIamContextFacade iamContextFacade,
    IIntakeContextFacade intakeContextFacade,
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IMonitoringContextFacade monitoringContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    PatientDataExportWriter writer)
{
    public async Task<PatientDataExportComposition> Compose(int patientId, CancellationToken ct = default)
    {
        var profile = await iamContextFacade.GetAccountProfile(patientId, ct);
        var diary = await intakeContextFacade.GetDiaryExport(patientId, ct);
        var weighIns = await intakeContextFacade.GetSelfWeighIns(patientId, ct);
        var planVersions = await nutritionalCareContextFacade.GetPlanVersionHistory(patientId, ct);
        var consultations = await nutritionalCareContextFacade.GetCompletedConsultations(patientId, ct);
        var followUps = await monitoringContextFacade.GetFollowUpHistory(patientId, ct);
        var careLinks = await careRelationshipContextFacade.GetCareLinkHistoryByPatientId(patientId, ct);

        var practitionerIds = careLinks.Select(l => l.PractitionerId)
            .Concat(followUps.Select(f => f.PractitionerId))
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        var users = practitionerIds.Count == 0
            ? new Dictionary<int, UserIdentityItem>()
            : await iamContextFacade.GetUsersByIds(practitionerIds, ct);

        return new PatientDataExportComposition(patientId, profile, diary, weighIns, planVersions, followUps,
            consultations, careLinks, users.ToDictionary(u => u.Key, u => u.Value.FullName));
    }

    /// <summary>The composition written as the downloadable ZIP.</summary>
    public async Task<PatientDataExportFile> Export(int patientId, CancellationToken ct = default)
    {
        var composition = await Compose(patientId, ct);
        return writer.Write(composition);
    }
}
