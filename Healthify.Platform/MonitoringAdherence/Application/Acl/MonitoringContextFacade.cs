using System.Globalization;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;

namespace Healthify.Platform.MonitoringAdherence.Application.Acl;

/// <inheritdoc cref="IMonitoringContextFacade" />
/// <remarks>
///     Every method degrades gracefully. A composite read model that asks this context a question it
///     cannot answer gets an empty section rather than a failed page, because the panel is assembled
///     from six contexts and one of them being quiet is not a reason for the other five to disappear.
/// </remarks>
public class MonitoringContextFacade(
    IEvaluationWindowQueryService evaluationWindowQueryService,
    IConsistencyIndexQueryService consistencyIndexQueryService,
    IReferralQueryService referralQueryService,
    IScheduledFollowUpQueryService scheduledFollowUpQueryService,
    IPreVisitCheckInQueryService preVisitCheckInQueryService) : IMonitoringContextFacade
{
    public async Task<IReadOnlyList<DailyComplianceItem>> GetDailyComplianceSeries(int patientId,
        int days, CancellationToken ct = default)
    {
        try
        {
            var series = await evaluationWindowQueryService.Handle(
                new GetDailyComplianceByPatientIdQuery(patientId, null), ct);

            var take = days <= 0 ? series.Count : Math.Min(days, series.Count);

            return series
                .Skip(series.Count - take)
                .Select(d => new DailyComplianceItem(d.Date, d.Outcome))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<ConsistencyStateItem?> GetConsistencyState(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var index = await consistencyIndexQueryService.Handle(
                new GetConsistencyIndexByPatientIdQuery(patientId), ct);

            if (index is null) return null;

            return new ConsistencyStateItem(index.State.Value, index.ShownToPatientAt,
                index.EscalatedAt);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     TODO: discrepancy between the prompt and this contract - the declared return type carries
    ///     a date and a string and has nowhere to put a weight, so the reading is formatted with the
    ///     invariant culture into the string field. Callers that need the number should use
    ///     <see cref="GetAnthropometrySeriesPoints" />.
    /// </remarks>
    public async Task<IReadOnlyList<DailyComplianceItem>> GetAnthropometrySeries(int patientId,
        CancellationToken ct = default)
    {
        var points = await GetAnthropometrySeriesPoints(patientId, ct);

        return points
            .Select(p => new DailyComplianceItem(p.Date,
                p.ValueKg.ToString("0.##", CultureInfo.InvariantCulture)))
            .ToList();
    }

    public async Task<IReadOnlyList<AnthropometryPointItem>> GetAnthropometrySeriesPoints(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var windows = await evaluationWindowQueryService.Handle(
                new GetEvaluationWindowsByPatientIdQuery(patientId), ct);

            // Clinical measurements only, in date order. Nothing merges the readings the patient
            // takes at home into this list, here or anywhere else.
            return windows
                .SelectMany(w => w.AnthropometrySeries)
                .OrderBy(p => p.Date)
                .Select(p => new AnthropometryPointItem(p.Date, p.ValueKg, p.Source))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ReferralItem>> GetReferrals(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var referrals = await referralQueryService.Handle(
                new GetReferralsByPatientIdQuery(patientId), ct);

            return referrals
                .Select(r => new ReferralItem(r.Id.Value, r.Specialty.Value, r.Reason.Value,
                    r.IssuedBy, r.IssuedAt, r.Status, r.ClosedAt))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyDictionary<int, NextFollowUpItem>> GetNextFollowUpsByPatientIds(
        IEnumerable<int> patientIds, CancellationToken ct = default)
    {
        try
        {
            var followUps = await scheduledFollowUpQueryService.Handle(
                new GetScheduledFollowUpsByPatientIdsQuery(patientIds.ToList()), ct);

            // One visit in Scheduled per patient by rule; the soonest wins if the race window ever left two.
            return followUps
                .GroupBy(f => f.PatientId)
                .ToDictionary(g => g.Key, g =>
                {
                    var f = g.OrderBy(x => x.ScheduledFor).First();
                    return new NextFollowUpItem(f.Id.Value, f.PatientId, f.ScheduledFor, f.Modality.Value,
                        f.Preparation.Select(p => p.Value).ToList(), f.ScheduledAt, f.PractitionerId);
                });
        }
        catch
        {
            return new Dictionary<int, NextFollowUpItem>();
        }
    }

    public async Task<ComplianceSummaryItem?> GetComplianceSummary(int patientId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        if (to < from) return null;

        try
        {
            var days = await evaluationWindowQueryService.Handle(
                new GetDailyComplianceByPatientIdQuery(patientId, null), ct);
            return ToItem(ComplianceSummary.Of(days, from, to));
        }
        catch
        {
            return null;
        }
    }

    public async Task<ComplianceRangeItem?> GetComplianceRange(int patientId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        if (!ComplianceSummary.IsValidRange(from, to)) return null;

        try
        {
            var range = await evaluationWindowQueryService.Handle(
                new GetDailyComplianceRangeQuery(patientId, from, to), ct);
            return new ComplianceRangeItem(
                range.Days.Select(d => new DailyComplianceItem(d.Date, d.Outcome)).ToList(),
                ToItem(range.Summary));
        }
        catch
        {
            return null;
        }
    }

    private static ComplianceSummaryItem ToItem(ComplianceSummary summary)
    {
        return new ComplianceSummaryItem(summary.Met, summary.Exceeded, summary.Short, summary.Unlogged,
            summary.Logged, summary.TotalDays);
    }

    public async Task<PreVisitCheckInItem?> GetLatestCheckInForPatient(int patientId, int practitionerId,
        CancellationToken ct = default)
    {
        try
        {
            var view = await preVisitCheckInQueryService.Handle(
                new GetLatestPreVisitCheckInQuery(patientId, practitionerId), ct);
            if (view is null) return null;

            var checkIn = view.CheckIn;
            return new PreVisitCheckInItem(checkIn.FollowUpId, checkIn.Feeling.Value,
                checkIn.Difficulties.Select(d => d.Value).ToList(),
                checkIn.Questions.Select(q => new CheckInQuestionItem(q.Text, q.Origin, q.Language)).ToList(),
                checkIn.SubmittedAt, checkIn.EditedAt, view.IsLocked);
        }
        catch
        {
            return null;
        }
    }
}
