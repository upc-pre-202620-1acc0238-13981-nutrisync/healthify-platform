using Healthify.Platform.MonitoringAdherence.Application.Acl;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-4. Monitoring publishes the check in of the visit still open between a patient and a practitioner, and
///     NC-2 shows it in EV-2 ("Antes de la consulta, … contó") while the consultation is in progress.
/// </summary>
public class LatestCheckInFacadeTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private readonly InMemoryScheduledFollowUps _agenda = new(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));

    private async Task Answer(int followUpId, string feeling)
    {
        await _agenda.CheckInCommands.Handle(new SubmitPreVisitCheckInCommand(followUpId, PatientId, feeling,
            ["Dinners"], [new PreVisitCheckInQuestionInput("¿Cómo armo cenas con más proteína?", "AiSuggested", 3)]));
    }

    [Fact]
    public async Task The_check_in_of_the_open_visit_crosses_as_primitives()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));
        await Answer(visit.Id.Value, "Fair");

        var item = await _agenda.Facade().GetLatestCheckInForPatient(PatientId, PractitionerId);

        Assert.NotNull(item);
        Assert.Equal((visit.Id.Value, "Fair", false), (item.FollowUpId, item.Feeling, item.IsLocked));
        Assert.Equal(["Dinners"], item.Difficulties);
        Assert.Equal(new CheckInQuestionItem("¿Cómo armo cenas con más proteína?", "AiSuggested"),
            Assert.Single(item.Questions));
    }

    [Fact]
    public async Task At_the_visit_it_is_still_returned_and_marked_locked()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddHours(2));
        await Answer(visit.Id.Value, "Hard");
        _agenda.Clock.Now = visit.ScheduledFor.AddMinutes(10);

        var item = await _agenda.Facade().GetLatestCheckInForPatient(PatientId, PractitionerId);

        Assert.NotNull(item);
        Assert.True(item.IsLocked);
    }

    [Fact]
    public async Task A_check_in_of_a_visit_already_completed_belongs_to_a_previous_cycle()
    {
        var previous = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(1));
        await Answer(previous.Id.Value, "Hard");
        previous.MarkCompleted(1, _agenda.Clock.Now.AddDays(1));
        _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(30));

        Assert.Null(await _agenda.Facade().GetLatestCheckInForPatient(PatientId, PractitionerId));
    }

    [Fact]
    public async Task Another_practitioner_does_not_get_it_and_a_failure_degrades_to_null()
    {
        var visit = _agenda.Add(PatientId, PractitionerId, _agenda.Clock.Now.AddDays(3));
        await Answer(visit.Id.Value, "Good");
        var failing = Substitute.For<IPreVisitCheckInQueryService>();
        failing.Handle(Arg.Any<GetLatestPreVisitCheckInQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));
        var degraded = new MonitoringContextFacade(Substitute.For<IEvaluationWindowQueryService>(),
            Substitute.For<IConsistencyIndexQueryService>(), Substitute.For<IReferralQueryService>(),
            Substitute.For<IScheduledFollowUpQueryService>(), failing);

        Assert.Null(await _agenda.Facade().GetLatestCheckInForPatient(PatientId, 99));
        Assert.Null(await degraded.GetLatestCheckInForPatient(PatientId, PractitionerId));
    }

    [Fact]
    public async Task The_consultation_in_progress_shows_it_and_a_closed_one_does_not_ask()
    {
        var item = new PreVisitCheckInItem(7, "Fair", ["Dinners", "Weekends"],
            [new CheckInQuestionItem("¿Puedo comer fuera los viernes?", "Patient")], DateTimeOffset.UtcNow, null,
            false);
        var monitoring = Substitute.For<IMonitoringContextFacade>();
        monitoring.GetLatestCheckInForPatient(PatientId, PractitionerId, Arg.Any<CancellationToken>())
            .Returns(item);
        var inProgress = ConsultationScenario.Started(44, PatientId, PractitionerId);
        var abandoned = ConsultationScenario.Started(45, PatientId, PractitionerId);
        abandoned.Abandon();
        var consultations = Substitute.For<IConsultationRepository>();
        consultations.FindByIdAsync(44, Arg.Any<CancellationToken>()).Returns(inProgress);
        consultations.FindByIdAsync(45, Arg.Any<CancellationToken>()).Returns(abandoned);
        var service = new ConsultationQueryService(consultations, Substitute.For<INutritionalAssessmentRepository>(),
            Substitute.For<INutritionalDiagnosisRepository>(), Substitute.For<IDefaultGuidelinesProvider>(),
            Substitute.For<INutritionPlanRepository>(), monitoring);

        var open = ConsultationResourceAssembler.ToResource(
            (await service.Handle(new GetConsultationByIdQuery(44)))!);
        var closed = await service.Handle(new GetConsultationByIdQuery(45));

        Assert.NotNull(open.PatientCheckIn);
        Assert.Equal(("Fair", 7), (open.PatientCheckIn.Feeling, open.PatientCheckIn.FollowUpId));
        Assert.Equal(["Dinners", "Weekends"], open.PatientCheckIn.Difficulties);
        Assert.Equal("Patient", Assert.Single(open.PatientCheckIn.Questions).Origin);
        Assert.Null(closed!.PatientCheckIn);
        await monitoring.Received(1).GetLatestCheckInForPatient(PatientId, PractitionerId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Without_a_check_in_the_consultation_resource_says_null()
    {
        var details = new ConsultationDetails(ConsultationScenario.Started(44, PatientId, PractitionerId), null,
            null, null, []);

        Assert.Null(ConsultationResourceAssembler.ToResource(details).PatientCheckIn);
    }
}
