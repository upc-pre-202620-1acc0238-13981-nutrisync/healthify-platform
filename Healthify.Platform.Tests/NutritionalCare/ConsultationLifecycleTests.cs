using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-2. The invariants of the <see cref="Consultation" /> aggregate and its value objects: steps in order,
///     only a consultation in progress moves, completion, discarding, the publication draft and the
///     Idempotency-Key; plus the guards of the publication draft and the key in the command service.
/// </summary>
public class ConsultationLifecycleTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    [Fact]
    public void Completion_needs_the_three_previous_steps()
    {
        var consultation = ConsultationScenario.Started(41, PatientId, PractitionerId);
        Assert.Throws<InvalidOperationException>(() => consultation.Complete(2));

        consultation.AttachAssessment(101);
        consultation.AttachDiagnosis(2);
        Assert.Throws<InvalidOperationException>(() => consultation.Complete(2));
    }

    [Fact]
    public void Completing_closes_the_consultation_with_its_version_and_key()
    {
        var consultation = ConsultationScenario.AtPublication(41, PatientId, PractitionerId, 101, 2, 401);

        consultation.Complete(2, new IdempotencyKey("retry-1"));

        Assert.Equal(ConsultationState.Completed, consultation.State.Value);
        Assert.False(consultation.IsInProgress);
        Assert.Equal(2, consultation.PublishedPlanVersion);
        Assert.NotNull(consultation.CompletedAt);
        Assert.True(consultation.IsReplayOf(new IdempotencyKey("retry-1")));
        Assert.False(consultation.IsReplayOf(new IdempotencyKey("retry-2")));
        Assert.False(consultation.IsReplayOf(null));
        Assert.Throws<InvalidOperationException>(() => consultation.Complete(2));
        Assert.Throws<InvalidOperationException>(() => consultation.AttachAssessment(102));
        Assert.Throws<InvalidOperationException>(consultation.Abandon);
    }

    [Fact]
    public void A_consultation_in_progress_is_never_a_replay()
    {
        var consultation = ConsultationScenario.AtPublication(41, PatientId, PractitionerId, 101, 2, 401);

        Assert.False(consultation.IsReplayOf(new IdempotencyKey("retry-1")));
    }

    [Fact]
    public void Repeating_step_one_asks_for_the_diagnosis_again()
    {
        var consultation = ConsultationScenario.AtPublication(41, PatientId, PractitionerId, 101, 2, 401);

        consultation.AttachAssessment(102);

        Assert.Equal(102, consultation.AssessmentId);
        Assert.Null(consultation.DiagnosisId);
        Assert.Equal(401, consultation.PlanId);
        Assert.Equal(ConsultationStep.Diagnosis, consultation.CurrentStep.Value);
        Assert.Throws<InvalidOperationException>(() => consultation.Complete(2));
    }

    [Fact]
    public void Discarding_stops_the_consultation()
    {
        var consultation = ConsultationScenario.Started(41, PatientId, PractitionerId);

        consultation.Abandon();

        Assert.Equal(ConsultationState.Abandoned, consultation.State.Value);
        Assert.NotNull(consultation.AbandonedAt);
        Assert.Throws<InvalidOperationException>(() => consultation.AttachAssessment(101));
        Assert.Throws<InvalidOperationException>(consultation.Abandon);
    }

    [Fact]
    public void The_publication_draft_comes_after_the_targets_and_is_replaced_when_saved_again()
    {
        var draft = new PublicationDraft(["Vegan"], ["ReduceSalt"], ["Caminar 20 minutos"]);
        var atTargets = ConsultationScenario.Started(41, PatientId, PractitionerId);
        atTargets.AttachAssessment(101);
        atTargets.AttachDiagnosis(2);
        Assert.Throws<InvalidOperationException>(() => atTargets.SavePublicationDraft(draft));

        var consultation = ConsultationScenario.AtPublication(41, PatientId, PractitionerId, 101, 2, 401);
        consultation.SavePublicationDraft(draft);
        consultation.SavePublicationDraft(new PublicationDraft([], ["Drink2LWater"], null));

        Assert.Equal(new PublicationDraft(null, ["Drink2LWater"], []), consultation.PublicationDraft);
    }

    [Fact]
    public void A_publication_draft_drops_blanks_and_repeats_and_keeps_at_most_five_custom_guidelines()
    {
        var draft = new PublicationDraft([" Vegan ", "Vegan", ""], null, ["a b c", "  "]);

        Assert.Equal(["Vegan"], draft.Restrictions);
        Assert.Empty(draft.Guidelines);
        Assert.Equal(["a b c"], draft.CustomGuidelines);
        Assert.Throws<ArgumentException>(() =>
            new PublicationDraft(null, null, ["one", "two", "three", "four", "five", "six"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("ñandú")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void An_idempotency_key_is_1_to_64_visible_ascii_characters(string value)
    {
        Assert.Throws<ArgumentException>(() => new IdempotencyKey(value));
    }

    [Fact]
    public void A_uuid_is_a_valid_idempotency_key()
    {
        var value = Guid.NewGuid().ToString();
        Assert.Equal(value, new IdempotencyKey(value).Value);
    }

    [Fact]
    public void A_published_version_is_superseded_never_discarded_and_a_discarded_draft_never_moves()
    {
        var published = PlanScenario.Prescribed(400, PatientId, PractitionerId, 1, 1);
        published.PublishWith([], []);
        Assert.Throws<InvalidOperationException>(published.Discard);

        var draft = PlanScenario.Prescribed(401, PatientId, PractitionerId, 2, 2);
        draft.Discard();

        Assert.True(draft.IsDiscarded);
        Assert.False(draft.IsActive);
        Assert.Throws<InvalidOperationException>(() => draft.PublishWith([], []));
        Assert.Throws<InvalidOperationException>(draft.Discard);
    }

    [Fact]
    public async Task An_invalid_idempotency_key_is_rejected_before_anything_is_loaded()
    {
        var scenario = new SecondConsultationAtPublication();

        var result = await scenario.Service.Handle(scenario.Publish() with { IdempotencyKey = "has space" });

        var failure = Assert.IsType<Result<ConsultationPublicationOutcome, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.InvalidIdempotencyKey, failure.Error);
        await scenario.Consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task The_publication_draft_is_saved_with_the_consultation_without_publishing_anything()
    {
        var scenario = new SecondConsultationAtPublication();

        var result = await scenario.Service.Handle(new SaveConsultationPublicationDraftCommand(
            SecondConsultationAtPublication.ConsultationId, SecondConsultationAtPublication.PractitionerId,
            [DietaryRestriction.Vegan], [Guideline.ReduceSalt], ["Caminar 20 minutos"]));

        var consultation = Assert.IsType<Result<Consultation, NutritionalCareError>.Success>(result).Value;
        Assert.Equal(new PublicationDraft(["Vegan"], ["ReduceSalt"], ["Caminar 20 minutos"]),
            consultation.PublicationDraft);
        Assert.True(consultation.IsInProgress);
        Assert.False(scenario.Draft.IsPublished);
        Assert.Equal(1, scenario.UnitOfWork.DurableSaves);
        Assert.Empty(Fakes.Published(scenario.Mediator));
    }

    [Theory]
    [InlineData(new[] { "NoOnion" }, new string[0], NutritionalCareError.UnknownRestriction)]
    [InlineData(new string[0], new[] { "Prioriza vegetales" }, NutritionalCareError.UnknownGuideline)]
    public async Task The_publication_draft_checks_the_catalogs_before_loading(string[] restrictions,
        string[] guidelines, NutritionalCareError expected)
    {
        var scenario = new SecondConsultationAtPublication();

        var result = await scenario.Service.Handle(new SaveConsultationPublicationDraftCommand(
            SecondConsultationAtPublication.ConsultationId, SecondConsultationAtPublication.PractitionerId,
            restrictions, guidelines, []));

        Assert.Equal(expected, Assert.IsType<Result<Consultation, NutritionalCareError>.Failure>(result).Error);
        await scenario.Consultations.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task There_is_no_publication_draft_before_the_targets()
    {
        var scenario = new SecondConsultationAtPublication();
        var atDiagnosis = ConsultationScenario.Started(SecondConsultationAtPublication.ConsultationId,
            SecondConsultationAtPublication.PatientId, SecondConsultationAtPublication.PractitionerId);
        atDiagnosis.AttachAssessment(101);
        scenario.Consultations.FindByIdAsync(SecondConsultationAtPublication.ConsultationId,
            Arg.Any<CancellationToken>()).Returns(atDiagnosis);

        var result = await scenario.Service.Handle(new SaveConsultationPublicationDraftCommand(
            SecondConsultationAtPublication.ConsultationId, SecondConsultationAtPublication.PractitionerId, [], [],
            []));

        Assert.Equal(NutritionalCareError.ConsultationStepOutOfOrder,
            Assert.IsType<Result<Consultation, NutritionalCareError>.Failure>(result).Error);
        Assert.Equal(0, scenario.UnitOfWork.DurableSaves);
    }
}
