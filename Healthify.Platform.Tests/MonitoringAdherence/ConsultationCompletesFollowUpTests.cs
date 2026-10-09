using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-2 acceptance. Publishing a consultation (NC-2, step 4) completes the visit it was held for, through the
///     integration event Consultation Completed and the Monitoring policy that consumes it; after that the
///     missed-visit worker leaves the visit alone. "Una consulta no acudida no cierra el vínculo" still holds for
///     the visits nobody attended.
/// </summary>
public class ConsultationCompletesFollowUpTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    private readonly InMemoryNutritionalCare _care = new(Today);

    public ConsultationCompletesFollowUpTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    [Fact]
    public async Task Publishing_the_consultation_completes_its_visit_and_the_worker_no_longer_flags_it_missed()
    {
        // The day of the agenda is wide enough to hold "now": the real local day is covered below with fixed dates.
        var agenda = new InMemoryScheduledFollowUps(WholeDayAroundNow());
        Fakes.Route(_care.Mediator, agenda.Handler());
        var visit = agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddMinutes(-30), "Fasting");
        var nobodyCame = agenda.Add(PatientId + 1, PractitionerId, DateTimeOffset.UtcNow.AddMinutes(-30));

        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 74.2m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, consultationId, PractitionerId,
            DiagnosisCode.OverweightGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);
        ConsultationFlow.Ok(await ConsultationFlow.PublishAsync(_care.Consultation, consultationId, PractitionerId,
            "publication"), "publication");

        Assert.Single(Fakes.Published(_care.Mediator).OfType<ConsultationCompleted>());
        Assert.Equal(FollowUpState.Completed, visit.State.Value);
        Assert.Equal(consultationId, visit.CompletedByConsultationId);
        Assert.NotNull(visit.CompletedAt);

        await agenda.RunMissedFollowUpCycleAsync();

        Assert.Equal(FollowUpState.Completed, visit.State.Value);
        Assert.Null(visit.MissedAt);
        // The worker did run: the visit nobody attended is flagged, and only that one.
        Assert.Equal(FollowUpState.Missed, nobodyCame.State.Value);
        var missed = Assert.Single(Fakes.Published(agenda.Mediator).OfType<FollowUpMissed>());
        Assert.Equal(nobodyCame.Id.Value, missed.FollowUpId);
    }

    [Fact]
    public async Task A_consultation_started_from_a_visit_completes_that_visit_whatever_its_day()
    {
        var agenda = new InMemoryScheduledFollowUps(new ClinicalTimeZoneFollowUpCalendar(Lima));
        Fakes.Route(_care.Mediator, agenda.Handler());
        var visit = agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(-3));

        var consultationId = ConsultationFlow.Ok(await _care.Consultation.Handle(
            new StartConsultationCommand(PatientId, PractitionerId, visit.Id.Value)), "start").Id.Value;
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 74.2m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, consultationId, PractitionerId,
            DiagnosisCode.OverweightGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);
        ConsultationFlow.Ok(await ConsultationFlow.PublishAsync(_care.Consultation, consultationId, PractitionerId,
            "publication"), "publication");

        Assert.Equal(FollowUpState.Completed, visit.State.Value);
        Assert.Equal(consultationId, visit.CompletedByConsultationId);
    }

    [Fact]
    public async Task Without_a_visit_id_the_visit_of_the_same_local_day_with_the_same_practitioner_is_completed()
    {
        var agenda = new InMemoryScheduledFollowUps(new ClinicalTimeZoneFollowUpCalendar(Lima));
        // 17 Sep 18:00 in Lima is 23:00 UTC; the consultation is published at 19:30 Lima, already 18 Sep in UTC.
        var visit = agenda.Add(PatientId, PractitionerId, new DateTimeOffset(2026, 9, 17, 23, 0, 0, TimeSpan.Zero));
        var otherPractitioner = agenda.Add(PatientId, PractitionerId + 1,
            new DateTimeOffset(2026, 9, 17, 22, 0, 0, TimeSpan.Zero));
        var nextDay = agenda.Add(PatientId, PractitionerId, new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero));

        var result = await agenda.Commands.Handle(new CompleteFollowUpFromConsultationCommand(PatientId,
            PractitionerId, 77, new DateTimeOffset(2026, 9, 18, 0, 30, 0, TimeSpan.Zero), null));

        Assert.Same(visit, Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Success>(result).Value);
        Assert.Equal(FollowUpState.Completed, visit.State.Value);
        Assert.True(otherPractitioner.IsScheduled);
        Assert.True(nextDay.IsScheduled);
        await agenda.UnitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_visit_id_of_another_patient_is_not_completed()
    {
        var agenda = new InMemoryScheduledFollowUps(new ClinicalTimeZoneFollowUpCalendar(Lima));
        var someoneElse = agenda.Add(PatientId + 5, PractitionerId, DateTimeOffset.UtcNow.AddHours(-1));

        var result = await agenda.Commands.Handle(new CompleteFollowUpFromConsultationCommand(PatientId,
            PractitionerId, 77, DateTimeOffset.UtcNow, someoneElse.Id.Value));

        Assert.Equal(MonitoringError.ScheduledFollowUpNotFound,
            Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Failure>(result).Error);
        Assert.True(someoneElse.IsScheduled);
        await agenda.UnitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
    }

    [Fact]
    public async Task A_cancelled_visit_stays_cancelled()
    {
        var agenda = new InMemoryScheduledFollowUps(new ClinicalTimeZoneFollowUpCalendar(Lima));
        var visit = agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(2));
        visit.Cancel("Discharged");

        var result = await agenda.Commands.Handle(new CompleteFollowUpFromConsultationCommand(PatientId,
            PractitionerId, 77, DateTimeOffset.UtcNow, visit.Id.Value));

        Assert.Equal(MonitoringError.FollowUpNotScheduled,
            Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Failure>(result).Error);
        Assert.Equal(FollowUpState.Cancelled, visit.State.Value);
    }

    [Fact]
    public async Task The_policy_never_throws_into_the_publication()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Throws(new InvalidOperationException("container down"));
        var handler = new OnConsultationCompletedHandler(scopeFactory,
            NullLogger<OnConsultationCompletedHandler>.Instance);

        await handler.Handle(new ConsultationCompleted(77, PatientId, PractitionerId, 2, DateTimeOffset.UtcNow, null),
            CancellationToken.None);
    }

    [Fact]
    public void The_local_day_of_Lima_spans_midnight_to_midnight_there()
    {
        var calendar = new ClinicalTimeZoneFollowUpCalendar(Lima);

        var (start, end) = calendar.LocalDayOf(new DateTimeOffset(2026, 9, 18, 0, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 17, 5, 0, 0, TimeSpan.Zero), start.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 5, 0, 0, TimeSpan.Zero), end.ToUniversalTime());
    }

    private static IFollowUpCalendar WholeDayAroundNow()
    {
        var calendar = Substitute.For<IFollowUpCalendar>();
        calendar.LocalDayOf(Arg.Any<DateTimeOffset>())
            .Returns(call => (call.Arg<DateTimeOffset>().AddHours(-12), call.Arg<DateTimeOffset>().AddHours(12)));
        return calendar;
    }
}
