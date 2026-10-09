using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-9. The practitioner's message to the patient belongs to one plan version: it comes in with the consultation
///     publication or the adjustment (optional), travels in ActiveTargetsUpdated to the Intake cache (PT4/PT3.M),
///     stays with its version and is not carried over to the next one.
/// </summary>
public class PatientMessagePerVersionTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const string Message = "Notamos que tus cenas son más ligeras. Probemos con estas ideas.";
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly InMemoryNutritionalCare _care = new(Today);

    public PatientMessagePerVersionTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    [Fact]
    public async Task The_message_of_the_consultation_publication_reaches_the_patient_cache()
    {
        var plan = await PublishFirstVersionAsync("  " + Message + "  ");

        Assert.Equal(Message, plan.PatientMessage);
        Assert.Equal(Message, Assert.Single(_care.Caches).PatientMessage);
        Assert.Equal(Message, NutritionPlanResourceAssembler.ToResource(plan).PatientMessage);
    }

    [Fact]
    public async Task An_adjustment_carries_its_own_message_and_the_next_one_does_not_inherit_it()
    {
        var first = await PublishFirstVersionAsync(null);

        var second = ConsultationFlow.Ok(await _care.PlanCommands.Handle(Adjust(first.Id.Value, 1700m, Message)),
            "adjustment with message");
        Assert.Equal(Message, second.PatientMessage);
        Assert.Equal(Message, Assert.Single(_care.Caches).PatientMessage);
        Assert.Null(first.PatientMessage);

        var third = ConsultationFlow.Ok(await _care.PlanCommands.Handle(Adjust(second.Id.Value, 1650m, null)),
            "adjustment without message");
        Assert.Null(third.PatientMessage);
        Assert.Null(Assert.Single(_care.Caches).PatientMessage);
        // The version keeps the message it was published with.
        Assert.Equal(Message, second.PatientMessage);
    }

    [Fact]
    public async Task A_message_longer_than_500_characters_is_rejected_before_anything_is_written()
    {
        var first = await PublishFirstVersionAsync(null);
        var saves = _care.UnitOfWork.Saves;

        var result = await _care.PlanCommands.Handle(Adjust(first.Id.Value, 1700m, new string('a', 501)));

        Assert.Equal(NutritionalCareError.InvalidPatientMessage,
            Assert.IsType<Result<Platform.NutritionalCare.Domain.Model.Aggregates.NutritionPlan, NutritionalCareError>
                .Failure>(result).Error);
        Assert.Equal(saves, _care.UnitOfWork.Saves);
        Assert.True(first.IsActive);
    }

    [Fact]
    public async Task A_consultation_publication_with_a_too_long_message_is_rejected()
    {
        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 80m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, consultationId, PractitionerId,
            DiagnosisCode.ObesityGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);

        var result = await _care.Consultation.Handle(new PublishFromConsultationCommand(consultationId, PractitionerId,
            [], [Guideline.ReduceSalt], [], null, new string('a', 501)));

        Assert.True(result.IsFailure);
        Assert.Empty(_care.Caches);
        Assert.DoesNotContain(_care.Plans, p => p.IsPublished);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_message_is_no_message(string? value)
    {
        Assert.Null(PatientFacingMessage.FromOptional(value));
    }

    [Fact]
    public void A_message_is_trimmed_and_holds_at_most_500_characters()
    {
        Assert.Equal("Hola", new PatientFacingMessage("  Hola ").Value);
        Assert.Equal(500, new PatientFacingMessage(new string('a', 500)).Value.Length);
        Assert.Throws<ArgumentException>(() => new PatientFacingMessage(new string('a', 501)));
        Assert.Throws<ArgumentException>(() => new PatientFacingMessage(" "));
    }

    [Fact]
    public async Task A_published_version_keeps_the_message_it_was_published_with()
    {
        var plan = await PublishFirstVersionAsync(Message);

        Assert.Throws<InvalidOperationException>(() => plan.SetPatientMessage(new PatientFacingMessage("Otro")));
        Assert.Equal(Message, plan.PatientMessage);
    }

    private async Task<Platform.NutritionalCare.Domain.Model.Aggregates.NutritionPlan> PublishFirstVersionAsync(
        string? message)
    {
        var consultationId = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, consultationId, PractitionerId, 80m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, consultationId, PractitionerId,
            DiagnosisCode.ObesityGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, consultationId, PractitionerId);
        return ConsultationFlow.Ok(await _care.Consultation.Handle(new PublishFromConsultationCommand(consultationId,
            PractitionerId, [], [Guideline.ReduceSalt], [], "publication", message)), "publication").Plan;
    }

    private static AdjustNutritionPlanCommand Adjust(int planId, decimal energyKcal, string? message)
    {
        // 4P + 4C + 9F stays close to the energy; the adjustment does not check it.
        return new AdjustNutritionPlanCommand(planId, PractitionerId, energyKcal, 100m, 180m, 60m,
            [Guideline.ReduceSalt], [], "Ajuste entre consultas", null, message);
    }
}
