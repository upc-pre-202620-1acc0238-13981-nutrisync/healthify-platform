using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>IN-1 acceptance criteria on the diary command service.</summary>
public class PlanAdherenceCommandTests
{
    private const int PatientId = 1;
    private const int PizzaId = 40;

    private readonly IDiaryEntryRepository _repository = Substitute.For<IDiaryEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public PlanAdherenceCommandTests()
    {
        _repository.AddAsync(Arg.Do<DiaryEntry>(e => Identity.Assign(e, new DiaryEntryId(1))),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, IntakeError.PlanAdherenceRequired)]
    [InlineData("", IntakeError.PlanAdherenceRequired)]
    [InlineData("NotAnswered", IntakeError.PlanAdherenceRequired)]
    [InlineData("Maybe", IntakeError.InvalidPlanAdherence)]
    public async Task Manual_log_without_a_valid_answer_is_rejected(string? answer, IntakeError expected)
    {
        var result = await CreateService().Handle(Manual(answer));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Off_plan_manual_log_is_an_ordinary_manual_entry_that_counts()
    {
        var result = await CreateService().Handle(Manual(PlanAdherence.OffPlan));

        var entry = Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result).Value;
        Assert.True(entry.Provenance.IsManual);
        Assert.True(entry.PlanAdherence.IsOffPlan);
        Assert.True(entry.HasConfirmedEstimate);

        var published = Fakes.Published(_mediator);
        var logged = Assert.IsType<MealLogged>(published[0]);
        Assert.Equal(PlanAdherence.OffPlan, logged.PlanAdherence);
        Assert.True(logged.ConfirmedOnCreation);
        var confirmed = Assert.IsType<EstimateConfirmedByPatient>(published[1]);
        Assert.Equal(PlanAdherence.OffPlan, confirmed.PlanAdherence);
        Assert.DoesNotContain(published, e => e is OffPlanEntryLogged);
    }

    [Fact]
    public async Task Confirming_without_an_answer_is_rejected_before_loading()
    {
        var result = await CreateService().Handle(new ConfirmEstimateCommand(5, PatientId));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(IntakeError.PlanAdherenceRequired, failure.Error);
        await _repository.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task Confirming_records_the_answer_and_publishes_it()
    {
        var entry = PhotoWithProposal();
        _repository.FindByIdAsync(5, Arg.Any<CancellationToken>()).Returns(entry);

        var result = await CreateService().Handle(new ConfirmEstimateCommand(5, PatientId, "InPlan"));

        Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result);
        Assert.True(entry.PlanAdherence.IsInPlan);
        var confirmed = Assert.IsType<EstimateConfirmedByPatient>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal(PlanAdherence.InPlan, confirmed.PlanAdherence);
    }

    [Fact]
    public async Task Adjusting_without_an_answer_is_rejected()
    {
        var result = await CreateService().Handle(new AdjustEstimateCommand(5, PatientId, PizzaId, 300m));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(IntakeError.PlanAdherenceRequired, failure.Error);
    }

    [Fact]
    public async Task Adjusting_records_the_answer_on_both_events()
    {
        var entry = PhotoWithProposal();
        _repository.FindByIdAsync(5, Arg.Any<CancellationToken>()).Returns(entry);

        var result = await CreateService().Handle(
            new AdjustEstimateCommand(5, PatientId, PizzaId, 300m, "OffPlan"));

        Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result);
        var published = Fakes.Published(_mediator);
        Assert.Equal(PlanAdherence.OffPlan, Assert.IsType<EstimateAdjustedByPatient>(published[0]).PlanAdherence);
        Assert.Equal(PlanAdherence.OffPlan, Assert.IsType<EstimateConfirmedByPatient>(published[1]).PlanAdherence);
    }

    [Fact]
    public async Task Deprecated_off_plan_endpoint_keeps_working_as_before()
    {
        var result = await CreateService().Handle(
            new LogOffPlanMealCommand(PatientId, DateTimeOffset.UtcNow.AddHours(-1)));

        var entry = Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result).Value;
        Assert.True(entry.Provenance.IsOffPlan);
        Assert.True(entry.PlanAdherence.IsOffPlan);
        Assert.False(entry.HasConfirmedEstimate);

        var published = Fakes.Published(_mediator);
        Assert.Equal(PlanAdherence.OffPlan, Assert.IsType<MealLogged>(published[0]).PlanAdherence);
        Assert.IsType<OffPlanEntryLogged>(published[1]);
    }

    private DiaryEntryCommandService CreateService()
    {
        return new DiaryEntryCommandService(_repository, _unitOfWork,
            Fakes.Catalog(Fakes.Food(PizzaId, "Pizza", 266m), Fakes.Food(12, "Ceviche", 120m)),
            new ConfigurationBuilder().Build(), NullLogger<DiaryEntryCommandService>.Instance, _mediator);
    }

    private static LogMealManuallyCommand Manual(string? answer)
    {
        return new LogMealManuallyCommand(PatientId, DateTimeOffset.UtcNow.AddHours(-1), PizzaId, 200m, answer);
    }

    private static DiaryEntry PhotoWithProposal()
    {
        var entry = new DiaryEntry(PatientId, new LocalTimestamp(DateTimeOffset.UtcNow.AddHours(-1)),
            new Provenance(Provenance.Photo), new SyncState(SyncState.Synced));
        entry.ProposeEstimate(new ProposedEstimate(12, 320m, new Confidence(0.8m), DateTimeOffset.UtcNow));
        return Identity.Assign(entry, new DiaryEntryId(5));
    }
}
