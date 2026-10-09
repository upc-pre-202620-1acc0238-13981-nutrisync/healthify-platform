using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
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
///     NC-3. The stand-alone endpoints keep their contract: the payloads old clients send still succeed
///     or fail with the same errors, and the new structured values are optional additions.
/// </summary>
public class LegacyAssessmentContractTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const int AssessmentId = 30;

    private readonly INutritionalAssessmentRepository _assessments = Substitute.For<INutritionalAssessmentRepository>();
    private readonly IPatientBaselineRepository _baselines = Substitute.For<IPatientBaselineRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();

    private readonly ICareRelationshipContextFacade _careRelationship =
        Substitute.For<ICareRelationshipContextFacade>();

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public LegacyAssessmentContractTests()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _assessments.AddAsync(Arg.Do<NutritionalAssessment>(a => Identity.Assign(a, new AssessmentId(AssessmentId))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_original_record_payload_still_succeeds_without_a_baseline()
    {
        var result = await CreateService().Handle(OldRecord());

        var assessment = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Success>(result).Value;
        Assert.Equal("Three meals a day", assessment.Habits);
        Assert.Equal(34, assessment.AgeYears);
        Assert.Null(assessment.ActivityLevel);
        Assert.Null(assessment.ConditionsSnapshot);
    }

    [Fact]
    public async Task The_original_record_payload_still_requires_the_free_text()
    {
        var result = await CreateService().Handle(OldRecord() with { MedicalHistory = " " });

        var failure = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.HabitsHistoryAndActivityRequired, failure.Error);
    }

    [Fact]
    public async Task With_a_baseline_the_record_keeps_a_snapshot_of_its_conditions_but_its_own_age()
    {
        _baselines.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(new PatientBaseline(
            new RecordPatientBaselineCommand(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
                ["Gout"]), DateOnly.FromDateTime(DateTime.UtcNow)));

        var result = await CreateService().Handle(OldRecord());

        var assessment = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Success>(result).Value;
        Assert.Equal(["Gout"], assessment.ConditionsSnapshot);
        Assert.Equal(34, assessment.AgeYears);
    }

    [Fact]
    public async Task The_structured_values_are_accepted_as_optional_additions()
    {
        var result = await CreateService().Handle(OldRecord() with
        {
            ActivityLevelCode = "Light",
            EatingHabitsData = new EatingHabitsDto(3, 2m, null),
            BiochemistryData = new BiochemistryDto(95m, 180m, null)
        });

        var assessment = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Success>(result).Value;
        Assert.Equal("Light", assessment.ActivityLevel!.Value);
        Assert.Equal(2m, assessment.EatingHabits!.WaterLitersPerDay);
        Assert.Equal(180m, assessment.BiochemistryPanel!.TotalCholesterolMgDl);
        Assert.Equal("Walks twice a week", assessment.PhysicalActivity);
    }

    [Fact]
    public async Task An_invalid_structured_value_maps_to_its_own_error_before_any_guard()
    {
        var result = await CreateService().Handle(OldRecord() with { ActivityLevelCode = "Athlete" });

        var failure = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.InvalidActivityLevel, failure.Error);
        Assert.Empty(_iam.ReceivedCalls());
    }

    [Fact]
    public async Task The_original_measurement_payload_still_succeeds_and_now_carries_the_bmi()
    {
        _assessments.FindByIdAsync(AssessmentId, Arg.Any<CancellationToken>())
            .Returns(Identity.Assign(new NutritionalAssessment(OldRecord()), new AssessmentId(AssessmentId)));

        var result = await CreateService().Handle(OldMeasurement());

        var assessment = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Success>(result).Value;
        var measurement = Assert.Single(assessment.Measurements);
        Assert.Equal("Fasting, morning, same scale", measurement.Protocol.Value);
        Assert.Null(measurement.ProtocolChecks);
        Assert.Equal(26.3m, measurement.BmiKgM2);
        Assert.Equal(BodyMassIndex.OverweightGradeI, measurement.BmiCategory);
    }

    [Fact]
    public async Task The_original_measurement_payload_still_requires_the_free_text_protocol()
    {
        var result = await CreateService().Handle(OldMeasurement() with { Protocol = "" });

        var failure = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.MeasurementProtocolRequired, failure.Error);
    }

    [Fact]
    public async Task An_empty_checklist_sent_to_the_stand_alone_endpoint_is_rejected()
    {
        var result = await CreateService().Handle(OldMeasurement() with { ProtocolChecks = [] });

        var failure = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.ProtocolChecklistEmpty, failure.Error);
    }

    [Fact]
    public async Task An_unknown_check_sent_to_the_stand_alone_endpoint_has_its_own_error()
    {
        var result = await CreateService().Handle(OldMeasurement() with { ProtocolChecks = ["Fasting", "Barefoot"] });

        var failure = Assert.IsType<Result<NutritionalAssessment, NutritionalCareError>.Failure>(result);
        Assert.Equal(NutritionalCareError.UnknownProtocolCheck, failure.Error);
    }

    private NutritionalAssessmentCommandService CreateService()
    {
        return new NutritionalAssessmentCommandService(_assessments, _baselines, _unitOfWork, _iam,
            _careRelationship, NullLogger<NutritionalAssessmentCommandService>.Instance, _mediator);
    }

    private static RecordAssessmentCommand OldRecord()
    {
        return new RecordAssessmentCommand(PatientId, PractitionerId, "Three meals a day", "Hypothyroidism",
            "Walks twice a week", null, 34, "Female", null);
    }

    private static TakeClinicalMeasurementCommand OldMeasurement()
    {
        return new TakeClinicalMeasurementCommand(AssessmentId, PractitionerId, 74.2m, 168m,
            "Fasting, morning, same scale", null, 88m);
    }
}
