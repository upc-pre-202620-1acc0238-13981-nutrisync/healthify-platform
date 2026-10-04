using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
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
///     NC-4 on the stand-alone endpoint POST /nutritional-diagnoses: it accepts a code, writes the
///     rationale when none is given, and keeps its 409 (see <see cref="OneActiveDiagnosisPerPatientTests" />).
/// </summary>
public class StandaloneCodedDiagnosisTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int AssessmentId = 100;

    private readonly INutritionalDiagnosisRepository _diagnoses = Substitute.For<INutritionalDiagnosisRepository>();
    private readonly INutritionalAssessmentRepository _assessments = Substitute.For<INutritionalAssessmentRepository>();
    private readonly ICareRelationshipContextFacade _careRelationship = Substitute.For<ICareRelationshipContextFacade>();

    public StandaloneCodedDiagnosisTests()
    {
        var today = new DateOnly(2026, 3, 10);
        var baseline = ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female",
            168m, today);
        _assessments.FindByIdAsync(AssessmentId, Arg.Any<CancellationToken>()).Returns(
            ConsultationScenario.MeasuredAssessment(AssessmentId, 40, PractitionerId, baseline, today, 74.2m));
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _diagnoses.AddAsync(Arg.Do<NutritionalDiagnosis>(d => Identity.Assign(d, new DiagnosisId(1))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_code_without_rationale_is_issued_with_the_deterministic_one()
    {
        var result = await CreateService().Handle(
            new IssueDiagnosisCommand(PatientId, PractitionerId, AssessmentId, null, null, "OverweightGradeI"));

        var diagnosis = Assert.IsType<Result<NutritionalDiagnosis, NutritionalCareError>.Success>(result).Value;
        Assert.Equal(DiagnosisCode.OverweightGradeI, diagnosis.Code!.Value);
        Assert.Equal(DiagnosisSource.PractitionerSelected, diagnosis.Source!.Value);
        Assert.Equal("IMC 26.3 kg/m²; cintura 88 cm", diagnosis.Rationale.Value);
    }

    [Fact]
    public async Task An_unknown_code_is_rejected_before_anything_loads()
    {
        var result = await CreateService().Handle(
            new IssueDiagnosisCommand(PatientId, PractitionerId, AssessmentId, null, null, "Otro"));

        var failure = Assert.IsType<Result<NutritionalDiagnosis, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.UnknownDiagnosisCode, failure.Error);
        await _assessments.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    private NutritionalDiagnosisCommandService CreateService()
    {
        return new NutritionalDiagnosisCommandService(_diagnoses, _assessments, Substitute.For<IUnitOfWork>(),
            _careRelationship, NullLogger<NutritionalDiagnosisCommandService>.Instance, Substitute.For<IMediator>());
    }
}
