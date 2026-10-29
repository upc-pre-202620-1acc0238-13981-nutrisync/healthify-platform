using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     A patient never has two active diagnoses at once. The diagnosis written during a consultation stays
///     pending and replaces the active one only when the consultation publishes, in the same transaction.
/// </summary>
public class OneActiveDiagnosisPerPatientTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int AssessmentId = 30;

    [Fact]
    public void A_diagnosis_is_active_until_superseded_and_can_only_be_superseded_once()
    {
        var diagnosis = new NutritionalDiagnosis(IssueCommand());
        Assert.True(diagnosis.IsActive);

        diagnosis.Supersede();

        Assert.False(diagnosis.IsActive);
        Assert.NotNull(diagnosis.SupersededAt);
        Assert.Throws<InvalidOperationException>(diagnosis.Supersede);
    }

    [Fact]
    public async Task From_a_consultation_the_active_diagnosis_is_replaced_at_publication_never_two_active()
    {
        var scenario = new SecondConsultationAtPublication();
        // Before publication the pending diagnosis is not active, so there is exactly one.
        Assert.Single(new[] { scenario.ActiveDiagnosis, scenario.Pending }, d => d.IsActive);

        var result = await scenario.Service.Handle(scenario.Publish());

        Assert.IsType<Result<ConsultationPublicationOutcome, NutritionalCareError>.Success>(result);
        Assert.False(scenario.ActiveDiagnosis.IsActive);
        Assert.True(scenario.Pending.IsActive);
        Assert.Single(new[] { scenario.ActiveDiagnosis, scenario.Pending }, d => d.IsActive);
    }

    [Fact]
    public void A_pending_diagnosis_is_not_active_and_only_publication_or_discard_moves_it()
    {
        var pending = new SecondConsultationAtPublication().Pending;
        Assert.False(pending.IsActive);
        Assert.Throws<InvalidOperationException>(pending.Supersede);

        pending.Activate();

        Assert.True(pending.IsActive);
        Assert.Throws<InvalidOperationException>(pending.Activate);
        Assert.Throws<InvalidOperationException>(pending.Discard);
    }

    private static IssueDiagnosisCommand IssueCommand()
    {
        return new IssueDiagnosisCommand(PatientId, PractitionerId, AssessmentId, "Overweight grade I",
            "BMI 26.3 kg/m2; waist 88 cm.");
    }
}
