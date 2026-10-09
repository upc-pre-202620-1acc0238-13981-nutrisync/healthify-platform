using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;

namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;

/// <summary>Subflow 5.10 - Record Referral. The practitioner comes from the token, never the payload.</summary>
public static class RecordReferralCommandAssembler
{
    public static RecordReferralCommand ToCommand(int practitionerId, RecordReferralResource resource)
    {
        return new RecordReferralCommand(resource.PatientId, practitionerId, resource.Specialty,
            resource.Reason);
    }
}

/// <summary>RM-4 - Close Referral. The practitioner comes from the token.</summary>
public static class CloseReferralCommandAssembler
{
    public static CloseReferralCommand ToCommand(int referralId, int practitionerId)
    {
        return new CloseReferralCommand(referralId, practitionerId);
    }
}

/// <summary>Subflow 5.10 - Schedule Follow Up. The practitioner comes from the token.</summary>
public static class ScheduleFollowUpCommandAssembler
{
    public static ScheduleFollowUpCommand ToCommand(int practitionerId, ScheduleFollowUpResource resource)
    {
        return new ScheduleFollowUpCommand(resource.PatientId, practitionerId, resource.ScheduledFor,
            resource.Preparation, resource.Modality ?? ConsultationModality.InPerson);
    }
}

/// <summary>MA-4. The check in of a visit. The patient comes from the token, the visit from the route.</summary>
public static class SubmitPreVisitCheckInCommandAssembler
{
    /// <param name="followUpId">The visit, from the route.</param>
    /// <param name="patientId">The patient, from the token.</param>
    /// <param name="resource">The body.</param>
    /// <param name="requestLanguage">
    ///     X-2. The language of the request (Accept-Language or the <c>lang</c> claim): the one an AI suggestion was
    ///     read in when the client does not send it. Only es or en is kept.
    /// </param>
    public static SubmitPreVisitCheckInCommand ToCommand(int followUpId, int patientId,
        SubmitPreVisitCheckInResource resource, string? requestLanguage = null)
    {
        var fallback = requestLanguage is "es" or "en" ? requestLanguage : null;
        return new SubmitPreVisitCheckInCommand(followUpId, patientId, resource.Feeling, resource.Difficulties,
            resource.Questions?.Select(q => new PreVisitCheckInQuestionInput(q.Text, q.Origin, q.AiGenerationId,
                    q.Language ?? fallback))
                .ToList());
    }
}

/// <summary>MA-5. Cancel and reschedule. The practitioner comes from the token, the visit from the route.</summary>
public static class FollowUpChangeCommandAssembler
{
    public static CancelFollowUpCommand ToCommand(int followUpId, int practitionerId, CancelFollowUpResource? resource)
    {
        return new CancelFollowUpCommand(followUpId, practitionerId, resource?.Reason);
    }

    public static RescheduleFollowUpCommand ToCommand(int followUpId, int practitionerId,
        RescheduleFollowUpResource resource)
    {
        return new RescheduleFollowUpCommand(followUpId, practitionerId, resource.ScheduledFor,
            resource.Preparation);
    }
}

/// <summary>MA-2. The agenda filters of the query string. The practitioner comes from the route.</summary>
public static class PractitionerAgendaQueryAssembler
{
    /// <summary>False when <paramref name="state" /> is not a follow up state.</summary>
    public static bool TryToQuery(int practitionerId, string? state, DateTimeOffset? from,
        out GetPractitionerAgendaQuery query)
    {
        query = new GetPractitionerAgendaQuery(practitionerId, null, from);
        if (string.IsNullOrWhiteSpace(state)) return true;

        try
        {
            query = query with { State = new FollowUpState(state.Trim()) };
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>MA-3. The state filter of the patient's visits. The patient comes from the route.</summary>
public static class PatientFollowUpsQueryAssembler
{
    /// <summary>False when <paramref name="state" /> is not a follow up state.</summary>
    public static bool TryToQuery(int patientId, string? state, out GetFollowUpsByPatientIdQuery query)
    {
        query = new GetFollowUpsByPatientIdQuery(patientId);
        if (string.IsNullOrWhiteSpace(state)) return true;

        try
        {
            query = query with { State = new FollowUpState(state.Trim()) };
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public static class DailyComplianceResourceAssembler
{
    public static DailyComplianceResource ToResource(DailyCompliance day)
    {
        return new DailyComplianceResource(day.Date, day.Outcome, day.ObservedEnergyKcal,
            day.TargetEnergyKcal, day.PlanVersion, day.EntryCount);
    }
}

/// <summary>MA-6. <c>?from=&amp;to=</c> of <c>GET /patients/{id}/daily-compliance</c>.</summary>
public static class DailyComplianceRangeQueryAssembler
{
    /// <summary>
    ///     False when only one end is sent, the ends are out of order or the range is longer than 31 days.
    /// </summary>
    public static bool TryToQuery(int patientId, DateOnly? from, DateOnly? to,
        out GetDailyComplianceRangeQuery? query)
    {
        query = null;
        if (from is null || to is null || !ComplianceSummary.IsValidRange(from.Value, to.Value)) return false;
        query = new GetDailyComplianceRangeQuery(patientId, from.Value, to.Value);
        return true;
    }
}

public static class DailyComplianceRangeResourceAssembler
{
    public static DailyComplianceRangeResource ToResource(DailyComplianceRange range)
    {
        return new DailyComplianceRangeResource(range.From, range.To,
            range.Days.Select(d => new ComplianceDayResource(d.Date, d.Outcome)).ToList(),
            ComplianceSummaryResourceAssembler.ToResource(range.Summary));
    }
}

public static class ComplianceSummaryResourceAssembler
{
    public static ComplianceSummaryResource ToResource(ComplianceSummary summary)
    {
        return new ComplianceSummaryResource(summary.Met, summary.Exceeded, summary.Short, summary.Unlogged,
            summary.Logged, summary.TotalDays);
    }
}

public static class TargetsSnapshotResourceAssembler
{
    public static TargetsSnapshotResource ToResource(TargetsSnapshot snapshot)
    {
        return new TargetsSnapshotResource(snapshot.PlanVersion, snapshot.EnergyKcal,
            snapshot.ProteinG, snapshot.CarbG, snapshot.FatG, snapshot.TakenAt);
    }
}

public static class AnthropometryPointResourceAssembler
{
    public static AnthropometryPointResource ToResource(AnthropometryPoint point)
    {
        return new AnthropometryPointResource(point.Date, point.ValueKg, point.Source);
    }
}

public static class EvaluationWindowResourceAssembler
{
    public static EvaluationWindowResource ToResource(EvaluationWindow window)
    {
        var summary = window.IntakeSummary;

        return new EvaluationWindowResource(
            window.Id.Value,
            window.PatientId,
            window.From,
            window.To,
            window.State.Value,
            window.ClosedAt,
            window.LoggedDaysCount,
            new IntakeSummaryResource(summary.LoggedDays, summary.UnloggedDays, summary.TotalEnergyKcal,
                summary.MeanObservedEnergyKcal, summary.MeanTargetEnergyKcal),
            window.TargetsSnapshot is null
                ? null
                : TargetsSnapshotResourceAssembler.ToResource(window.TargetsSnapshot),
            window.TargetsSnapshots.Select(TargetsSnapshotResourceAssembler.ToResource).ToList(),
            window.DailyComplianceSeries.Select(DailyComplianceResourceAssembler.ToResource).ToList(),
            window.AnthropometrySeries.Select(AnthropometryPointResourceAssembler.ToResource).ToList());
    }
}

public static class DeviationResourceAssembler
{
    public static DeviationResource ToResource(Deviation deviation)
    {
        return new DeviationResource(
            deviation.Id.Value,
            deviation.PatientId,
            deviation.WindowRef.Value,
            deviation.MagnitudeRelativeValue,
            deviation.MagnitudeEnergyKcal,
            deviation.Direction.Value,
            deviation.DetectedAt,
            deviation.IsSustained,
            deviation.SustainedAt,
            deviation.LoggedDaysConsidered,
            deviation.DeviatingDaysConsidered,
            deviation.Evidence());
    }
}

public static class ConsistencyIndexResourceAssembler
{
    public static ConsistencyIndexResource ToResource(ConsistencyIndex index)
    {
        return new ConsistencyIndexResource(index.PatientId, index.Value, index.State.Value,
            index.FirstFlaggedAt, index.ShownToPatientAt, index.EscalatedAt, index.LastRecomputedAt,
            index.IsPatientPromptPending);
    }
}

public static class ReferralResourceAssembler
{
    public static ReferralResource ToResource(Referral referral)
    {
        return new ReferralResource(referral.Id.Value, referral.PatientId, referral.Specialty.Value,
            referral.Reason.Value, referral.IssuedBy, referral.IssuedAt, referral.Status, referral.ClosedAt);
    }
}

public static class ScheduledFollowUpResourceAssembler
{
    public static ScheduledFollowUpResource ToResource(ScheduledFollowUp followUp)
    {
        return ToResource(followUp, null);
    }

    /// <summary>MA-2. One row of the agenda, with the name read from Iam.</summary>
    public static ScheduledFollowUpResource ToResource(FollowUpAgendaEntry entry)
    {
        return ToResource(entry.FollowUp, entry.PatientFullName);
    }

    private static ScheduledFollowUpResource ToResource(ScheduledFollowUp followUp, string? patientFullName)
    {
        return new ScheduledFollowUpResource(followUp.Id.Value, followUp.PatientId,
            followUp.PractitionerId, followUp.ScheduledFor, followUp.State.Value, followUp.MissedAt,
            patientFullName, followUp.Preparation.Select(p => p.Value).ToList(), followUp.Modality.Value,
            followUp.ScheduledAt, followUp.CompletedAt, followUp.CancelledAt);
    }
}

/// <summary>MA-3. A visit as the patient reads it.</summary>
public static class PatientFollowUpResourceAssembler
{
    public static PatientFollowUpResource ToResource(PatientFollowUpEntry entry)
    {
        var followUp = entry.FollowUp;
        return new PatientFollowUpResource(followUp.Id.Value, followUp.ScheduledFor, followUp.Modality.Value,
            followUp.Preparation.Select(p => p.Value).ToList(), followUp.ScheduledAt, entry.PractitionerFullName,
            followUp.State.Value);
    }
}

/// <summary>MA-4. A check in, without the AI generation it came from (that stays for the audit).</summary>
public static class PreVisitCheckInResourceAssembler
{
    public static PreVisitCheckInResource ToResource(PreVisitCheckInView view)
    {
        var checkIn = view.CheckIn;
        return new PreVisitCheckInResource(checkIn.FollowUpId, checkIn.Feeling.Value,
            checkIn.Difficulties.Select(d => d.Value).ToList(),
            checkIn.Questions.Select(q => new CheckInQuestionResource(q.Text, q.Origin, q.Language)).ToList(),
            checkIn.SubmittedAt, checkIn.EditedAt, view.IsLocked);
    }
}

/// <summary>IA-2. A weekly summary, without its AI generation (that stays for the audit).</summary>
public static class WeeklySummaryResourceAssembler
{
    public static WeeklySummaryResource ToResource(WeeklySummary summary)
    {
        var facts = summary.ComplianceFacts;
        return new WeeklySummaryResource(summary.WeekStart, summary.WeekEnd, summary.Headline, summary.WentWell,
            summary.WatchOut,
            new WeeklySummaryFactsResource(facts.MetDays, facts.TotalDays, facts.LoggedDays, facts.UnloggedDays,
                facts.WeightChangeKg),
            summary.Language, summary.GeneratedAt);
    }
}

/// <summary>IA-4. The suggested questions of one generation.</summary>
public static class SuggestedQuestionsResourceAssembler
{
    public static SuggestedQuestionsResource ToResource(SuggestedQuestionsView view)
    {
        return new SuggestedQuestionsResource(
            view.Questions.Select(q => new SuggestedQuestionResource(q.Id, q.Text)).ToList(), view.AiGenerationId,
            view.BasedOnFrom, view.BasedOnTo, view.Language);
    }
}

/// <summary>IA-5. The monitoring summary and its facts.</summary>
public static class MonitoringSummaryResourceAssembler
{
    public static MonitoringSummaryResource ToResource(MonitoringSummaryView view)
    {
        var facts = view.Facts;
        return new MonitoringSummaryResource(view.Text,
            new MonitoringSummaryFactsResource(facts.MetDays, facts.TotalDays, facts.ShortWeekdays,
                facts.DominantMissingSlotOnShortDays, facts.ExceededDays, facts.LoggedDays, facts.UnloggedDays,
                facts.OffPlanEntryCount, facts.WeightSlopeKgPerWeek, facts.WeightChangeKg),
            facts.From, facts.To, view.ConsistencyState, view.AiGenerationId, view.GeneratedAt,
            view.TextUnavailableReason);
    }
}
