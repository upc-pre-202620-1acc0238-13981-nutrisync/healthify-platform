using Healthify.Platform.NutritionalCare.Application.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     RM-2. What the Nutritional Care facade publishes for PAC-1: the baseline as "Mujer · 31 años · 168 cm ·
///     hipotiroidismo", where a consultation in progress was left, and when the last one was published. Nothing
///     a step produced crosses, and every method degrades to null.
/// </summary>
public class PatientSummaryFacadeTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly IPatientBaselineQueryService _baselines = Substitute.For<IPatientBaselineQueryService>();
    private readonly IConsultationQueryService _consultations = Substitute.For<IConsultationQueryService>();

    [Fact]
    public async Task The_baseline_crosses_with_the_age_of_the_practice_today()
    {
        _baselines.Handle(Arg.Any<GetPatientBaselineByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female",
                168m, Today));

        var baseline = await Facade().GetBaselineSummary(PatientId);

        Assert.NotNull(baseline);
        Assert.Equal(("Female", 31, 168m), (baseline.BiologicalSex, baseline.AgeYears, baseline.HeightCm));
        Assert.Equal(["Hypothyroidism"], baseline.Conditions);
    }

    [Fact]
    public async Task Without_baseline_or_when_the_lookup_fails_it_is_null()
    {
        _baselines.Handle(Arg.Any<GetPatientBaselineByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((PatientBaseline?)null);
        Assert.Null(await Facade().GetBaselineSummary(PatientId));

        _baselines.Handle(Arg.Any<GetPatientBaselineByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));
        Assert.Null(await Facade().GetBaselineSummary(PatientId));
    }

    [Fact]
    public async Task The_consultation_in_progress_crosses_as_its_step_and_last_save_only()
    {
        var consultation = ConsultationScenario.Started(44, PatientId, PractitionerId);
        _consultations.Handle(Arg.Any<GetInProgressConsultationByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultationDetails(consultation, null, null, null, []));

        var inProgress = await Facade().GetConsultationInProgress(PatientId);

        Assert.NotNull(inProgress);
        Assert.Equal((44, 1, ConsultationStep.Measurement), (inProgress.ConsultationId, inProgress.StepNumber,
            inProgress.StepName));
        Assert.Equal(consultation.LastSavedAt, inProgress.LastSavedAt);
    }

    [Fact]
    public async Task The_last_completed_consultation_is_the_latest_published_one()
    {
        var first = new DateTimeOffset(2026, 3, 3, 15, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 9, 3, 15, 0, 0, TimeSpan.Zero);
        _consultations.Handle(Arg.Is<GetConsultationsByPatientIdQuery>(q => q.State!.Value == ConsultationState.Completed),
                Arg.Any<CancellationToken>())
            .Returns([Summary(2, second), Summary(1, first)]);

        Assert.Equal(second, await Facade().GetLastCompletedConsultationAt(PatientId));
    }

    [Fact]
    public async Task Before_the_first_consultation_there_is_no_last_one()
    {
        _consultations.Handle(Arg.Any<GetConsultationsByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConsultationSummary>());

        Assert.Null(await Facade().GetLastCompletedConsultationAt(PatientId));
    }

    private static ConsultationSummary Summary(int id, DateTimeOffset completedAt)
    {
        return new ConsultationSummary(id, ConsultationState.Completed, completedAt.AddHours(-1), completedAt, id,
            id == 1, 74m, 26m, 88m);
    }

    private NutritionalCareContextFacade Facade()
    {
        return new NutritionalCareContextFacade(Substitute.For<INutritionPlanQueryService>(),
            Substitute.For<IReviewItemQueryService>(), _baselines, _consultations, new FixedClinicalDate(Today),
            Substitute.For<INutritionalAssessmentQueryService>(), Substitute.For<INutritionalDiagnosisQueryService>());
    }
}
