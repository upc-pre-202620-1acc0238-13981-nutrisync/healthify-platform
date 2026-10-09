using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST.Resources;

namespace Healthify.Platform.ReadModels.Interfaces.REST.Transform;

/// <summary>
///     Turns what the composers assembled into what the two endpoints return.
/// </summary>
/// <remarks>
///     The mapping is one to one and it stays that way on purpose. A composite view that starts
///     computing on the way out is a bounded context growing an opinion, and this one has none: it
///     shows what five contexts publish, in the shape they publish it.
/// </remarks>
public static class PatientRecordResourceAssembler
{
    public static PatientRecordResource ToResource(PatientRecordComposition composition)
    {
        return new PatientRecordResource(
            composition.PatientId,
            ToResource(composition.Identity),
            ToResource(composition.Care),
            new RecordAssessmentSectionResource(
                composition.Assessment.ClinicalAnthropometrySeries.Select(ToResource).ToList()),
            ToResource(composition.Intervention),
            new RecordFollowUpSectionResource(
                composition.FollowUp.DailyCompliance.Select(ToResource).ToList(),
                composition.FollowUp.SelfWeighInTrend.Select(ToResource).ToList(),
                ToResource(composition.FollowUp.Consistency),
                composition.FollowUp.Referrals.Select(ToResource).ToList()));
    }

    internal static RecordIdentityResource? ToResource(UserIdentityItem? item)
    {
        return item is null
            ? null
            : new RecordIdentityResource(item.UserId, item.Email, item.Role, item.GivenNames, item.FamilyNames,
                item.FullName);
    }

    internal static RecordCareResource? ToResource(CareLinkStatusItem? item)
    {
        return item is null
            ? null
            : new RecordCareResource(item.CareLinkId, item.PractitionerId, item.IsActive,
                item.HasConsent, item.LastAcknowledgedVersion);
    }

    internal static RecordActiveTargetsResource? ToResource(ActiveTargetsItem? item)
    {
        return item is null
            ? null
            : new RecordActiveTargetsResource(item.PlanVersion, item.ValidFrom, item.EnergyKcal,
                item.ProteinG, item.CarbG, item.FatG, item.Guidelines, item.Restrictions,
                item.GuidelineItems?.Select(g => new RecordGuidelineResource(g.Code, g.Custom)).ToList(),
                item.LegacyRestrictions);
    }

    internal static RecordAnthropometryPointResource ToResource(AnthropometryPointItem item)
    {
        return new RecordAnthropometryPointResource(item.Date, item.ValueKg, item.Source);
    }

    internal static RecordWeightTrendPointResource ToResource(WeightTrendPointItem item)
    {
        return new RecordWeightTrendPointResource(item.Date, item.SmoothedValueKg);
    }

    internal static RecordDailyComplianceResource ToResource(DailyComplianceItem item)
    {
        return new RecordDailyComplianceResource(item.Date, item.Outcome);
    }

    internal static RecordConsistencyResource? ToResource(ConsistencyStateItem? item)
    {
        return item is null
            ? null
            : new RecordConsistencyResource(item.State, item.ShownToPatientAt, item.EscalatedAt);
    }

    internal static RecordReferralResource ToResource(ReferralItem item)
    {
        return new RecordReferralResource(item.ReferralId, item.Specialty, item.Reason, item.IssuedBy,
            item.IssuedAt, item.Status, item.ClosedAt);
    }
}

/// <summary>
///     Turns the composed panel into what the endpoint returns.
/// </summary>
/// <remarks>
///     Every field of every diary entry is carried across, confidence and provenance included. There
///     is no shape of this mapping that drops them, which is the point: the rule that they are always
///     exposed is only a rule if no layer between the diary and the reader is allowed to quietly
///     tidy them away.
/// </remarks>
public static class PatientMonitoringPanelResourceAssembler
{
    public static PatientMonitoringPanelResource ToResource(PatientMonitoringPanelComposition composition)
    {
        return new PatientMonitoringPanelResource(
            composition.PatientId,
            composition.Date,
            PatientRecordResourceAssembler.ToResource(composition.ActiveTargets),
            ToResource(composition.DailyIntakeSummary),
            composition.Diary.Select(ToResource).ToList(),
            composition.DailyCompliance.Select(PatientRecordResourceAssembler.ToResource).ToList(),
            composition.ClinicalAnthropometrySeries
                .Select(PatientRecordResourceAssembler.ToResource).ToList(),
            composition.SelfWeighInTrend.Select(PatientRecordResourceAssembler.ToResource).ToList(),
            // RM-3: the consistency index left the panel (Patient First Always); the field stays, always null.
            null,
            composition.Week is null
                ? null
                : new PanelWeekResource(composition.WeekFrom, composition.WeekTo,
                    composition.Week.Days.Select(d => new PanelWeekDayResource(d.Date, d.Outcome)).ToList(),
                    composition.Week.Summary.Met, composition.Week.Summary.Exceeded, composition.Week.Summary.Short,
                    composition.Week.Summary.Unlogged, composition.Week.Summary.Logged,
                    composition.Week.Summary.TotalDays),
            composition.LoggedDays is null
                ? null
                : new PanelLoggedDaysResource(composition.LoggedDaysFrom, composition.Date,
                    composition.LoggedDays.Logged, composition.LoggedDays.TotalDays),
            composition.WeightTrendSummary is null
                ? null
                : new PanelWeightTrendSummaryResource(PatientMonitoringPanelComposer.WeightTrendWeeks,
                    composition.WeightTrendSummary.SlopeKgPerWeek, composition.WeightTrendSummary.ChangeKg,
                    composition.WeightTrendSummary.PointCount),
            composition.LastClinicalMeasurement is null
                ? null
                : new PanelClinicalMeasurementResource(composition.LastClinicalMeasurement.TakenAt,
                    composition.LastClinicalMeasurement.WeightKg, composition.LastClinicalMeasurement.ProtocolChecks,
                    composition.LastClinicalMeasurement.Protocol));
    }

    private static PanelDailyIntakeSummaryResource? ToResource(DailyIntakeSummaryItem? item)
    {
        return item is null
            ? null
            : new PanelDailyIntakeSummaryResource(item.Date, item.EnergyKcal, item.ProteinG,
                item.CarbG, item.FatG, item.EntryCount, item.OffPlanEntryCount, item.HasAnyEntry);
    }

    private static PanelDiaryEntryResource ToResource(DiaryEntryItem item)
    {
        return new PanelDiaryEntryResource(
            item.DiaryEntryId,
            item.LocalTimestamp,
            item.Provenance,
            item.SyncState,
            item.ProposedReferenceFoodId,
            item.ProposedFoodName,
            item.ProposedPortionGrams,
            item.ProposedConfidence,
            item.ConfirmedReferenceFoodId,
            item.ConfirmedFoodName,
            item.ConfirmedPortionGrams,
            item.ConfirmedAt,
            item.FoodName,
            item.PlanAdherence,
            item.IsCountedTowardsTargets);
    }
}

/// <summary>RM-2. Turns the composed patient summary into what the endpoint returns.</summary>
public static class PatientSummaryResourceAssembler
{
    public static PatientSummaryResource ToResource(PatientSummaryComposition composition)
    {
        var since = composition.SinceLastConsultation;

        return new PatientSummaryResource(
            composition.PatientId,
            composition.Identity?.FullName,
            composition.Care?.EstablishedAt,
            composition.Care is { IsActive: false } ? "PendingConsent" : "Active",
            composition.ActivePlanVersion,
            composition.Baseline is { } b
                ? new PatientBaselineSummaryResource(b.BiologicalSex, b.AgeYears, b.HeightCm, b.Conditions)
                : null,
            composition.NextFollowUp is { } f
                ? new NextFollowUpResource(f.FollowUpId, f.ScheduledFor, f.Modality, f.Preparation, f.ScheduledAt)
                : null,
            composition.ConsultationInProgress is { } c
                ? new ConsultationInProgressResource(c.ConsultationId, c.StepNumber, c.StepName, c.LastSavedAt)
                : null,
            new SinceLastConsultationResource(
                since.FromDate,
                since.WeightTrend?.SlopeKgPerWeek,
                since.TrendWeeks,
                since.Compliance is { } compliance
                    ? new ComplianceRatioResource(compliance.Met, compliance.TotalDays)
                    : null));
    }
}

/// <summary>RM-1. Turns one composed roster entry into what the endpoint returns.</summary>
public static class PatientRosterItemResourceAssembler
{
    public static PatientRosterItemResource ToResource(PatientRosterEntry entry)
    {
        return new PatientRosterItemResource(
            entry.Link.PatientId,
            entry.Link.CareLinkId,
            entry.Identity?.FullName ?? string.Empty,
            entry.Identity?.Initial ?? '?',
            entry.Link.LinkedSince,
            entry.Link.LinkStatus,
            entry.CareStatus?.HasBaseline ?? false,
            entry.CareStatus?.ActivePlanVersion,
            entry.IsNew,
            entry.NextFollowUp?.ScheduledFor,
            entry.CareStatus?.HasConsultationInProgress ?? false,
            entry.CareStatus?.HasOpenReviewItem ?? false);
    }
}

/// <summary>RM-5. PT25 "Mis consultas". The label is a code; the app writes it in es/en.</summary>
public static class PatientConsultationsOverviewResourceAssembler
{
    public static PatientConsultationsOverviewResource ToResource(PatientConsultationsComposition composition)
    {
        var next = composition.Next is { } n
            ? new UpcomingConsultationResource(n.FollowUpId, n.ScheduledFor, n.Modality, n.Preparation,
                n.ScheduledAt, composition.NextPractitionerFullName)
            : null;
        var checkIn = composition.CheckIn is { } c
            ? new ConsultationCheckInSummaryResource(c.Feeling, c.Difficulties,
                c.Questions.Select(q => q.Text).ToList(), c.SubmittedAt, c.EditedAt, c.IsLocked)
            : null;
        var past = composition.Past
            .Select(p => new PastConsultationResource(p.ConsultationId, p.CompletedAt,
                p.IsFirstConsultation
                    ? PastConsultationResource.FirstConsultation
                    : PastConsultationResource.AssessmentAndNewPlan,
                p.PlanVersion))
            .ToList();

        return new PatientConsultationsOverviewResource(composition.PatientId, next, checkIn, past);
    }
}
