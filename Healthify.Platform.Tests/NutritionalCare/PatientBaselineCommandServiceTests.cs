using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>NC-1. Order of the guards of the baseline command service and its persistence.</summary>
public class PatientBaselineCommandServiceTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 10, 5);

    private readonly IPatientBaselineRepository _repository = Substitute.For<IPatientBaselineRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();

    private readonly ICareRelationshipContextFacade _careRelationship =
        Substitute.For<ICareRelationshipContextFacade>();

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public PatientBaselineCommandServiceTests()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>())
            .Returns(true);
        _repository.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns((PatientBaseline?)null);
    }

    [Fact]
    public async Task Baseline_is_recorded_and_announced_after_the_commit()
    {
        await _repository.AddAsync(
            Arg.Do<PatientBaseline>(b => Identity.Assign(b, new PatientBaselineId(1))),
            Arg.Any<CancellationToken>());

        var result = await CreateService().Handle(Record());

        var success = Assert.IsType<Result<PatientBaseline, NutritionalCareError>.Success>(result);
        Assert.Equal(PatientId, success.Value.PatientId);
        Assert.False(success.Value.BirthDateEstimated);
        Received.InOrder(() =>
        {
            _unitOfWork.CompleteAsync(Arg.Any<CancellationToken>());
            _mediator.PublishAsync(Arg.Any<PatientBaselineRecorded>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Unknown_condition_is_rejected_before_asking_any_other_context()
    {
        var result = await CreateService().Handle(Record(conditions: ["Hypothyroidism", "Other"]));

        AssertFailure(result, NutritionalCareError.UnknownMedicalCondition);
        Assert.Empty(_iam.ReceivedCalls());
        Assert.Empty(_careRelationship.ReceivedCalls());
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
    }

    [Fact]
    public async Task Each_invalid_value_maps_to_its_own_error()
    {
        var service = CreateService();
        AssertFailure(await service.Handle(Record(birthDate: Today)), NutritionalCareError.InvalidBirthDate);
        AssertFailure(await service.Handle(Record(sex: "Other")), NutritionalCareError.InvalidBiologicalSex);
        AssertFailure(await service.Handle(Record(heightCm: 300m)), NutritionalCareError.InvalidHeight);
    }

    [Fact]
    public async Task A_patient_cannot_record_a_baseline()
    {
        _iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(false);

        AssertFailure(await CreateService().Handle(Record()), NutritionalCareError.PractitionerOnly);
        Assert.Empty(_careRelationship.ReceivedCalls());
    }

    [Fact]
    public async Task Without_an_active_care_link_nothing_is_writable()
    {
        _careRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>())
            .Returns(false);

        AssertFailure(await CreateService().Handle(Record()), NutritionalCareError.ActiveCareLinkRequired);
        await _repository.DidNotReceiveWithAnyArgs().FindByPatientIdAsync(default);
    }

    [Fact]
    public async Task A_second_record_is_a_conflict()
    {
        _repository.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns(new PatientBaseline(Record(), Today));

        AssertFailure(await CreateService().Handle(Record()), NutritionalCareError.BaselineAlreadyRecorded);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task Updating_a_missing_baseline_is_not_found()
    {
        var result = await CreateService().Handle(new UpdatePatientBaselineCommand(PatientId, PractitionerId,
            new DateOnly(1995, 3, 10), "Female", 168m, []));

        AssertFailure(result, NutritionalCareError.BaselineNotFound);
    }

    [Fact]
    public async Task Update_persists_and_announces_the_change()
    {
        var baseline = new PatientBaseline(Record(), Today);
        _repository.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(baseline);

        var result = await CreateService().Handle(new UpdatePatientBaselineCommand(PatientId, PractitionerId,
            new DateOnly(1995, 3, 10), "Female", 165m, ["Gout"]));

        Assert.IsType<Result<PatientBaseline, NutritionalCareError>.Success>(result);
        Assert.Equal(165m, baseline.Height.Value);
        _repository.Received(1).Update(baseline);
        Assert.IsType<PatientBaselineUpdated>(Assert.Single(Fakes.Published(_mediator)));
    }

    private PatientBaselineCommandService CreateService()
    {
        return new PatientBaselineCommandService(_repository, _unitOfWork, _iam, _careRelationship,
            new FixedClinicalDate(Today), NullLogger<PatientBaselineCommandService>.Instance, _mediator);
    }

    private static RecordPatientBaselineCommand Record(DateOnly? birthDate = null, string sex = "Female",
        decimal heightCm = 168m, IReadOnlyList<string>? conditions = null)
    {
        return new RecordPatientBaselineCommand(PatientId, PractitionerId, birthDate ?? new DateOnly(1995, 3, 10),
            sex, heightCm, conditions ?? ["Hypothyroidism"]);
    }

    private static void AssertFailure(Result<PatientBaseline, NutritionalCareError> result,
        NutritionalCareError expected)
    {
        var failure = Assert.IsType<Result<PatientBaseline, NutritionalCareError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
