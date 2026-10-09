using System.Globalization;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     X-2. Text the system generates is stored as a code with its parameters, for the client to word in the reader's
///     language, and the old sentence stays as the legacy fallback; text a person writes is kept as written; text of
///     the AI is generated in the language of whoever reads it. Here: the change reason of a plan version
///     (NewConsultation, SignalAdjustment, Custom), the structured evidence of every signal type and the two languages
///     of an AI plan proposal. Real Nutritional Care services over in-memory repositories, fake model.
/// </summary>
public class GeneratedTextLanguageTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    private const string EnglishTitle = "Adjust the energy and strengthen dinners";
    private const string SpanishMessage = "Notamos que tus cenas son más ligeras. Probemos con estas ideas.";
    private const string EnglishRationale = "Logged 40 % below the target on 5 of 9 logged days.";

    // The proposal tests resolve with the real clock; the clinical "today" of a consultation is fixed.
    private static readonly DateOnly ConsultationDay = new(2026, 9, 18);

    private readonly InMemoryNutritionalCare _care = new(ConsultationDay);

    public GeneratedTextLanguageTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            ConsultationDay));
    }

    // ---- Change reason of a plan version ----

    [Fact]
    public async Task A_version_published_by_a_consultation_exposes_NewConsultation_with_its_date()
    {
        var first = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        var second = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second-publication");

        Assert.Null(first.Plan.ChangeReason);
        var reason = second.Plan.ChangeReason!;
        Assert.Equal(ChangeReason.NewConsultationCode, reason.Code);
        Assert.Equal(new ChangeReasonData(ConsultationDay, null), reason.Data);
        // The Spanish sentence stays, as the fallback of clients that do not know the code.
        Assert.Equal("Nueva consulta del 18 sept. 2026", reason.Value);

        var resource = NutritionPlanResourceAssembler.ToResource(second.Plan);
        Assert.Equal("NewConsultation", resource.ChangeReasonCode);
        Assert.Equal(ConsultationDay, resource.ChangeReasonData!.Date);
        Assert.Null(resource.ChangeReasonData.SignalType);
        Assert.Equal("Nueva consulta del 18 sept. 2026", resource.ChangeReason);

        var v1 = NutritionPlanResourceAssembler.ToResource(first.Plan);
        Assert.Null(v1.ChangeReasonCode);
        Assert.Null(v1.ChangeReasonData);
    }

    [Fact]
    public async Task A_reason_the_practitioner_writes_is_Custom_and_kept_as_written()
    {
        var plan = (await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication")).Plan;
        var targets = plan.PrescribedTargets!;

        var result = await _care.PlanCommands.Handle(new AdjustNutritionPlanCommand(plan.Id.Value, PractitionerId,
            targets.EnergyKcal - 100m, targets.ProteinG, targets.CarbG - 25m, targets.FatG, [Guideline.ReduceSalt],
            plan.Restrictions.ToList(), "  Bajó su actividad física en septiembre  "));

        var adjusted = Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Success>(result).Value;
        Assert.Equal(ChangeReason.Custom, adjusted.ChangeReason!.Code);
        Assert.Null(adjusted.ChangeReason.Data);
        Assert.Equal("Bajó su actividad física en septiembre", adjusted.ChangeReason.Value);

        var resource = NutritionPlanResourceAssembler.ToResource(adjusted);
        Assert.Equal("Custom", resource.ChangeReasonCode);
        Assert.Null(resource.ChangeReasonData);
        Assert.Equal("Bajó su actividad física en septiembre", resource.ChangeReason);
    }

    [Fact]
    public async Task A_version_assigned_from_an_AI_proposal_is_SignalAdjustment_with_the_signal_date()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan, ProposalJson(plan, "es", "es"));

        var result = await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true, null));

        var v2 = Assert.IsType<Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Success>(result).Value
            .PlanVersion;
        // The item was created "now" (UTC) in the test; the default DateOf reads it in UTC.
        var signalDay = DateOnly.FromDateTime(item.CreatedAt?.UtcDateTime ?? DateTime.UtcNow);
        Assert.Equal(ChangeReason.SignalAdjustmentCode, v2.ChangeReason!.Code);
        Assert.Equal(new ChangeReasonData(signalDay, SignalType.SustainedDeviation), v2.ChangeReason.Data);
        Assert.StartsWith("Ajuste por señal: desviación sostenida del ", v2.ChangeReason.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Custom")]
    [InlineData("SomethingNew")]
    public void A_stored_reason_without_a_known_code_reads_as_Custom(string? code)
    {
        var reason = ChangeReason.Rehydrate("Nueva consulta del 18 sept. 2026", code,
            new ChangeReasonData(ConsultationDay, null))!;

        Assert.Equal(ChangeReason.Custom, reason.Code);
        Assert.Null(reason.Data);
        Assert.Equal("Nueva consulta del 18 sept. 2026", reason.Value);
        Assert.Null(ChangeReason.Rehydrate(null, "NewConsultation", null));
    }

    [Fact]
    public void The_parameters_of_a_reason_are_stored_as_json_and_read_back()
    {
        var data = new ChangeReasonData(new DateOnly(2026, 9, 8), SignalType.SustainedDeviation);

        var json = ChangeReasonDataJsonConverter.Serialize(data);

        Assert.Equal("""{"date":"2026-09-08","signalType":"SustainedDeviation"}""", json);
        Assert.Equal(data, ChangeReasonDataJsonConverter.Deserialize(json));
        // The shape the backfill of the migration writes (JSON_OBJECT, keys in another order).
        Assert.Equal(new ChangeReasonData(new DateOnly(2026, 9, 18), null),
            ChangeReasonDataJsonConverter.Deserialize("""{"date": "2026-09-18"}"""));
        Assert.Null(ChangeReasonDataJsonConverter.Deserialize(null));
    }

    // ---- Evidence of a review item as numbers ----

    [Fact]
    public async Task A_consistency_escalation_opens_its_item_with_the_evidence_as_numbers()
    {
        var handler = new OnAlertEscalatedToPractitionerHandler(
            Fakes.ScopeFactoryWith((typeof(IReviewItemCommandService), _care.ReviewItems),
                (typeof(IClinicalDateProvider), new FixedClinicalDate(ConsultationDay))),
            NullLogger<OnAlertEscalatedToPractitionerHandler>.Instance);
        var alertSince = new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero);
        var shown = new DateTimeOffset(2026, 9, 9, 13, 30, 0, TimeSpan.Zero);

        await handler.Handle(new AlertEscalatedToPractitioner(PatientId, PractitionerId, 0.4567m,
            "Consistency index 0.457 kg per week of unexplained weight movement, in Alert since 2026-08-20.",
            "Alert", alertSince, shown, 4), CancellationToken.None);

        var item = Assert.Single(_care.ReviewItemRows);
        Assert.Equal(SignalType.ConsistencyEscalation, item.SignalType.Value);
        Assert.Equal(new ReviewItemEvidence(null, null, null, null, consistencyKgPerWeek: 0.457m,
            consistencyState: "Alert", alertSinceOn: new DateOnly(2026, 8, 20), weeksInAlert: 4,
            shownToPatientOn: new DateOnly(2026, 9, 9)), item.EvidenceData);
        // The English sentence stays as the fallback.
        Assert.StartsWith("Consistency index 0.457", item.Evidence);

        var resource = ReviewItemResourceAssembler.ToResource(item).EvidenceData!;
        Assert.Equal(0.457m, resource.ConsistencyKgPerWeek);
        Assert.Equal("Alert", resource.ConsistencyState);
        Assert.Equal(new DateOnly(2026, 8, 20), resource.AlertSinceOn);
        Assert.Equal(4, resource.WeeksInAlert);
        Assert.Equal(new DateOnly(2026, 9, 9), resource.ShownToPatientOn);
    }

    [Fact]
    public async Task An_escalation_from_a_producer_before_X2_still_opens_its_item_with_the_text_only()
    {
        var handler = new OnAlertEscalatedToPractitionerHandler(Fakes.ScopeFactoryWith(_care.ReviewItems),
            NullLogger<OnAlertEscalatedToPractitionerHandler>.Instance);

        await handler.Handle(new AlertEscalatedToPractitioner(PatientId, PractitionerId, 0.4m, "Consistency index"),
            CancellationToken.None);

        var item = Assert.Single(_care.ReviewItemRows);
        Assert.Null(item.EvidenceData);
        Assert.Equal("Consistency index", item.Evidence);
    }

    [Fact]
    public async Task A_sustained_deviation_carries_its_kcal_per_day_signed_like_the_percent()
    {
        var handler = new OnSustainedDeviationDetectedHandler(Fakes.ScopeFactoryWith(_care.ReviewItems),
            _care.ProposalQueue, NullLogger<OnSustainedDeviationDetectedHandler>.Instance);

        await handler.Handle(new SustainedDeviationDetected(3, PatientId, 0.4m, "Below", "Mean energy 40.0% below", 5,
            9, 718.4m), CancellationToken.None);

        var evidence = Assert.Single(_care.ReviewItemRows).EvidenceData!;
        Assert.Equal(-40m, evidence.AveragePercentFromTarget);
        Assert.Equal(-718.4m, evidence.AverageEnergyKcalFromTarget);
        Assert.Equal((5, 9, "Below"), (evidence.DeviatedDays!.Value, evidence.LoggedDaysConsidered!.Value,
            evidence.Direction));
    }

    [Fact]
    public void Every_field_of_the_evidence_is_stored_as_json_and_read_back()
    {
        ReviewItemEvidence[] samples =
        [
            new(-40m, 5, 9, "Below", averageEnergyKcalFromTarget: -718.4m),
            new(null, 2, 6, null, new DateOnly(2026, 9, 8), 3),
            new(null, null, null, null, consistencyKgPerWeek: 0.457m, consistencyState: "Alert",
                alertSinceOn: new DateOnly(2026, 8, 20), weeksInAlert: 4, shownToPatientOn: new DateOnly(2026, 9, 9))
        ];

        foreach (var evidence in samples)
            Assert.Equal(evidence,
                ReviewItemEvidenceJsonConverter.Deserialize(ReviewItemEvidenceJsonConverter.Serialize(evidence)));
        // Rows written before X-2 read the same.
        Assert.Equal(new ReviewItemEvidence(-40m, 5, 9, "Below"), ReviewItemEvidenceJsonConverter.Deserialize(
            """{"averagePercentFromTarget":-40,"deviatedDays":5,"loggedDaysConsidered":9,"direction":"Below"}"""));
    }

    // ---- The AI plan proposal: each part in the language of whoever reads it ----

    [Fact]
    public async Task The_proposal_title_is_in_the_practitioners_language_and_the_message_in_the_patients()
    {
        _care.Iam.GetPreferredLanguage(PractitionerId, Arg.Any<CancellationToken>()).Returns("en");
        _care.Iam.GetPreferredLanguage(PatientId, Arg.Any<CancellationToken>()).Returns("es");
        var plan = await PublishedPlanAsync();

        var item = await ItemWithProposalAsync(plan, ProposalJson(plan, "en", "es"));

        // One call carries both languages; the instructions are rendered for the practitioner.
        var request = Assert.Single(_care.Model.Requests);
        Assert.Contains("\"languages\":{\"practitioner\":\"en\",\"patient\":\"es\"}", request.UserContent);
        Assert.Contains("`title` and `rationale` in `languages.practitioner` (`en`)", request.SystemInstruction);
        Assert.Equal("plan-adjustment-proposal@2", Assert.Single(_care.AiLog.Rows).PromptVersion);

        var proposal = item.Proposal!;
        Assert.Equal((EnglishTitle, EnglishRationale, "en"),
            (proposal.Title, proposal.Rationale, proposal.PractitionerLanguage));
        Assert.Equal((SpanishMessage, "es"), (proposal.PatientMessage, proposal.PatientLanguage));

        var resource = PlanAdjustmentProposalResourceAssembler.ToResource(
            (await _care.ReviewItemQueries.Handle(new GetPlanProposalByReviewItemIdQuery(item.Id.Value)))!)!;
        Assert.Equal(("en", "es"), (resource.PractitionerLanguage, resource.PatientLanguage));
        Assert.Equal(EnglishTitle, resource.Title);
        Assert.Equal(SpanishMessage, resource.PatientMessage);
    }

    [Fact]
    public async Task A_proposal_written_in_another_language_than_asked_is_rejected()
    {
        _care.Iam.GetPreferredLanguage(PractitionerId, Arg.Any<CancellationToken>()).Returns("en");
        var plan = await PublishedPlanAsync();
        _care.Model.Answers(ProposalJson(plan, "es", "es"));
        var item = await OpenSustainedAsync();

        var result = await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value));

        Assert.Equal(NutritionalCareError.PlanProposalNotFound,
            Assert.IsType<Result<ReviewItem, NutritionalCareError>.Failure>(result).Error);
        Assert.False(item.HasPlanProposal);
        Assert.Equal(AiGenerationStatus.Rejected, Assert.Single(_care.AiLog.Rows).Status);
    }

    [Fact]
    public async Task A_proposal_stored_before_X2_has_no_languages()
    {
        var plan = await PublishedPlanAsync();
        var item = await OpenSustainedAsync();
        var targets = plan.PrescribedTargets!;

        item.AttachProposal(1, "Ajuste", targets.EnergyKcal, targets.ProteinG, targets.CarbG, targets.FatG, [], [],
            SpanishMessage, 7, "Por qué", DateTimeOffset.UtcNow);

        Assert.Null(item.Proposal!.PractitionerLanguage);
        Assert.Null(item.Proposal.PatientLanguage);
        Assert.Throws<ArgumentException>(() => new ReviewItem(new OpenReviewItemCommand(PatientId,
                SignalType.SustainedDeviation, "e"), PractitionerId)
            .AttachProposal(1, "Ajuste", targets.EnergyKcal, targets.ProteinG, targets.CarbG, targets.FatG, [], [],
                SpanishMessage, 7, "Por qué", DateTimeOffset.UtcNow, "fr", "es"));
    }

    // ---- Resolution note of a review item ----

    [Fact]
    public async Task Accepting_a_proposal_as_is_resolves_with_PlanAssignedAsIs_and_the_version()
    {
        var plan = await PublishedPlanAsync();
        var item = await ItemWithProposalAsync(plan, ProposalJson(plan, "es", "es"));

        var result = await _care.PlanProposals.Handle(
            new AcceptPlanProposalCommand(item.Id.Value, PractitionerId, true, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(ResolutionNoteCode.PlanAssignedAsIs, item.ResolutionNoteCode);
        Assert.Equal(new ResolutionNoteData(2), item.ResolutionNoteData);
        // The Spanish sentence stays, as the fallback.
        Assert.Equal("Plan v2 asignado (propuesta IA aceptada tal cual)", item.ResolutionNote);

        var resource = ReviewItemResourceAssembler.ToResource(item);
        Assert.Equal("PlanAssignedAsIs", resource.ResolutionNoteCode);
        Assert.Equal(2, resource.ResolutionNoteData!.PlanVersion);
        Assert.Equal("Plan v2 asignado (propuesta IA aceptada tal cual)", resource.ResolutionNote);
    }

    [Fact]
    public void Accepting_with_edits_resolves_with_PlanAssignedWithEdits()
    {
        var item = SustainedWithProposal();

        item.ResolveByAssigningPlan(4, acceptedAsIs: false);

        Assert.Equal(ResolutionNoteCode.PlanAssignedWithEdits, item.ResolutionNoteCode);
        Assert.Equal(new ResolutionNoteData(4), item.ResolutionNoteData);
        Assert.Equal("Plan v4 asignado (propuesta IA aceptada con ediciones)", item.ResolutionNote);
    }

    [Fact]
    public void A_note_the_practitioner_writes_is_Custom_and_kept_as_written()
    {
        var withProposal = SustainedWithProposal();
        withProposal.Resolve(false, "  Lo conversamos en la próxima consulta  ");
        var withoutProposal = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.ConsistencyEscalation,
            "e"), PractitionerId);
        withoutProposal.Resolve(true, "Ajusté el plan por teléfono");

        Assert.Equal((ResolutionNoteCode.Custom, "Lo conversamos en la próxima consulta"),
            (withProposal.ResolutionNoteCode, withProposal.ResolutionNote));
        Assert.Null(withProposal.ResolutionNoteData);
        Assert.Equal(PlanProposalStatus.Dismissed, withProposal.Proposal!.Status.Value);
        Assert.Equal((ResolutionNoteCode.Custom, "Ajusté el plan por teléfono"),
            (withoutProposal.ResolutionNoteCode, withoutProposal.ResolutionNote));
    }

    [Fact]
    public void Discarding_a_proposal_without_a_note_is_ProposalDiscarded_and_nothing_else_has_a_code()
    {
        var discarded = SustainedWithProposal();
        discarded.Resolve(false, "  ");
        var plain = Identity.Assign(new ReviewItem(new OpenReviewItemCommand(PatientId,
            SignalType.ConsistencyEscalation, "e"), PractitionerId), new ReviewItemId(6));
        plain.Resolve(false, null);

        Assert.Equal(ResolutionNoteCode.ProposalDiscarded, discarded.ResolutionNoteCode);
        Assert.Null(discarded.ResolutionNote);
        Assert.Null(discarded.ResolutionNoteData);
        Assert.Null(plain.ResolutionNoteCode);
        Assert.Null(plain.ResolutionNote);
        Assert.Null(SustainedWithProposal().ResolutionNoteCode);
        Assert.Equal("ProposalDiscarded", ReviewItemResourceAssembler.ToResource(discarded).ResolutionNoteCode);
    }

    [Fact]
    public void The_parameters_of_a_resolution_note_are_stored_as_json_and_read_back()
    {
        Assert.Equal("""{"planVersion":2}""", ResolutionNoteDataJsonConverter.Serialize(new ResolutionNoteData(2)));
        Assert.Equal(new ResolutionNoteData(7), ResolutionNoteDataJsonConverter.Deserialize("""{"planVersion": 7}"""));
        Assert.Null(ResolutionNoteDataJsonConverter.Deserialize(null));
    }

    // ---- Helpers ----

    private static ReviewItem SustainedWithProposal()
    {
        var item = Identity.Assign(new ReviewItem(new OpenReviewItemCommand(PatientId,
            SignalType.SustainedDeviation, "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below")),
            PractitionerId), new ReviewItemId(5));
        item.AttachProposal(1, "Ajuste", 1650m, 95m, 190m, 60m, [], [], SpanishMessage, 7, "Por qué",
            DateTimeOffset.UtcNow, "es", "es");
        return item;
    }

    private async Task<NutritionPlan> PublishedPlanAsync()
    {
        return (await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "publication")).Plan;
    }

    private async Task<ReviewItem> OpenSustainedAsync()
    {
        var result = await _care.ReviewItems.Handle(new OpenReviewItemCommand(PatientId,
            SignalType.SustainedDeviation, "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below")));
        return Assert.IsType<Result<ReviewItem, NutritionalCareError>.Success>(result).Value;
    }

    private async Task<ReviewItem> ItemWithProposalAsync(NutritionPlan plan, string json)
    {
        _care.Model.Answers(json);
        var item = await OpenSustainedAsync();
        Assert.True((await _care.ReviewItems.Handle(new GeneratePlanProposalCommand(item.Id.Value))).IsSuccess);
        return item;
    }

    /// <summary>A coherent proposal at 92 % of the energy in force, written as the given languages say.</summary>
    private static string ProposalJson(NutritionPlan plan, string practitionerLanguage, string patientLanguage)
    {
        var targets = plan.PrescribedTargets!;
        var energy = decimal.Round(targets.EnergyKcal * 0.92m, 0);
        var protein = decimal.Round(targets.ProteinG, 0);
        var fat = decimal.Round(targets.FatG * 0.92m, 0);
        var carb = decimal.Round((energy - 4m * protein - 9m * fat) / 4m, 0);
        return string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "title": "{{EnglishTitle}}",
              "energyKcal": {{energy}}, "proteinG": {{protein}}, "carbG": {{carb}}, "fatG": {{fat}},
              "addedGuidelines": ["ProteinAndVegetablesAtDinner"], "removedGuidelines": [],
              "patientMessage": "{{SpanishMessage}}",
              "recheckAfterDays": 7,
              "rationale": "{{EnglishRationale}}",
              "practitionerLanguage": "{{practitionerLanguage}}", "patientLanguage": "{{patientLanguage}}"
            }
            """);
    }
}
