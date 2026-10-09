using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-2. The read side of the consultation (resume, history) and its REST mapping: the 409 of a second
///     consultation carries the identifier of the one in progress, the pending diagnosis appears only in the
///     consultation, and the new errors map to 400.
/// </summary>
public class ConsultationQueryAndRestTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly InMemoryNutritionalCare _care = new(Today);
    private readonly IStringLocalizer<NutritionalCareMessages> _localizer =
        Substitute.For<IStringLocalizer<NutritionalCareMessages>>();

    public ConsultationQueryAndRestTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
        _localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.ArgAt<string>(0), call.ArgAt<string>(0)));
    }

    [Fact]
    public async Task A_second_consultation_answers_409_with_the_identifier_of_the_one_in_progress()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);

        var result = await _care.Consultation.Handle(new StartConsultationCommand(PatientId, PractitionerId));
        var inProgress = await _care.Queries.Handle(new GetInProgressConsultationByPatientIdQuery(PatientId));
        var response = Assert.IsType<ObjectResult>(
            NutritionalCareActionResultAssembler.ToStartConsultationResult(result, inProgress, _localizer));

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(id, problem.Extensions["consultationId"]);
    }

    [Fact]
    public async Task Starting_answers_201_with_the_consultation_on_step_one()
    {
        var result = await _care.Consultation.Handle(new StartConsultationCommand(PatientId, PractitionerId));
        var details = await _care.Queries.Handle(new GetInProgressConsultationByPatientIdQuery(PatientId));

        var response = Assert.IsType<ObjectResult>(
            NutritionalCareActionResultAssembler.ToStartConsultationResult(result, details, _localizer));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var resource = Assert.IsType<ConsultationResource>(response.Value);
        Assert.Equal((ConsultationState.InProgress, ConsultationStep.Measurement, 1),
            (resource.State, resource.CurrentStep, resource.StepNumber));
        Assert.True(resource.IsFirstConsultation);
        Assert.Null(resource.PatientCheckIn);
    }

    [Fact]
    public async Task Resuming_rehydrates_every_saved_step_and_shows_the_diagnosis_as_pending()
    {
        var id = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, id, PractitionerId, 74.2m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, id, PractitionerId, DiagnosisCode.OverweightGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, id, PractitionerId);
        ConsultationFlow.Ok(await _care.Consultation.Handle(new SaveConsultationPublicationDraftCommand(id,
            PractitionerId, [DietaryRestriction.Vegan], [Guideline.ReduceSalt], [])), "draft");

        var details = await _care.Queries.Handle(new GetInProgressConsultationByPatientIdQuery(PatientId));
        var resource = ConsultationResourceAssembler.ToResource(details!);

        Assert.Equal(ConsultationStep.Publication, resource.CurrentStep);
        Assert.Equal(4, resource.StepNumber);
        Assert.Equal(74.2m, resource.Measurement!.WeightKg);
        Assert.Equal(26.3m, resource.Measurement.Bmi);
        Assert.Equal(168m, resource.Measurement.HeightCm);
        Assert.Equal(DiagnosisCode.OverweightGradeI, resource.Diagnosis!.Code);
        Assert.True(resource.Diagnosis.IsPending);
        Assert.Equal(1, resource.Targets!.Version);
        Assert.NotNull(resource.Targets.Prescribed);
        Assert.False(resource.Targets.IsPublished);
        Assert.Equal(["Vegan"], resource.PublicationDraft!.Restrictions);
        Assert.Equal(["ReduceSalt"], resource.PublicationDraft.Guidelines);
        // The pending diagnosis is not the active one: nothing outside the consultation sees it.
        Assert.DoesNotContain(_care.Diagnoses, d => d.IsActive);
    }

    [Fact]
    public async Task There_is_no_consultation_in_progress_once_it_is_published()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first");

        Assert.Null(await _care.Queries.Handle(new GetInProgressConsultationByPatientIdQuery(PatientId)));
    }

    [Fact]
    public async Task The_history_lists_completed_consultations_with_their_version_and_measurement()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first");
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second");
        await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);

        Assert.True(ConsultationQueryAssembler.TryToQuery(PatientId, "completed", out var query));
        var completed = await _care.Queries.Handle(query);
        var all = await _care.Queries.Handle(new GetConsultationsByPatientIdQuery(PatientId));

        Assert.Equal(3, all.Count);
        Assert.Equal(2, completed.Count);
        Assert.Equal([2, 1], completed.Select(c => c.PlanVersion!.Value));
        var first = completed[1];
        Assert.True(first.IsFirstConsultation);
        Assert.Equal((80m, 28.3m, 88m), (first.WeightKg!.Value, first.Bmi!.Value, first.WaistCm!.Value));
        Assert.All(completed, c => Assert.NotNull(c.CompletedAt));
        Assert.Empty(await _care.Queries.Handle(new GetConsultationsByPatientIdQuery(99)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("InProgress", true)]
    [InlineData("Abandoned", true)]
    [InlineData("Finished", false)]
    public void The_state_filter_accepts_only_consultation_states(string? state, bool valid)
    {
        Assert.Equal(valid, ConsultationQueryAssembler.TryToQuery(PatientId, state, out _));
    }

    [Theory]
    [InlineData(NutritionalCareError.InvalidIdempotencyKey, StatusCodes.Status400BadRequest)]
    [InlineData(NutritionalCareError.InvalidConsultationState, StatusCodes.Status400BadRequest)]
    [InlineData(NutritionalCareError.ConsultationAlreadyInProgress, StatusCodes.Status409Conflict)]
    [InlineData(NutritionalCareError.ConsultationNotInProgress, StatusCodes.Status409Conflict)]
    [InlineData(NutritionalCareError.ConsultationStepOutOfOrder, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(NutritionalCareError.BaselineRequired, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(NutritionalCareError.ConsultationNotFound, StatusCodes.Status404NotFound)]
    public void Consultation_errors_map_to_their_status(NutritionalCareError error, int status)
    {
        var response = Assert.IsType<ObjectResult>(NutritionalCareActionResultAssembler.ToErrorResult(error,
            _localizer));

        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public void Discarding_answers_204()
    {
        var result = new Result<Consultation, NutritionalCareError>.Success(
            ConsultationScenario.Started(41, PatientId, PractitionerId));

        Assert.IsType<NoContentResult>(NutritionalCareActionResultAssembler.ToNoContentResult(result, _localizer));
    }

    [Fact]
    public async Task The_publication_answers_200_with_the_completed_consultation()
    {
        var outcome = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first");
        var details = await _care.Queries.Handle(new GetConsultationByIdQuery(outcome.Consultation.Id.Value));

        var response = Assert.IsType<ObjectResult>(NutritionalCareActionResultAssembler.ToConsultationResult(
            new Result<ConsultationPublicationOutcome, NutritionalCareError>.Success(outcome), details, _localizer));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var resource = Assert.IsType<ConsultationResource>(response.Value);
        Assert.Equal(ConsultationState.Completed, resource.State);
        Assert.Equal(1, resource.PublishedPlanVersion);
        Assert.True(resource.Targets!.IsPublished);
        Assert.False(resource.Diagnosis!.IsPending);
    }
}
