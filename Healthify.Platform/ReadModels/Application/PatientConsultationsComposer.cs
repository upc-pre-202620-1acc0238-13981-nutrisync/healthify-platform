using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;

namespace Healthify.Platform.ReadModels.Application;

/// <summary>
///     RM-5. The consultations of a patient as PT25 shows them: the next visit, the check in of that visit and
///     the consultations already held.
/// </summary>
/// <remarks>
///     Only what the patient can see: no diagnosis, no calculation basis, no weight, BMI or BMI category, because
///     none of the contracts this composer reads publishes them. The AI questions of PT25 are not here (IA-4):
///     they are asked separately so the latency of the AI does not hold this page.
/// </remarks>
/// <param name="PatientId">Whose consultations.</param>
/// <param name="Next">The visit on the calendar, or null (PT25 hides "Próxima consulta").</param>
/// <param name="NextPractitionerFullName">Who the next visit is with; null when Iam could not answer.</param>
/// <param name="CheckIn">The check in of the next visit; null shows "Responder", a value shows PT25.3.</param>
/// <param name="Past">The consultations already held, most recent first.</param>
public record PatientConsultationsComposition(
    int PatientId,
    NextFollowUpItem? Next,
    string? NextPractitionerFullName,
    PreVisitCheckInItem? CheckIn,
    IReadOnlyList<CompletedConsultationItem> Past);

/// <summary>
///     Composes the Patient Consultations read model (RM-5) from the ACL contracts of Monitoring, Nutritional Care
///     and Iam, and nothing else.
/// </summary>
/// <remarks>Every section degrades on its own: a facade that cannot answer leaves its section empty.</remarks>
public class PatientConsultationsComposer(
    IMonitoringContextFacade monitoringContextFacade,
    INutritionalCareContextFacade nutritionalCareContextFacade,
    IIamContextFacade iamContextFacade)
{
    public async Task<PatientConsultationsComposition> Compose(int patientId, CancellationToken ct = default)
    {
        var nextFollowUps = await monitoringContextFacade.GetNextFollowUpsByPatientIds([patientId], ct);
        var next = nextFollowUps.TryGetValue(patientId, out var followUp) ? followUp : null;

        string? practitionerName = null;
        PreVisitCheckInItem? checkIn = null;
        if (next is not null && next.PractitionerId > 0)
        {
            var users = await iamContextFacade.GetUsersByIds([next.PractitionerId], ct);
            practitionerName = users.TryGetValue(next.PractitionerId, out var user) ? user.FullName : null;

            // The check in of this visit only: the facade returns the one of the open visit of this pair.
            var latest = await monitoringContextFacade.GetLatestCheckInForPatient(patientId, next.PractitionerId,
                ct);
            checkIn = latest?.FollowUpId == next.FollowUpId ? latest : null;
        }

        var past = await nutritionalCareContextFacade.GetCompletedConsultations(patientId, ct);

        return new PatientConsultationsComposition(patientId, next, practitionerName, checkIn, past);
    }
}
