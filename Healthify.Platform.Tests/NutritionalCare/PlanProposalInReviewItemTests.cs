using System.Globalization;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-10. The AI plan proposal of a sustained deviation (PR14.IA): generated in the background after the item
///     opens, it attaches a text to the item and never touches the plan; only the practitioner's acceptance creates
///     the adjusted version, supersedes the previous one and resolves the item, together. Then the scheduled recheck.
///     Real Nutritional Care services over in-memory repositories, real AI pipeline with a fake model.
/// </summary>
public class PlanProposalInReviewItemTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const string Message = "Notamos que tus cenas son más ligeras. Probemos con estas ideas.";
    // The aggregate stamps ResolvedAt with the real clock: the clinical "today" of the recheck is after it.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);

    private readonly InMemoryNutritionalCare _care = new(Today);

    public PlanProposalInReviewItemTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    // ---- The key rule: generating a proposal never touches the plan ----

    [Fact]
    public async Task Generating_the_proposal_never_touches_the_plan()
    {
        var plan = await PublishedPlanAsync();
        var before = Snapshot();
        _care.Model.Answers(ProposalJson(plan));
        _care.Mediator.ClearReceivedCalls();

        // The real chain: the policy opens the item and queues the generation; the worker generates it.
        var item = await OpenThroughThePolicyAsync();
        Assert.True(_care.ProposalQueue.IsPending(item.Id.Value));
        await Worker().GenerateAsync(item.Id.Value, CancellationToken.None);

        Assert.True(item.HasPlanProposal);
        Assert.Equal(PlanProposalStatus.Proposed, item.Proposal!.Status.Value);
        Assert.True(item.IsOpen);
        Assert.False(_care.ProposalQueue.IsPending(item.Id.Value));

        // Nothing about the plan moved: same rows, same version in force, same targets, same cache.
        Assert.Equal(before, Snapshot());
        var published = Fakes.Published(_care.Mediator);
        Assert.DoesNotContain(published, e => e is NutritionPlanAdjusted or NutritionPlanPublished
            or PlanVersionSuperseded or ActiveTargetsUpdated or ReviewItemResolved);
    }

    [Fact]
    public void Structurally_the_signal_side_cannot_reach_a_plan_command()
    {
        Type[] forbidden = [typeof(INutritionPlanCommandService), typeof(IPlanProposalCommandService)];
        foreach (var type in new[]
                 {
                     typeof(ReviewItemCommandService), typeof(OnSustainedDeviationDetectedHandler),
                     typeof(PlanProposalGenerationHostedService), typeof(ReviewItemRecheckHostedService),
                     typeof(PlanAdjustmentProposer), typeof(PlanAdjustmentInputReader)
                 })
        foreach (var constructor in type.GetConstructors())
            Assert.DoesNotContain(constructor.GetParameters(), p => forbidden.Contains(p.ParameterType));
    }

    // ---- Acceptance ----

    [Fact]
    public async Task Accepting_as_is_creates_the_adjusted_version_supersedes_the_previous_and_resolves_the_item()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        var proposal = item.Proposal!;
        _care.Mediator.ClearReceivedCalls();

        var outcome = Success(await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true, null)));

        var v2 = outcome.PlanVersion;
        Assert.Equal(2, v2.Version);
        Assert.True(v2.IsActive);
        Assert.False(plan.IsActive);
        Assert.NotNull(plan.SupersededAt);
        Assert.Equal(proposal.ProposedEnergyKcal, v2.PrescribedTargets!.EnergyKcal);
        Assert.Equal(Message, v2.PatientMessage);
        Assert.Contains(v2.Guidelines, g => g.Code == Guideline.ProteinAndVegetablesAtDinner);
        Assert.Equal(plan.Restrictions, v2.Restrictions);
        Assert.Equal("Ajuste por señal: desviación sostenida del " + SpelledToday(), v2.ChangeReason!.Value);

        Assert.False(item.IsOpen);
        Assert.True(item.ResolvedWithAdjustment);
        Assert.Equal("Plan v2 asignado (propuesta IA aceptada tal cual)", item.ResolutionNote);
        Assert.Equal(PlanProposalStatus.AcceptedAsIs, proposal.Status.Value);
        Assert.Equal(2, proposal.AssignedPlanVersion);
        Assert.Equal(item.ResolvedAt!.Value.AddDays(7), item.RecheckDueAt);

        // The version reached the patient through the existing fan-out, with the message (NC-9).
        var cache = Assert.Single(_care.Caches);
        Assert.Equal(2, cache.PlanVersion);
        Assert.Equal(Message, cache.PatientMessage);
        Assert.Empty(_care.Violations);
        var published = Fakes.Published(_care.Mediator);
        Assert.Contains(published, e => e is NutritionPlanAdjusted { Version: 2 });
        Assert.Contains(published, e => e is ReviewItemResolved { ResolvedWithAdjustment: true });

        var resource = PlanAdjustmentProposalResourceAssembler.ToResource(outcome);
        Assert.Equal(2, resource.PlanVersion.Version);
        Assert.Equal("Resolved", resource.ReviewItem.State);
    }

    [Fact]
    public async Task Accepting_with_edits_assigns_the_edited_plan_even_outside_the_AI_band()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);

        // 1 250 kcal is more than 25 % below the version in force but above the 1 200 floor of a woman:
        // a human edit meets the floor only.
        var outcome = Success(await _care.PlanProposals.Handle(new AcceptPlanProposalCommand(item.Id.Value,
            PractitionerId, false,
            new AdjustedPlanDto(1250m, 80m, 140m, 40m, [Guideline.ReduceSalt], "Probemos con cenas completas."))));

        Assert.Equal(1250m, outcome.PlanVersion.PrescribedTargets!.EnergyKcal);
        Assert.Equal("Probemos con cenas completas.", outcome.PlanVersion.PatientMessage);
        Assert.Equal([Guideline.ReduceSalt],
            outcome.PlanVersion.Guidelines.Where(g => !g.IsCustom).Select(g => g.Code));
        Assert.Equal(PlanProposalStatus.AcceptedWithEdits, item.Proposal!.Status.Value);
        Assert.Equal("Plan v2 asignado (propuesta IA aceptada con ediciones)", item.ResolutionNote);
        // What the AI proposed is kept as it was.
        Assert.NotEqual(1250m, item.Proposal.ProposedEnergyKcal);
    }

    [Fact]
    public async Task An_edit_below_the_calorie_floor_is_rejected_and_nothing_changes()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        var before = Snapshot();

        var result = await _care.PlanProposals.Handle(new AcceptPlanProposalCommand(item.Id.Value, PractitionerId,
            false, new AdjustedPlanDto(1100m, 70m, 120m, 40m, [], null)));

        Assert.Equal(NutritionalCareError.PlanProposalOutOfSafetyBounds, Failure(result));
        Assert.Equal(before, Snapshot());
        Assert.True(item.IsOpen);
        Assert.True(item.Proposal!.IsProposed);
    }

    [Fact]
    public async Task Accepting_twice_or_after_resolving_without_assigning_is_a_conflict()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        Success(await _care.PlanProposals.Handle(new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true,
            null)));

        var again = await _care.PlanProposals.Handle(new AcceptPlanProposalCommand(item.Id.Value, PractitionerId,
            true, null));

        Assert.Equal(NutritionalCareError.PlanProposalAlreadyDecided, Failure(again));
        Assert.Equal(2, _care.Plans.Count);
    }

    [Fact]
    public async Task Resolving_without_assigning_dismisses_the_proposal_and_keeps_the_plan()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        var before = Snapshot();

        var resolved = await _care.ReviewItems.Handle(new ResolveReviewItemCommand(item.Id.Value, PractitionerId,
            false, "Lo conversamos en la próxima cita"));

        Assert.True(resolved.IsSuccess);
        Assert.Equal(PlanProposalStatus.Dismissed, item.Proposal!.Status.Value);
        Assert.Null(item.RecheckDueAt);
        Assert.Equal(before, Snapshot());
        Assert.Equal(NutritionalCareError.PlanProposalAlreadyDecided, Failure(await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true, null))));
    }

    [Fact]
    public async Task Only_the_practitioner_of_the_inbox_accepts_and_edits_need_their_fields()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);

        Assert.Equal(NutritionalCareError.PractitionerOnly, Failure(await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, 99, true, null))));
        Assert.Equal(NutritionalCareError.PlanProposalEditsRequired, Failure(await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, false, null))));
        Assert.Equal(NutritionalCareError.UnknownGuideline, Failure(await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, false,
                new AdjustedPlanDto(1600m, 90m, 190m, 55m, ["EatLessCarbs"], null)))));
        Assert.True(item.IsOpen);
    }

    // ---- When there is no proposal ----

    [Fact]
    public async Task Only_a_sustained_deviation_gets_a_proposal()
    {
        await PublishedPlanAsync();
        var item = Opened(await _care.ReviewItems.Handle(new OpenReviewItemCommand(PatientId,
            SignalType.ConsistencyEscalation, "Escalated after the patient was asked")));

        var result = await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value));

        Assert.Equal(NutritionalCareError.ReviewItemNotSustainedDeviation, FailureOf(result));
        Assert.Empty(_care.Model.Requests);
        Assert.Throws<InvalidOperationException>(() => item.AttachProposal(1, "t", 1650m, 95m, 190m, 60m, [], [],
            Message, 7, "r", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task With_the_AI_off_there_is_no_proposal_and_PR14_stays_as_it_was()
    {
        await PublishedPlanAsync();
        _care.AiConfiguration["Ai:Enabled"] = "false";
        var item = await OpenSustainedAsync();

        var result = await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value));

        Assert.Equal(NutritionalCareError.PlanProposalNotFound, FailureOf(result));
        Assert.False(item.HasPlanProposal);
        Assert.Empty(_care.Model.Requests);
        var lookup = await _care.ReviewItemQueries.Handle(new GetPlanProposalByReviewItemIdQuery(item.Id.Value));
        Assert.False(lookup!.IsGenerating);
        Assert.Null(PlanAdjustmentProposalResourceAssembler.ToResource(lookup));
    }

    [Fact]
    public async Task A_proposal_that_breaks_a_safety_rule_is_discarded()
    {
        var plan = await PublishedPlanAsync();
        _care.Model.Answers(ProposalJson(plan, 0.6m));
        var item = await OpenSustainedAsync();
        var before = Snapshot();

        var result = await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value));

        Assert.Equal(NutritionalCareError.PlanProposalNotFound, FailureOf(result));
        Assert.False(item.HasPlanProposal);
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_care.AiLog.Rows).Status);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task While_it_is_generated_the_lookup_says_so()
    {
        await PublishedPlanAsync();
        var item = await OpenSustainedAsync();
        _care.ProposalQueue.TryEnqueue(item.Id.Value);

        var lookup = await _care.ReviewItemQueries.Handle(new GetPlanProposalByReviewItemIdQuery(item.Id.Value));

        Assert.True(lookup!.IsGenerating);
        Assert.Null(await _care.ReviewItemQueries.Handle(new GetPlanProposalByReviewItemIdQuery(999)));
    }

    [Fact]
    public async Task The_proposal_reads_with_the_energy_in_force()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);

        var lookup = await _care.ReviewItemQueries.Handle(new GetPlanProposalByReviewItemIdQuery(item.Id.Value));
        var resource = PlanAdjustmentProposalResourceAssembler.ToResource(lookup!)!;

        Assert.Equal(plan.PrescribedTargets!.EnergyKcal, resource.CurrentEnergyKcal);
        Assert.Equal(Message, resource.PatientMessage);
        Assert.Equal("Proposed", resource.Status);
        Assert.True(ReviewItemResourceAssembler.ToResource(item).HasPlanProposal);
    }

    // ---- The scheduled recheck (DECISIÓN §12-#12) ----

    [Fact]
    public async Task The_recheck_opens_a_ScheduledRecheck_item_once_when_its_date_arrives()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        Success(await _care.PlanProposals.Handle(new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true,
            null)));
        _care.Monitoring.GetComplianceSummary(PatientId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(new ComplianceSummaryItem(3, 1, 1, 2, 5, 7));
        var due = item.RecheckDueAt!.Value;

        Assert.Equal(0, Count(await _care.ReviewItems.Handle(new OpenDueRechecksCommand(due.AddHours(-1)))));
        Assert.Equal(1, Count(await _care.ReviewItems.Handle(new OpenDueRechecksCommand(due.AddHours(1)))));
        Assert.Equal(0, Count(await _care.ReviewItems.Handle(new OpenDueRechecksCommand(due.AddDays(2)))));

        var recheck = Assert.Single(_care.ReviewItemRows, r => r.SignalType.Value == SignalType.ScheduledRecheck);
        Assert.True(recheck.IsOpen);
        Assert.Equal(PractitionerId, recheck.PractitionerId);
        Assert.Equal(2, recheck.EvidenceData!.AdjustedPlanVersion);
        Assert.Equal(2, recheck.EvidenceData.DeviatedDays);
        Assert.Equal(5, recheck.EvidenceData.LoggedDaysConsidered);
        Assert.StartsWith("Scheduled review after the plan adjustment of", recheck.Evidence);
        Assert.NotNull(item.RecheckIssuedAt);
        // A recheck carries no proposal and changes nothing.
        Assert.False(recheck.HasPlanProposal);
        Assert.Equal(2, _care.Plans.Count);
    }

    [Fact]
    public async Task Without_an_active_care_link_the_recheck_closes_without_a_new_item()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan);
        Success(await _care.PlanProposals.Handle(new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true,
            null)));
        _care.CareRelationship.GetActiveCareLinkByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns((Platform.CareRelationship.Interfaces.Acl.CareLinkStatusItem?)null);

        Assert.Equal(0, Count(await _care.ReviewItems.Handle(
            new OpenDueRechecksCommand(item.RecheckDueAt!.Value.AddHours(1)))));
        Assert.NotNull(item.RecheckIssuedAt);
        Assert.DoesNotContain(_care.ReviewItemRows, r => r.SignalType.Value == SignalType.ScheduledRecheck);
    }

    [Fact]
    public void The_recheck_is_1_to_30_days_and_the_macros_add_up()
    {
        var item = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.SustainedDeviation, "e"),
            PractitionerId);

        Assert.Throws<ArgumentException>(() => item.AttachProposal(1, "t", 1650m, 95m, 190m, 60m, [], [], Message,
            31, "r", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => item.AttachProposal(1, "t", 1650m, 120m, 190m, 60m, [], [], Message,
            7, "r", DateTimeOffset.UtcNow));
        Assert.False(item.HasPlanProposal);
    }

    // ---- Helpers ----

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

    private async Task<ReviewItem> OpenThroughThePolicyAsync()
    {
        var handler = new OnSustainedDeviationDetectedHandler(Fakes.ScopeFactoryWith(_care.ReviewItems),
            _care.ProposalQueue, NullLogger<OnSustainedDeviationDetectedHandler>.Instance);
        await handler.Handle(new SustainedDeviationDetected(3, PatientId, 0.4m, "Below", "Mean energy 40.0% below", 5,
            9), CancellationToken.None);
        return Assert.Single(_care.ReviewItemRows);
    }

    private async Task<ReviewItem> OpenSustainedAsync()
    {
        return Opened(await _care.ReviewItems.Handle(new OpenReviewItemCommand(PatientId,
            SignalType.SustainedDeviation, "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below"))));
    }

    private async Task<ReviewItem> ItemWithProposalAsync(NutritionPlan plan)
    {
        _care.Model.Answers(ProposalJson(plan));
        var item = await OpenSustainedAsync();
        Assert.True((await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value))).IsSuccess);
        return item;
    }

    private PlanProposalGenerationHostedService Worker()
    {
        return new PlanProposalGenerationHostedService(_care.ProposalQueue, Fakes.ScopeFactoryWith(_care.ReviewItems),
            NullLogger<PlanProposalGenerationHostedService>.Instance);
    }

    /// <summary>A coherent proposal at <paramref name="factor" /> of the energy in force (0.92 by default).</summary>
    private static string ProposalJson(NutritionPlan plan, decimal factor = 0.92m)
    {
        var targets = plan.PrescribedTargets!;
        var energy = decimal.Round(targets.EnergyKcal * factor, 0);
        var protein = decimal.Round(targets.ProteinG, 0);
        var fat = decimal.Round(targets.FatG * factor, 0);
        var carb = decimal.Round((energy - 4m * protein - 9m * fat) / 4m, 0);
        return string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "title": "Ajustar la energía y reforzar las cenas",
              "energyKcal": {{energy}}, "proteinG": {{protein}}, "carbG": {{carb}}, "fatG": {{fat}},
              "addedGuidelines": ["ProteinAndVegetablesAtDinner"], "removedGuidelines": [],
              "patientMessage": "{{Message}}",
              "recheckAfterDays": 7,
              "rationale": "Registró 40 % menos de su meta en 5 de 9 días registrados.",
              "practitionerLanguage": "es", "patientLanguage": "es"
            }
            """);
    }

    /// <summary>Everything about the plans a proposal could have touched.</summary>
    private string Snapshot()
    {
        var plans = string.Join(";", _care.Plans.OrderBy(p => p.Version).Select(p =>
            $"{p.Id.Value}:{p.Version}:{p.IsActive}:{p.SupersededAt}:{p.PrescribedTargets?.EnergyKcal}:" +
            $"{string.Join(",", p.Guidelines.Select(g => g.ToString()))}:{p.PatientMessage}"));
        var cache = string.Join(";", _care.Caches.Select(c => $"{c.PlanVersion}:{c.PatientMessage}"));
        return plans + "|" + cache;
    }

    private static string SpelledToday()
    {
        // The item was created "now" (UTC) in the test; the default DateOf reads it in UTC.
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        string[] months = ["ene.", "feb.", "mar.", "abr.", "may.", "jun.", "jul.", "ago.", "sept.", "oct.", "nov.", "dic."];
        return $"{date.Day} {months[date.Month - 1]} {date.Year}";
    }

    private static ReviewItem Opened(Result<ReviewItem, NutritionalCareError> result)
    {
        return Assert.IsType<Result<ReviewItem, NutritionalCareError>.Success>(result).Value;
    }

    private static NutritionalCareError FailureOf(Result<ReviewItem, NutritionalCareError> result)
    {
        return Assert.IsType<Result<ReviewItem, NutritionalCareError>.Failure>(result).Error;
    }

    private static PlanProposalAcceptanceOutcome Success(
        Result<PlanProposalAcceptanceOutcome, NutritionalCareError> result)
    {
        return result switch
        {
            Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Success s => s.Value,
            Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Failure f =>
                throw new Xunit.Sdk.XunitException($"Acceptance failed: {f.Error}"),
            _ => throw new Xunit.Sdk.XunitException("No result")
        };
    }

    private static NutritionalCareError Failure(Result<PlanProposalAcceptanceOutcome, NutritionalCareError> result)
    {
        return Assert.IsType<Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Failure>(result).Error;
    }

    private static int Count(Result<int, NutritionalCareError> result)
    {
        return Assert.IsType<Result<int, NutritionalCareError>.Success>(result).Value;
    }
}
