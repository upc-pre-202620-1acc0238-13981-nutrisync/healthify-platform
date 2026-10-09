using System.Globalization;
using System.Security.Claims;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;
using Healthify.Platform.NutritionalCare.Interfaces.REST;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     Follow-ups of NC-9/NC-10/NC-11: the recovery of the in-memory proposal queue after a restart, the message for the
///     patient kept with the EV-5 publication draft, and <c>POST /resolution</c> answering with the inbox fields.
/// </summary>
public class PlanProposalFollowUpTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const string Message = "Notamos que tus cenas son más ligeras. Probemos con estas ideas.";

    // Clinical "today" after the real clock, as in PlanProposalInReviewItemTests.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);

    private readonly InMemoryNutritionalCare _care = new(Today);

    public PlanProposalFollowUpTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    // ---- 1. Recovery of the queue ----

    [Fact]
    public async Task An_item_left_without_a_proposal_by_a_restart_gets_one_after_recovery()
    {
        var plan = await PublishedPlanAsync();
        // The item was opened and queued, then the API restarted: the in-memory queue is empty again.
        var item = await OpenSustainedAsync(DateTimeOffset.UtcNow.AddHours(-2));
        Assert.False(_care.ProposalQueue.IsPending(item.Id.Value));
        _care.Model.Answers(ProposalJson(plan));

        // Start-up of the API: the recovery cycle runs, then the worker drains the queue.
        await Recovery().RunCycleAsync(CancellationToken.None);
        Assert.True(_care.ProposalQueue.IsPending(item.Id.Value));
        await Worker().GenerateAsync(item.Id.Value, CancellationToken.None);

        Assert.True(item.HasPlanProposal);
        Assert.Equal(PlanProposalStatus.Proposed, item.Proposal!.Status.Value);
        Assert.Single(_care.Plans);
    }

    [Fact]
    public async Task Recovery_is_idempotent_with_an_item_waiting_or_already_proposed()
    {
        var plan = await PublishedPlanAsync();
        var item = await OpenSustainedAsync(DateTimeOffset.UtcNow.AddHours(-1));
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(1, Count(await _care.ReviewItems.Handle(Recover(now))));
        // Already waiting: nothing more.
        Assert.Equal(0, Count(await _care.ReviewItems.Handle(Recover(now))));

        _care.Model.Answers(ProposalJson(plan));
        await Worker().GenerateAsync(item.Id.Value, CancellationToken.None);
        Assert.True(item.HasPlanProposal);
        // Already proposed: nothing more, and the model is not asked again.
        Assert.Equal(0, Count(await _care.ReviewItems.Handle(Recover(now))));
        Assert.Single(_care.Model.Requests);
    }

    [Fact]
    public async Task Recovery_leaves_out_old_items_items_without_AI_consent_and_other_signals()
    {
        await PublishedPlanAsync();
        var old = await OpenSustainedAsync(DateTimeOffset.UtcNow.AddHours(-73));
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(0, Count(await _care.ReviewItems.Handle(Recover(now))));
        Assert.False(_care.ProposalQueue.IsPending(old.Id.Value));

        // Within 72 h but without consent: nothing to generate.
        old.CreatedAt = now.AddHours(-1);
        _care.AiConsent.IsAllowedAsync(PatientId, Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(0, Count(await _care.ReviewItems.Handle(Recover(now))));

        // A consistency escalation never gets a proposal.
        _care.AiConsent.IsAllowedAsync(PatientId, Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(true);
        old.Resolve(false, null);
        var escalation = Opened(await _care.ReviewItems.Handle(new OpenReviewItemCommand(PatientId,
            SignalType.ConsistencyEscalation, "Escalated after the patient was asked")));
        escalation.CreatedAt = now;
        Assert.Equal(0, Count(await _care.ReviewItems.Handle(Recover(now))));
    }

    [Fact]
    public async Task With_the_AI_off_recovery_queues_nothing()
    {
        await PublishedPlanAsync();
        await OpenSustainedAsync(DateTimeOffset.UtcNow.AddHours(-1));
        _care.AiConfiguration["Ai:Enabled"] = "false";

        Assert.Equal(0, Count(await _care.ReviewItems.Handle(Recover(DateTimeOffset.UtcNow))));
    }

    [Fact]
    public async Task The_recovery_window_is_configurable()
    {
        await PublishedPlanAsync();
        var item = await OpenSustainedAsync(DateTimeOffset.UtcNow.AddHours(-100));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["NutritionalCare:PlanProposalRecoveryHours"] = "120" }).Build();

        await Recovery(configuration).RunCycleAsync(CancellationToken.None);

        Assert.True(_care.ProposalQueue.IsPending(item.Id.Value));
    }

    // ---- 2. The message in the publication draft ----

    [Fact]
    public async Task The_publication_draft_keeps_the_message_and_EV5_reads_it_back()
    {
        var consultationId = await ConsultationAtStep4Async();

        var saved = ConsultationFlow.Ok(await _care.Consultation.Handle(new SaveConsultationPublicationDraftCommand(
            consultationId, PractitionerId, [DietaryRestriction.LactoseFree], [Guideline.ReduceSalt], [],
            "  " + Message + " ")), "draft");

        Assert.Equal(Message, saved.PublicationDraft!.PatientMessage);
        var details = await _care.Queries.Handle(new Platform.NutritionalCare.Domain.Model.Queries
            .GetConsultationByIdQuery(consultationId));
        Assert.Equal(Message, ConsultationResourceAssembler.ToResource(details!).PublicationDraft!.PatientMessage);

        // Saving again without a message clears it, like the other fields of the draft.
        var cleared = ConsultationFlow.Ok(await _care.Consultation.Handle(new SaveConsultationPublicationDraftCommand(
            consultationId, PractitionerId, [], [Guideline.ReduceSalt], [])), "draft without message");
        Assert.Null(cleared.PublicationDraft!.PatientMessage);
    }

    [Fact]
    public async Task A_draft_message_longer_than_500_characters_is_rejected()
    {
        var consultationId = await ConsultationAtStep4Async();

        var result = await _care.Consultation.Handle(new SaveConsultationPublicationDraftCommand(consultationId,
            PractitionerId, [], [], [], new string('a', 501)));

        Assert.Equal(NutritionalCareError.InvalidPatientMessage,
            Assert.IsType<Result<Consultation, NutritionalCareError>.Failure>(result).Error);
    }

    [Fact]
    public void Drafts_differ_by_their_message()
    {
        Assert.NotEqual(new PublicationDraft([], ["ReduceSalt"], [], "a"), new PublicationDraft([], ["ReduceSalt"], []));
        Assert.Equal(new PublicationDraft([], ["ReduceSalt"], [], " a "), new PublicationDraft([], ["ReduceSalt"], [], "a"));
    }

    // ---- 3. POST /resolution answers like GET /review-items ----

    [Fact]
    public async Task Resolving_answers_with_the_patients_name_and_the_inbox_fields()
    {
        var plan = await PublishedPlanAsync();
        _care.Model.Answers(ProposalJson(plan));
        var item = await OpenSustainedAsync(DateTimeOffset.UtcNow);
        await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value));
        _care.Iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
                { [PatientId] = new(PatientId, "ana@x.pe", "Patient", "Ana", "Flores") });

        var response = await Controller().ResolveReviewItem(item.Id.Value,
            new ResolveReviewItemResource(false, "Lo conversamos en la próxima cita"));

        var resource = Assert.IsType<ReviewItemResource>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("Ana Flores", resource.PatientFullName);
        Assert.True(resource.HasPlanProposal);
        Assert.Equal(-40m, resource.EvidenceData!.AveragePercentFromTarget);
        Assert.Equal("Resolved", resource.State);
        Assert.False(resource.ResolvedWithAdjustment);
    }

    [Fact]
    public async Task A_failed_resolution_still_answers_with_its_problem()
    {
        var response = await Controller().ResolveReviewItem(999, new ResolveReviewItemResource(false, null));

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(response).StatusCode);
    }

    [Fact]
    public async Task Accepting_answers_with_the_patients_name_too()
    {
        var plan = await PublishedPlanAsync();
        _care.Model.Answers(ProposalJson(plan));
        var item = await OpenSustainedAsync(DateTimeOffset.UtcNow);
        await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value));
        _care.Iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
                { [PatientId] = new(PatientId, "ana@x.pe", "Patient", "Ana", "Flores") });

        var response = await Controller().AcceptPlanProposal(item.Id.Value, new AcceptPlanProposalResource(true));

        var resource = Assert.IsType<PlanProposalAcceptanceResource>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("Ana Flores", resource.ReviewItem.PatientFullName);
        Assert.Equal(2, resource.PlanVersion.Version);
    }

    // ---- Helpers ----

    private ReviewItemsController Controller()
    {
        var localizer = Substitute.For<IStringLocalizer<NutritionalCareMessages>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString((string)c[0], (string)c[0]));
        return new ReviewItemsController(_care.ReviewItems, _care.ReviewItemQueries, _care.PlanProposals, localizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, PractitionerId.ToString()),
                        new Claim(ClaimTypes.Role, "Practitioner")
                    ], "test"))
                }
            }
        };
    }

    private PlanProposalRecoveryHostedService Recovery(IConfiguration? configuration = null)
    {
        return new PlanProposalRecoveryHostedService(Fakes.ScopeFactoryWith(_care.ReviewItems),
            configuration ?? new ConfigurationBuilder().Build(), TimeProvider.System,
            NullLogger<PlanProposalRecoveryHostedService>.Instance);
    }

    private PlanProposalGenerationHostedService Worker()
    {
        return new PlanProposalGenerationHostedService(_care.ProposalQueue, Fakes.ScopeFactoryWith(_care.ReviewItems),
            NullLogger<PlanProposalGenerationHostedService>.Instance);
    }

    private static RecoverPlanProposalsCommand Recover(DateTimeOffset now)
    {
        return new RecoverPlanProposalsCommand(now, TimeSpan.FromHours(72));
    }

    private async Task<int> ConsultationAtStep4Async()
    {
        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 80m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, consultationId, PractitionerId,
            DiagnosisCode.ObesityGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);
        return consultationId;
    }

    private async Task<NutritionPlan> PublishedPlanAsync()
    {
        var consultationId = await ConsultationAtStep4Async();
        return ConsultationFlow.Ok(await _care.Consultation.Handle(new PublishFromConsultationCommand(consultationId,
            PractitionerId, [], [Guideline.ReduceSalt], [], "publication")), "publication").Plan;
    }

    /// <summary>An open sustained deviation, as the policy opens it, with the creation time EF would stamp.</summary>
    private async Task<ReviewItem> OpenSustainedAsync(DateTimeOffset createdAt)
    {
        var item = Opened(await _care.ReviewItems.Handle(new OpenReviewItemCommand(PatientId,
            SignalType.SustainedDeviation, "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below"))));
        item.CreatedAt = createdAt;
        return item;
    }

    private static string ProposalJson(NutritionPlan plan)
    {
        var targets = plan.PrescribedTargets!;
        var energy = decimal.Round(targets.EnergyKcal * 0.92m, 0);
        var protein = decimal.Round(targets.ProteinG, 0);
        var fat = decimal.Round(targets.FatG * 0.92m, 0);
        var carb = decimal.Round((energy - 4m * protein - 9m * fat) / 4m, 0);
        return string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "title": "Ajustar la energía y reforzar las cenas",
              "energyKcal": {{energy}}, "proteinG": {{protein}}, "carbG": {{carb}}, "fatG": {{fat}},
              "addedGuidelines": ["ProteinAndVegetablesAtDinner"], "removedGuidelines": [],
              "patientMessage": "{{Message}}", "recheckAfterDays": 7,
              "rationale": "Registró 40 % menos de su meta en 5 de 9 días registrados.",
              "practitionerLanguage": "es", "patientLanguage": "es"
            }
            """);
    }

    private static ReviewItem Opened(Result<ReviewItem, NutritionalCareError> result)
    {
        return Assert.IsType<Result<ReviewItem, NutritionalCareError>.Success>(result).Value;
    }

    private static int Count(Result<int, NutritionalCareError> result)
    {
        return Assert.IsType<Result<int, NutritionalCareError>.Success>(result).Value;
    }
}
