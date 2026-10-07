using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal.QueryServices;

public class EvaluationWindowQueryService(IEvaluationWindowRepository repository)
    : IEvaluationWindowQueryService
{
    public async Task<IEnumerable<EvaluationWindow>> Handle(GetEvaluationWindowsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdAsync(query.PatientId, cancellationToken);
    }

    public async Task<EvaluationWindow?> Handle(GetCurrentEvaluationWindowByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindOpenByPatientIdAsync(query.PatientId, cancellationToken);
    }

    /// <summary>
    ///     The day-by-day series, taken from whichever window covers it.
    /// </summary>
    /// <remarks>
    ///     Business rule: Evaluated Data Is Preserved (Subflow 5.11). Closed windows are read too, so
    ///     a patient whose care link ended can still see the days they lived through.
    /// </remarks>
    public async Task<IReadOnlyList<DailyCompliance>> Handle(GetDailyComplianceByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var windows = await repository.ListByPatientIdAsync(query.PatientId, cancellationToken);

        var days = windows
            .SelectMany(w => w.DailyComplianceSeries)
            .Where(d => query.Date is null || d.Date == query.Date)
            .OrderBy(d => d.Date)
            .ToList();

        return days;
    }

    public async Task<DailyComplianceRange> Handle(GetDailyComplianceRangeQuery query,
        CancellationToken cancellationToken = default)
    {
        // The windows of every link the patient had: a range can cross a switch of practitioner (CR-1).
        var evaluated = await Handle(new GetDailyComplianceByPatientIdQuery(query.PatientId, null),
            cancellationToken);

        return new DailyComplianceRange(query.From, query.To,
            ComplianceSummary.DaysOf(evaluated, query.From, query.To),
            ComplianceSummary.Of(evaluated, query.From, query.To));
    }

    public async Task<IEnumerable<EvaluationWindow>> Handle(GetOpenEvaluationWindowsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListOpenAsync(query.BatchSize, cancellationToken);
    }
}

public class DeviationQueryService(IDeviationRepository repository) : IDeviationQueryService
{
    public async Task<IEnumerable<Deviation>> Handle(GetDeviationsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdAsync(query.PatientId, cancellationToken);
    }
}

public class ConsistencyIndexQueryService(IConsistencyIndexRepository repository)
    : IConsistencyIndexQueryService
{
    public async Task<ConsistencyIndex?> Handle(GetConsistencyIndexByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByPatientIdAsync(query.PatientId, cancellationToken);
    }

    public async Task<IEnumerable<ConsistencyIndex>> Handle(GetEscalatableConsistencyIndicesQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListEscalatableAsync(query.AlertSinceBefore, query.BatchSize,
            cancellationToken);
    }
}

public class ReferralQueryService(IReferralRepository repository) : IReferralQueryService
{
    public async Task<IEnumerable<Referral>> Handle(GetReferralsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdAsync(query.PatientId, cancellationToken);
    }
}

public class ScheduledFollowUpQueryService(
    IScheduledFollowUpRepository repository,
    IIamContextFacade iamContextFacade) : IScheduledFollowUpQueryService
{
    public async Task<IEnumerable<ScheduledFollowUp>> Handle(
        GetScheduledFollowUpsByPractitionerIdQuery query, CancellationToken cancellationToken = default)
    {
        return await repository.ListByPractitionerIdAsync(query.PractitionerId, query.State, query.From,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FollowUpAgendaEntry>> Handle(GetPractitionerAgendaQuery query,
        CancellationToken cancellationToken = default)
    {
        var followUps = (await repository.ListByPractitionerIdAsync(query.PractitionerId, query.State,
            query.From, cancellationToken)).ToList();
        if (followUps.Count == 0) return [];

        // One read of Iam for the whole agenda, never one per row. The facade degrades to an empty
        // dictionary, and the agenda is still shown without names.
        var users = await iamContextFacade.GetUsersByIds(followUps.Select(f => f.PatientId).Distinct(),
            cancellationToken);

        return followUps
            .Select(f => new FollowUpAgendaEntry(f, users.TryGetValue(f.PatientId, out var u) ? u.FullName : null))
            .ToList();
    }

    public async Task<IEnumerable<ScheduledFollowUp>> Handle(GetScheduledFollowUpsByPatientIdsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListScheduledByPatientIdsAsync(query.PatientIds, cancellationToken);
    }

    public async Task<ScheduledFollowUp?> Handle(GetScheduledFollowUpByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByIdAsync(query.FollowUpId, cancellationToken);
    }

    public async Task<PatientFollowUpEntry?> Handle(GetNextFollowUpByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var followUp = await repository.FindScheduledByPatientIdAsync(query.PatientId, cancellationToken);
        if (followUp is null) return null;

        return (await WithPractitionerNames([followUp], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<PatientFollowUpEntry>> Handle(GetFollowUpsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var followUps = (await repository.ListByPatientIdAsync(query.PatientId, query.State,
            cancellationToken)).ToList();
        if (followUps.Count == 0) return [];

        return await WithPractitionerNames(followUps, cancellationToken);
    }

    public async Task<IEnumerable<ScheduledFollowUp>> Handle(GetOverdueScheduledFollowUpsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListOverdueAsync(query.AsOf, query.BatchSize, cancellationToken);
    }

    /// <summary>
    ///     MA-3. One read of Iam for every visit, never one per row. The facade degrades to an empty dictionary,
    ///     and the visits are still shown without the name.
    /// </summary>
    private async Task<IReadOnlyList<PatientFollowUpEntry>> WithPractitionerNames(
        IReadOnlyList<ScheduledFollowUp> followUps, CancellationToken cancellationToken)
    {
        var users = await iamContextFacade.GetUsersByIds(followUps.Select(f => f.PractitionerId).Distinct(),
            cancellationToken);

        return followUps
            .Select(f => new PatientFollowUpEntry(f,
                users.TryGetValue(f.PractitionerId, out var u) ? u.FullName : null))
            .ToList();
    }
}

/// <summary>MA-4. The check ins, with whether each can still be edited.</summary>
public class PreVisitCheckInQueryService(
    IPreVisitCheckInRepository checkInRepository,
    IScheduledFollowUpRepository scheduledFollowUpRepository,
    TimeProvider timeProvider) : IPreVisitCheckInQueryService
{
    public async Task<PreVisitCheckInView?> Handle(GetPreVisitCheckInByFollowUpIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var checkIn = await checkInRepository.FindByFollowUpIdAsync(query.FollowUpId, cancellationToken);
        if (checkIn is null) return null;

        var followUp = await scheduledFollowUpRepository.FindByIdAsync(checkIn.FollowUpId, cancellationToken);
        return new PreVisitCheckInView(checkIn,
            followUp is null || PreVisitCheckIn.IsLockedFor(followUp, timeProvider.GetUtcNow()));
    }

    public async Task<PreVisitCheckInView?> Handle(GetLatestPreVisitCheckInQuery query,
        CancellationToken cancellationToken = default)
    {
        // Only a visit still open: the check in of a visit already completed belongs to a previous cycle, and
        // EV-2 would show it as if the patient had just said it.
        var openVisits = (await scheduledFollowUpRepository.ListOpenByPatientAndPractitionerAsync(query.PatientId,
            query.PractitionerId, cancellationToken)).ToList();
        if (openVisits.Count == 0) return null;

        var checkIns = (await checkInRepository.ListByFollowUpIdsAsync(
                openVisits.Select(f => f.Id.Value).ToList(), cancellationToken))
            .ToDictionary(c => c.FollowUpId);
        var now = timeProvider.GetUtcNow();

        // Most recent visit first, so the answer is the one of the visit closest to now.
        return openVisits
            .Where(f => checkIns.ContainsKey(f.Id.Value))
            .Select(f => new PreVisitCheckInView(checkIns[f.Id.Value], PreVisitCheckIn.IsLockedFor(f, now)))
            .FirstOrDefault();
    }
}

/// <summary>IA-2. The weekly summaries.</summary>
public class WeeklySummaryQueryService(IWeeklySummaryRepository repository) : IWeeklySummaryQueryService
{
    public async Task<WeeklySummary?> Handle(GetLatestWeeklySummaryByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindLatestByPatientIdAsync(query.PatientId, cancellationToken);
    }
}
