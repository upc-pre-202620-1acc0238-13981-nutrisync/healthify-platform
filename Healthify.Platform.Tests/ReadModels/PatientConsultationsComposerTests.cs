using System.Text.RegularExpressions;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST.Resources;
using Healthify.Platform.ReadModels.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.ReadModels;

/// <summary>
///     RM-5. PT25 composed from Monitoring (next visit and its check in), Nutritional Care (consultations already
///     held) and Iam (the practitioner's name). Only what the patient can see, and each section degrades alone.
/// </summary>
public class PatientConsultationsComposerTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateTimeOffset Next = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);

    private readonly IMonitoringContextFacade _monitoring = Substitute.For<IMonitoringContextFacade>();
    private readonly INutritionalCareContextFacade _nutritionalCare = Substitute.For<INutritionalCareContextFacade>();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();

    public PatientConsultationsComposerTests()
    {
        _monitoring.GetNextFollowUpsByPatientIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, NextFollowUpItem>
            {
                [PatientId] = new(7, PatientId, Next, "InPerson", ["Fasting"],
                    new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero), PractitionerId)
            });
        _iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
            {
                [PractitionerId] = new(PractitionerId, "rosa@correo.com", "Practitioner", "Rosa", "Medina")
            });
        _nutritionalCare.GetCompletedConsultations(PatientId, Arg.Any<CancellationToken>())
            .Returns([
                new CompletedConsultationItem(2, new DateTimeOffset(2026, 9, 3, 15, 0, 0, TimeSpan.Zero), 3, false),
                new CompletedConsultationItem(1, new DateTimeOffset(2026, 3, 12, 15, 0, 0, TimeSpan.Zero), 1, true)
            ]);
    }

    private PatientConsultationsComposer Composer()
    {
        return new PatientConsultationsComposer(_monitoring, _nutritionalCare, _iam);
    }

    private static PreVisitCheckInItem CheckInOf(int followUpId)
    {
        return new PreVisitCheckInItem(followUpId, "Fair", ["Dinners", "Weekends"],
            [new CheckInQuestionItem("¿Puedo comer fuera los viernes?", "Patient")],
            new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero), null, false);
    }

    [Fact]
    public async Task It_combines_the_next_visit_its_check_in_and_the_past_consultations()
    {
        _monitoring.GetLatestCheckInForPatient(PatientId, PractitionerId, Arg.Any<CancellationToken>())
            .Returns(CheckInOf(7));

        var resource = PatientConsultationsOverviewResourceAssembler.ToResource(await Composer().Compose(PatientId));

        Assert.NotNull(resource.Next);
        Assert.Equal((7, Next, "InPerson", "Rosa Medina"), (resource.Next.FollowUpId, resource.Next.ScheduledFor,
            resource.Next.Modality, resource.Next.PractitionerFullName));
        Assert.Equal(["Fasting"], resource.Next.Preparation);
        Assert.NotNull(resource.CheckIn);
        Assert.Equal("Fair", resource.CheckIn.Feeling);
        Assert.Equal(["¿Puedo comer fuera los viernes?"], resource.CheckIn.Questions);
        Assert.Equal([
            (2, PastConsultationResource.AssessmentAndNewPlan, (int?)3),
            (1, PastConsultationResource.FirstConsultation, (int?)1)
        ], resource.Past.Select(p => (p.ConsultationId, p.Label, p.PlanVersion)));
    }

    [Fact]
    public async Task Without_an_answer_the_check_in_is_null_so_the_app_shows_Responder()
    {
        _monitoring.GetLatestCheckInForPatient(PatientId, PractitionerId, Arg.Any<CancellationToken>())
            .Returns((PreVisitCheckInItem?)null);

        var composition = await Composer().Compose(PatientId);

        Assert.NotNull(composition.Next);
        Assert.Null(composition.CheckIn);
    }

    [Fact]
    public async Task A_check_in_of_another_visit_is_not_shown_as_the_one_of_the_next()
    {
        _monitoring.GetLatestCheckInForPatient(PatientId, PractitionerId, Arg.Any<CancellationToken>())
            .Returns(CheckInOf(6));

        Assert.Null((await Composer().Compose(PatientId)).CheckIn);
    }

    [Fact]
    public async Task Without_a_visit_there_is_no_next_and_no_check_in_but_the_past_is_still_shown()
    {
        _monitoring.GetNextFollowUpsByPatientIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, NextFollowUpItem>());

        var composition = await Composer().Compose(PatientId);

        Assert.Null(composition.Next);
        Assert.Null(composition.CheckIn);
        Assert.Equal(2, composition.Past.Count);
        await _monitoring.DidNotReceiveWithAnyArgs().GetLatestCheckInForPatient(default, default, default);
    }

    [Fact]
    public async Task When_Iam_and_Nutritional_Care_are_quiet_their_sections_are_empty()
    {
        _iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>());
        _nutritionalCare.GetCompletedConsultations(PatientId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompletedConsultationItem>());

        var composition = await Composer().Compose(PatientId);

        Assert.NotNull(composition.Next);
        Assert.Null(composition.NextPractitionerFullName);
        Assert.Empty(composition.Past);
    }

    [Fact]
    public void The_resources_carry_no_diagnosis_basis_weight_or_bmi()
    {
        var forbidden = new[] { "Diagnosis", "Basis", "Rationale", "Bmi", "Weight", "Waist", "Category" };
        var words = new[]
            {
                typeof(PatientConsultationsOverviewResource), typeof(UpcomingConsultationResource),
                typeof(ConsultationCheckInSummaryResource), typeof(PastConsultationResource),
                typeof(CompletedConsultationItem)
            }
            .SelectMany(t => t.GetProperties())
            .SelectMany(p => Regex.Split(p.Name, "(?<!^)(?=[A-Z])"))
            .ToList();

        Assert.DoesNotContain(words, w => forbidden.Contains(w));
    }

    [Fact]
    public async Task Nutritional_Care_publishes_completed_consultations_most_recent_first_and_degrades_to_empty()
    {
        var consultations = Substitute.For<IConsultationQueryService>();
        consultations.Handle(Arg.Is<GetConsultationsByPatientIdQuery>(q => q.State!.Value == ConsultationState.Completed),
                Arg.Any<CancellationToken>())
            .Returns([
                Summary(1, new DateTimeOffset(2026, 3, 12, 15, 0, 0, TimeSpan.Zero), true),
                Summary(2, new DateTimeOffset(2026, 9, 3, 15, 0, 0, TimeSpan.Zero), false)
            ]);
        var facade = new NutritionalCareContextFacade(Substitute.For<INutritionPlanQueryService>(),
            Substitute.For<IReviewItemQueryService>(), Substitute.For<IPatientBaselineQueryService>(), consultations,
            new FixedClinicalDate(new DateOnly(2026, 9, 18)),
            Substitute.For<INutritionalAssessmentQueryService>(), Substitute.For<INutritionalDiagnosisQueryService>());

        var past = await facade.GetCompletedConsultations(PatientId);
        consultations.Handle(Arg.Any<GetConsultationsByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));

        Assert.Equal([2, 1], past.Select(p => p.ConsultationId));
        Assert.Equal([false, true], past.Select(p => p.IsFirstConsultation));
        Assert.Empty(await facade.GetCompletedConsultations(PatientId));
    }

    [Fact]
    public async Task The_next_visit_crosses_with_the_practitioner_whose_agenda_it_is_on()
    {
        var agenda = new InMemoryScheduledFollowUps(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));
        await agenda.Commands.Handle(new ScheduleFollowUpCommand(PatientId, PractitionerId,
            DateTimeOffset.UtcNow.AddDays(3)));

        var next = await agenda.Facade().GetNextFollowUpsByPatientIds([PatientId]);

        Assert.Equal(PractitionerId, next[PatientId].PractitionerId);
    }

    private static ConsultationSummary Summary(int id, DateTimeOffset completedAt, bool isFirst)
    {
        return new ConsultationSummary(id, ConsultationState.Completed, completedAt.AddHours(-1), completedAt, id,
            isFirst, 74m, 26m, 88m);
    }
}
