using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     IA-6 / NC-4. Business rule: Accepted Suggestion Is Traceable. "Usar sugerencia" sends back the
///     <c>aiGenerationId</c> of the suggestion card; it travels resource → command → pending diagnosis → active
///     diagnosis at step 4, and is what <c>nutritional_diagnoses.ai_generation_id</c> keeps
///     (<see cref="SecondConsultationMySqlTests" /> reads the column back).
/// </summary>
public class AcceptedAiDiagnosisTraceTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const long GenerationId = 4711;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly InMemoryNutritionalCare _care = new(Today);

    public AcceptedAiDiagnosisTraceTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    [Fact]
    public async Task The_generation_of_an_accepted_suggestion_is_kept_on_the_diagnosis_that_becomes_active()
    {
        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 74.2m);

        // The body of PUT /consultations/{id}/diagnosis after "Usar sugerencia".
        var command = ConsultationCommandAssembler.ToCommand(consultationId, PractitionerId,
            new IssueConsultationDiagnosisResource(DiagnosisCode.OverweightGradeI, DiagnosisSource.AiSuggestionAccepted,
                GenerationId, "IMC 26.3 kg/m² con cintura de 88 cm."));
        var pending = ConsultationFlow.Ok(await _care.Consultation.Handle(command), "diagnosis").Diagnosis;
        Assert.Equal(GenerationId, pending.AiGenerationId);
        Assert.True(pending.Source!.IsAiSuggestionAccepted);

        await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);
        var published = ConsultationFlow.Ok(
            await ConsultationFlow.PublishAsync(_care.Consultation, consultationId, PractitionerId, "publication"),
            "publication");

        var active = Assert.Single(_care.Diagnoses, d => d.IsActive);
        Assert.Same(published.Diagnosis, active);
        Assert.Equal(GenerationId, active.AiGenerationId);
        Assert.Equal("IMC 26.3 kg/m² con cintura de 88 cm.", active.Rationale.Value);
    }

    [Fact]
    public async Task A_code_the_practitioner_picked_carries_no_generation_even_if_one_is_sent()
    {
        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 74.2m);

        var command = ConsultationCommandAssembler.ToCommand(consultationId, PractitionerId,
            new IssueConsultationDiagnosisResource(DiagnosisCode.ObesityGradeI, DiagnosisSource.PractitionerSelected,
                GenerationId, null));
        var pending = ConsultationFlow.Ok(await _care.Consultation.Handle(command), "diagnosis").Diagnosis;

        Assert.Null(pending.AiGenerationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task Accepting_a_suggestion_without_its_generation_is_refused(long? generationId)
    {
        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 74.2m);

        var command = ConsultationCommandAssembler.ToCommand(consultationId, PractitionerId,
            new IssueConsultationDiagnosisResource(DiagnosisCode.OverweightGradeI, DiagnosisSource.AiSuggestionAccepted,
                generationId, "IMC 26.3 kg/m²."));

        Assert.Equal(NutritionalCareError.InvalidDiagnosisSource,
            Assert.IsType<Result<ConsultationDiagnosisOutcome, NutritionalCareError>.Failure>(
                await _care.Consultation.Handle(command)).Error);
        Assert.DoesNotContain(_care.Diagnoses, d => d.IsPendingFor(consultationId));
    }
}
