using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     Characterization of the rule One Active Diagnosis Per Patient (Subflow 3.2), reinterpreted by NC-4/NC-7 as
///     "never two active at once". The stand-alone endpoint keeps rejecting a second diagnosis (409); from a
///     consultation the diagnosis of step 2 stays pending and replaces the active one only when step 4 publishes,
///     in the same transaction.
/// </summary>
/// <remarks>
///     Updated on purpose with NC-7: the original cases are kept unchanged; the consultation cases were added.
/// </remarks>
public class OneActiveDiagnosisPerPatientTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int AssessmentId = 30;

    private readonly INutritionalDiagnosisRepository _diagnosisRepository =
        Substitute.For<INutritionalDiagnosisRepository>();

    private readonly INutritionalAssessmentRepository _assessmentRepository =
        Substitute.For<INutritionalAssessmentRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ICareRelationshipContextFacade _careRelationship =
        Substitute.For<ICareRelationshipContextFacade>();

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public OneActiveDiagnosisPerPatientTests()
    {
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>())
            .Returns(true);
        _assessmentRepository.FindByIdAsync(AssessmentId, Arg.Any<CancellationToken>())
            .Returns(ClosedAssessment());
    }

    [Fact]
    public async Task Second_diagnosis_is_rejected_while_one_is_active()
    {
        _diagnosisRepository.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns(new NutritionalDiagnosis(IssueCommand()));

        var result = await CreateService().Handle(IssueCommand());

        var failure = Assert.IsType<Result<NutritionalDiagnosis, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.PatientAlreadyHasActiveDiagnosis, failure.Error);
        await _diagnosisRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task First_diagnosis_is_issued_when_none_is_active()
    {
        _diagnosisRepository.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns((NutritionalDiagnosis?)null);
        await _diagnosisRepository.AddAsync(
            Arg.Do<NutritionalDiagnosis>(d => Identity.Assign(d, new DiagnosisId(1))),
            Arg.Any<CancellationToken>());

        var result = await CreateService().Handle(IssueCommand());

        var success = Assert.IsType<Result<NutritionalDiagnosis, NutritionalCareError>.Success>(result);
        Assert.True(success.Value.IsActive);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

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
        // Before step 4: the pending diagnosis is not active, so there is exactly one.
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

    private NutritionalDiagnosisCommandService CreateService()
    {
        return new NutritionalDiagnosisCommandService(_diagnosisRepository, _assessmentRepository, _unitOfWork,
            _careRelationship, NullLogger<NutritionalDiagnosisCommandService>.Instance, _mediator);
    }

    private static IssueDiagnosisCommand IssueCommand()
    {
        return new IssueDiagnosisCommand(PatientId, PractitionerId, AssessmentId, "Overweight grade I",
            "BMI 26.3 kg/m2; waist 88 cm.");
    }

    private static NutritionalAssessment ClosedAssessment()
    {
        var assessment = new NutritionalAssessment(new RecordAssessmentCommand(PatientId, PractitionerId,
            "Four meals a day", "Hypothyroidism", "Moderate", null, 31, BiologicalSex.Female, null));
        assessment.Close();
        return assessment;
    }
}
