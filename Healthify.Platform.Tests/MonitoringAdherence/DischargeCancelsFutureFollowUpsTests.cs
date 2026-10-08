using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     CR-4 acceptance: the discharge (PR18) closes the evaluation window of the link and cancels the visits still
///     ahead with that practitioner, so they leave the agenda (PR17.0-Alta). The same happens to the visits with
///     the previous practitioner when the patient switches (CR-1). Past visits and other agendas are untouched.
/// </summary>
public class DischargeCancelsFutureFollowUpsTests
{
    private const int PatientId = 7;
    private const int PractitionerId = 20;
    private const int OtherPractitionerId = 30;
    private const int CareLinkId = 4;

    private readonly InMemoryScheduledFollowUps _agenda =
        new(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));

    private readonly IEvaluationWindowCommandService _windows = Substitute.For<IEvaluationWindowCommandService>();

    public DischargeCancelsFutureFollowUpsTests()
    {
        _windows.Handle(Arg.Any<CloseEvaluationWindowCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<EvaluationWindow, MonitoringError>.Failure(MonitoringError.EvaluationWindowNotFound));
    }

    [Fact]
    public async Task The_discharge_cancels_the_future_visits_and_closes_the_window_of_the_link()
    {
        var tomorrow = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(1));
        var nextMonth = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(30));
        var overdue = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddHours(-2));
        var otherAgenda = _agenda.Add(PatientId, OtherPractitionerId, DateTimeOffset.UtcNow.AddDays(2));
        var otherPatient = _agenda.Add(PatientId + 1, PractitionerId, DateTimeOffset.UtcNow.AddDays(2));
        var dischargedAt = DateTimeOffset.UtcNow;

        await DischargeHandler().Handle(
            new TreatmentDischarged(CareLinkId, PatientId, PractitionerId, "Objetivos alcanzados", dischargedAt),
            CancellationToken.None);

        foreach (var cancelled in new[] { tomorrow, nextMonth })
        {
            Assert.True(cancelled.State.IsCancelled);
            Assert.Equal(ScheduledFollowUp.DischargedReason, cancelled.CancellationReason);
            Assert.NotNull(cancelled.CancelledAt);
        }

        // History, another agenda and another patient are not touched.
        Assert.True(overdue.IsScheduled);
        Assert.True(otherAgenda.IsScheduled);
        Assert.True(otherPatient.IsScheduled);

        await _windows.Received(1).Handle(
            Arg.Is<CloseEvaluationWindowCommand>(c =>
                c.PatientId == PatientId && c.RevokedAt == dischargedAt && c.CareLinkId == CareLinkId),
            Arg.Any<CancellationToken>());
        var published = Fakes.Published(_agenda.Mediator).OfType<FollowUpCancelled>().ToList();
        Assert.Equal([tomorrow.Id.Value, nextMonth.Id.Value], published.Select(e => e.FollowUpId).Order());
        Assert.All(published, e => Assert.Equal("Discharged", e.Reason));
    }

    [Fact]
    public async Task After_the_discharge_the_visit_leaves_the_agenda_and_the_patient_has_no_next_visit()
    {
        _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(3));

        await DischargeHandler().Handle(
            new TreatmentDischarged(CareLinkId, PatientId, PractitionerId, "Alta", DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Null(await _agenda.Queries.Handle(new GetNextFollowUpByPatientIdQuery(PatientId)));
        var scheduled = await _agenda.Queries.Handle(new GetScheduledFollowUpsByPractitionerIdQuery(PractitionerId,
            new FollowUpState(FollowUpState.Scheduled)));
        Assert.Empty(scheduled);
    }

    [Fact]
    public async Task A_failing_window_does_not_stop_the_cancellation_and_nothing_throws()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(1));
        _windows.Handle(Arg.Any<CloseEvaluationWindowCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database down"));

        await DischargeHandler().Handle(
            new TreatmentDischarged(CareLinkId, PatientId, PractitionerId, "Alta", DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.True(visit.State.IsCancelled);
    }

    [Fact]
    public async Task Switching_practitioner_cancels_the_future_visits_with_the_previous_one()
    {
        var withPrevious = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(1));
        var withNew = _agenda.Add(PatientId, OtherPractitionerId, DateTimeOffset.UtcNow.AddDays(5));

        await RevokedHandler().Handle(
            new CareLinkRevoked(CareLinkId, PatientId, PractitionerId, DateTimeOffset.UtcNow, "SwitchedPractitioner"),
            CancellationToken.None);

        Assert.True(withPrevious.State.IsCancelled);
        Assert.Equal(ScheduledFollowUp.SwitchedPractitionerReason, withPrevious.CancellationReason);
        Assert.True(withNew.IsScheduled);
        await _windows.Received(1).Handle(Arg.Any<CloseEvaluationWindowCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_withdrawn_consent_closes_the_window_but_does_not_cancel_visits()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, DateTimeOffset.UtcNow.AddDays(1));

        await RevokedHandler().Handle(
            new CareLinkRevoked(CareLinkId, PatientId, PractitionerId, DateTimeOffset.UtcNow, "ConsentWithdrawn"),
            CancellationToken.None);

        Assert.True(visit.IsScheduled);
        await _windows.Received(1).Handle(Arg.Any<CloseEvaluationWindowCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_future_visits_the_command_succeeds_and_saves_nothing()
    {
        var result = await _agenda.Commands.Handle(
            new CancelFollowUpsForPatientCommand(PatientId, ScheduledFollowUp.DischargedReason, PractitionerId));

        Assert.Empty(Assert.IsType<Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>.Success>(result).Value);
        await _agenda.UnitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync();
    }

    private OnTreatmentDischargedHandler DischargeHandler()
    {
        return new OnTreatmentDischargedHandler(
            Fakes.ScopeFactoryWith((typeof(IEvaluationWindowCommandService), _windows),
                (typeof(IScheduledFollowUpCommandService), _agenda.Commands)),
            NullLogger<OnTreatmentDischargedHandler>.Instance);
    }

    private OnCareLinkRevokedHandler RevokedHandler()
    {
        return new OnCareLinkRevokedHandler(
            Fakes.ScopeFactoryWith((typeof(IEvaluationWindowCommandService), _windows),
                (typeof(IScheduledFollowUpCommandService), _agenda.Commands)),
            NullLogger<OnCareLinkRevokedHandler>.Instance);
    }
}
