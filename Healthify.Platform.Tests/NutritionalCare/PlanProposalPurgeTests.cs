using System.Globalization;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     IA-8. When the patient's AI processing ends (consent withdrawn, or the link ended or discharge, which publish
///     the same <see cref="AiProcessingConsentChanged" /> with Granted = false), the AI plan proposals no practitioner
///     accepted are deleted. Accepted ones produced a version of the plan and stay as clinical record. The review
///     items are untouched, and PR14 keeps working without a proposal. Real services over in-memory repositories;
///     <see cref="PlanProposalAcceptanceMySqlTests" /> checks that the rows really go away in MySQL.
/// </summary>
public class PlanProposalPurgeTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);

    private readonly InMemoryNutritionalCare _care = new(Today);

    public PlanProposalPurgeTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    [Fact]
    public async Task A_pending_proposal_is_deleted_and_the_item_keeps_working_as_PR14()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        var before = (item.State.Value, item.Evidence, item.EvidenceData, item.CreatedAt);

        await ConsentEnded();

        Assert.False(item.HasPlanProposal);
        Assert.Null(item.Proposal);
        Assert.Same(item, Assert.Single(_care.ReviewItemRows));
        Assert.Equal(before, (item.State.Value, item.Evidence, item.EvidenceData, item.CreatedAt));

        // PR14 without AI: no proposal and none on the way (404), and the practitioner resolves as before.
        var lookup = await _care.ReviewItemQueries.Handle(new GetPlanProposalByReviewItemIdQuery(item.Id.Value));
        Assert.Null(lookup!.ReviewItem.Proposal);
        Assert.False(lookup.IsGenerating);
        Assert.Equal(NutritionalCareError.PlanProposalNotFound, Assert.IsType<
            Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Failure>(await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true, null))).Error);
        Assert.True((await _care.ReviewItems.Handle(new ResolveReviewItemCommand(item.Id.Value, PractitionerId, false,
            "Lo conversamos en la próxima cita"))).IsSuccess);
        Assert.Single(_care.Plans, p => p.IsActive);
    }

    [Fact]
    public async Task An_accepted_proposal_stays_and_a_pending_one_of_the_same_patient_goes()
    {
        var plan = await PublishedPlanAsync();
        var accepted = await ItemWithProposalAsync(plan);
        var assigned = Assert.IsType<Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Success>(
            await _care.PlanProposals.Handle(new AcceptPlanProposalCommand(accepted.Id.Value, PractitionerId, true,
                null))).Value.PlanVersion;
        var pending = await ItemWithProposalAsync(assigned);

        await ConsentEnded();

        Assert.NotNull(accepted.Proposal);
        Assert.Equal(PlanProposalStatus.AcceptedAsIs, accepted.Proposal!.Status.Value);
        Assert.Equal(assigned.Version, accepted.Proposal.AssignedPlanVersion);
        Assert.Null(pending.Proposal);
        Assert.True(pending.IsOpen);
        Assert.Equal(2, _care.ReviewItemRows.Count);
        Assert.True(assigned.IsActive); // the version it produced is untouched
    }

    [Fact]
    public async Task A_dismissed_proposal_is_deleted_and_the_resolution_stays()
    {
        // DECISIÓN IA-8: "no aceptadas" includes the dismissed ones.
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        await _care.ReviewItems.Handle(new ResolveReviewItemCommand(item.Id.Value, PractitionerId, false, "Sin cambios"));

        await ConsentEnded();

        Assert.Null(item.Proposal);
        Assert.False(item.IsOpen);
        Assert.Equal("Sin cambios", item.ResolutionNote);
    }

    [Fact]
    public async Task Granting_consent_and_other_patients_are_left_alone()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);

        await Handler().Handle(new AiProcessingConsentChanged(PatientId, true, DateTimeOffset.UtcNow),
            CancellationToken.None);
        await Handler().Handle(new AiProcessingConsentChanged(PatientId + 1, false, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.NotNull(item.Proposal);
        Assert.Equal(0, Assert.IsType<Result<int, NutritionalCareError>.Success>(
            await _care.ReviewItems.Handle(new PurgeUnacceptedPlanProposalsCommand(PatientId + 1))).Value);
    }

    [Fact]
    public async Task A_proposal_whose_consent_ended_during_the_generation_is_not_attached()
    {
        var plan = await PublishedPlanAsync();
        _care.Model.Answers(ProposalJson(plan));
        var item = await OpenSustainedAsync();
        // The pipeline still sees the consent; the withdrawal commits while the model answers.
        _care.AiConsent.IsAllowedAsync(PatientId, Arg.Any<AiFeature>(), Arg.Any<CancellationToken>())
            .Returns(true, false);

        var result = await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value));

        Assert.Equal(NutritionalCareError.PlanProposalNotFound,
            Assert.IsType<Result<ReviewItem, NutritionalCareError>.Failure>(result).Error);
        Assert.False(item.HasPlanProposal);
    }

    [Fact]
    public void The_aggregate_never_drops_an_accepted_proposal()
    {
        var item = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.SustainedDeviation, "x"),
            PractitionerId);
        Assert.False(item.PurgeUnacceptedProposal());

        item.AttachProposal(1, "Ajuste", 1650m, 100m, 190m, 55m, [], [], "Mensaje", 7, "Por qué",
            DateTimeOffset.UtcNow);
        item.ResolveByAssigningPlan(2, true);

        Assert.False(item.PurgeUnacceptedProposal());
        Assert.NotNull(item.Proposal);
    }

    // ---- Helpers ----

    private Task ConsentEnded()
    {
        return Handler().Handle(new AiProcessingConsentChanged(PatientId, false, DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private OnAiProcessingConsentChangedNutritionalCareHandler Handler()
    {
        return new OnAiProcessingConsentChangedNutritionalCareHandler(Fakes.ScopeFactoryWith(_care.ReviewItems),
            NullLogger<OnAiProcessingConsentChangedNutritionalCareHandler>.Instance);
    }

    private async Task<NutritionPlan> PublishedPlanAsync()
    {
        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 80m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, consultationId, PractitionerId,
            DiagnosisCode.ObesityGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);
        return ConsultationFlow.Ok(await _care.Consultation.Handle(new PublishFromConsultationCommand(consultationId,
            PractitionerId, [DietaryRestriction.LactoseFree], [Guideline.ReduceSalt], ["Caminar 20 minutos"],
            "publication")), "publication").Plan;
    }

    private async Task<ReviewItem> OpenSustainedAsync()
    {
        return Assert.IsType<Result<ReviewItem, NutritionalCareError>.Success>(await _care.ReviewItems.Handle(
            new OpenReviewItemCommand(PatientId, SignalType.SustainedDeviation, "Mean energy 40.0% below",
                new ReviewItemEvidenceDto(-40m, 5, 9, "Below")))).Value;
    }

    private async Task<ReviewItem> ItemWithProposalAsync(NutritionPlan plan)
    {
        _care.Model.Answers(ProposalJson(plan));
        var item = await OpenSustainedAsync();
        Assert.True((await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value))).IsSuccess);
        Assert.True(item.HasPlanProposal);
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
              "addedGuidelines": [], "removedGuidelines": [],
              "patientMessage": "Probemos con estas ideas para las cenas.",
              "recheckAfterDays": 7,
              "rationale": "Registró 40 % menos de su meta en 5 de 9 días registrados.",
              "practitionerLanguage": "es", "patientLanguage": "es"
            }
            """);
    }
}
