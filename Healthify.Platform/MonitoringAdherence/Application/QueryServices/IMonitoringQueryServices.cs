using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Application.QueryServices;

/// <summary>Read models Patient Monitoring Panel and Daily Compliance Indicator.</summary>
public interface IEvaluationWindowQueryService
{
    Task<IEnumerable<EvaluationWindow>> Handle(GetEvaluationWindowsByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    Task<EvaluationWindow?> Handle(GetCurrentEvaluationWindowByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     The day-by-day series the patient sees. An unlogged day is part of the answer, not a hole
    ///     in it.
    /// </summary>
    /// <summary>MA-6. Every calendar day of the range and its summary; never null.</summary>
    Task<DailyComplianceRange> Handle(GetDailyComplianceRangeQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyCompliance>> Handle(GetDailyComplianceByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Input to the time-driven policy of Subflow 5.9.</summary>
    Task<IEnumerable<EvaluationWindow>> Handle(GetOpenEvaluationWindowsQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read model Patient Monitoring Panel, deviations section.</summary>
public interface IDeviationQueryService
{
    Task<IEnumerable<Deviation>> Handle(GetDeviationsByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read model Consistency Card.</summary>
public interface IConsistencyIndexQueryService
{
    Task<ConsistencyIndex?> Handle(GetConsistencyIndexByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Input to the time-driven policy of Subflow 5.8.</summary>
    Task<IEnumerable<ConsistencyIndex>> Handle(GetEscalatableConsistencyIndicesQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read model Patient Record, referrals section.</summary>
public interface IReferralQueryService
{
    Task<IEnumerable<Referral>> Handle(GetReferralsByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Read model Practitioner Agenda.</summary>
public interface IScheduledFollowUpQueryService
{
    Task<IEnumerable<ScheduledFollowUp>> Handle(GetScheduledFollowUpsByPractitionerIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>MA-2. The agenda with the name of each patient, from one batch read of Iam.</summary>
    Task<IReadOnlyList<FollowUpAgendaEntry>> Handle(GetPractitionerAgendaQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>MA-2/RM-1/RM-2. The scheduled visit of each patient; empty, never null.</summary>
    Task<IEnumerable<ScheduledFollowUp>> Handle(GetScheduledFollowUpsByPatientIdsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>MA-4. One visit, or null.</summary>
    Task<ScheduledFollowUp?> Handle(GetScheduledFollowUpByIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>MA-3. The visit on the calendar of a patient with the name of the practitioner, or null.</summary>
    Task<PatientFollowUpEntry?> Handle(GetNextFollowUpByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>MA-3. The visits of a patient with the name of the practitioner; empty, never null.</summary>
    Task<IReadOnlyList<PatientFollowUpEntry>> Handle(GetFollowUpsByPatientIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Input to the time-driven policy of Subflow 5.10.</summary>
    Task<IEnumerable<ScheduledFollowUp>> Handle(GetOverdueScheduledFollowUpsQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>MA-4. Read models PT25.3 "Le contaste a tu nutricionista cómo te fue" and EV-2.</summary>
public interface IPreVisitCheckInQueryService
{
    /// <summary>The check in of one visit, or null when the patient has not answered.</summary>
    Task<PreVisitCheckInView?> Handle(GetPreVisitCheckInByFollowUpIdQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>The check in of the open visit of a patient with a practitioner, or null.</summary>
    Task<PreVisitCheckInView?> Handle(GetLatestPreVisitCheckInQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>IA-2. Read model PT13 / PT13.2 "Tu semana".</summary>
public interface IWeeklySummaryQueryService
{
    /// <summary>The summary of the most recent week, or null (PT13.2.V "aún sin resumen").</summary>
    Task<WeeklySummary?> Handle(GetLatestWeeklySummaryByPatientIdQuery query,
        CancellationToken cancellationToken = default);
}
