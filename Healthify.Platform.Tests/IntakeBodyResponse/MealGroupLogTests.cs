using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-6 (§12-#4, option A). «Registrar esta comida»: one confirmed Manual entry per food, sharing a meal group
///     and the moment, in one save, and the day evaluated once by the real Monitoring policies.
/// </summary>
public class MealGroupLogTests
{
    private const int PatientId = 10;
    private const int PolloId = 231;
    private const int CamoteId = 3;
    private const int LechugaId = 9;

    private static readonly DateTimeOffset Moment = new(DateTime.UtcNow.Date.AddHours(-10), TimeSpan.Zero);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Moment.Date);

    private readonly IEvaluationWindowCommandService _windows = Substitute.For<IEvaluationWindowCommandService>();
    private readonly IDiaryEntryRepository _diary = Substitute.For<IDiaryEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly List<DiaryEntry> _added = [];
    private int _nextEntryId = 1;

    public MealGroupLogTests()
    {
        _windows.Handle(Arg.Any<EvaluateDayCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<EvaluationWindow, MonitoringError>.Failure(MonitoringError.DayAlreadyEvaluated));
        _diary.AddAsync(Arg.Do<DiaryEntry>(e =>
        {
            Identity.Assign(e, new DiaryEntryId(_nextEntryId++));
            _added.Add(e);
        }), Arg.Any<CancellationToken>());

        var scopes = Fakes.ScopeFactoryWith(_windows);
        Fakes.Route(_mediator, new OnMealLoggedHandler(scopes, NullLogger<OnMealLoggedHandler>.Instance));
        Fakes.Route(_mediator, new OnEstimateConfirmedByPatientHandler(scopes,
            NullLogger<OnEstimateConfirmedByPatientHandler>.Instance));
        Fakes.Route(_mediator, new OnMealGroupLoggedHandler(scopes, NullLogger<OnMealGroupLoggedHandler>.Instance));
    }

    [Fact]
    public async Task The_batch_evaluates_the_day_exactly_once()
    {
        var result = await Diary().Handle(IdeaLog());

        Assert.Equal(3, Success(result).Entries.Count);
        var evaluations = _windows.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<EvaluateDayCommand>()
            .ToList();
        var evaluation = Assert.Single(evaluations);
        Assert.Equal((PatientId, Day), (evaluation.PatientId, evaluation.Date));
    }

    [Fact]
    public async Task An_idea_becomes_one_confirmed_manual_entry_per_food_in_one_group_and_one_save()
    {
        var outcome = Success(await Diary().Handle(IdeaLog()));

        Assert.Equal([PolloId, CamoteId, LechugaId], outcome.Entries.Select(e => e.ConfirmedReferenceFoodId!.Value));
        Assert.All(outcome.Entries, e =>
        {
            Assert.Equal(outcome.MealGroupId, e.MealGroupId);
            Assert.Equal(EntryOrigin.MealIdea, e.Origin?.Value);
            Assert.True(e.Provenance.IsManual);
            Assert.True(e.HasConfirmedEstimate);
            Assert.Equal(PlanAdherence.InPlan, e.PlanAdherence.Value);
            Assert.Equal(Moment, e.DeclaredLocalTimestamp);
        });
        Assert.NotEqual(Guid.Empty, outcome.MealGroupId);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_entry_announces_itself_and_leaves_the_day_to_the_group_event()
    {
        var outcome = Success(await Diary().Handle(IdeaLog()));

        var published = Fakes.Published(_mediator);
        Assert.Equal(3, published.OfType<MealLogged>().Count(m => m.ConfirmedOnCreation));
        Assert.All(published.OfType<EstimateConfirmedByPatient>(), c => Assert.False(c.EvaluatesDay));
        var group = Assert.IsType<MealGroupLogged>(published.Last());
        Assert.Equal(outcome.MealGroupId, group.MealGroupId);
        Assert.Equal(outcome.Entries.Select(e => e.Id.Value), group.DiaryEntryIds);
        Assert.Equal(EntryOrigin.MealIdea, group.Origin);
    }

    [Fact]
    public async Task An_explicit_answer_of_the_patient_is_kept_for_an_idea()
    {
        var outcome = Success(await Diary().Handle(IdeaLog(PlanAdherence.OffPlan)));

        Assert.All(outcome.Entries, e => Assert.True(e.PlanAdherence.IsOffPlan));
    }

    [Fact]
    public async Task A_group_that_is_not_an_idea_must_answer_whether_it_was_in_the_plan()
    {
        var command = new LogMealGroupManuallyCommand(PatientId, Moment, null, null,
            [new MealGroupItem(PolloId, 150m), new MealGroupItem(CamoteId, 120m)]);

        AssertFailure(await Diary().Handle(command), IntakeError.PlanAdherenceRequired);
    }

    [Fact]
    public async Task A_food_the_catalog_cannot_resolve_leaves_nothing_stored()
    {
        var command = new LogMealGroupManuallyCommand(PatientId, Moment, null,
            new MealGroupOrigin(EntryOrigin.MealIdea, "991-1"),
            [new MealGroupItem(PolloId, 150m), new MealGroupItem(999, 100m)]);

        AssertFailure(await Diary().Handle(command), IntakeError.ReferenceFoodNotResolved);
        Assert.Empty(_added);
        await _unitOfWork.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task A_group_needs_one_to_ten_items(int count)
    {
        var items = Enumerable.Range(0, count).Select(_ => new MealGroupItem(PolloId, 50m)).ToList();

        AssertFailure(await Diary().Handle(new LogMealGroupManuallyCommand(PatientId, Moment, PlanAdherence.InPlan,
            null, items)), IntakeError.InvalidMealGroupItems);
    }

    [Fact]
    public async Task Every_item_needs_a_positive_portion_and_the_origin_must_be_known()
    {
        AssertFailure(await Diary().Handle(new LogMealGroupManuallyCommand(PatientId, Moment, PlanAdherence.InPlan,
            null, [new MealGroupItem(PolloId, 0m)])), IntakeError.InvalidMealGroupItems);
        AssertFailure(await Diary().Handle(new LogMealGroupManuallyCommand(PatientId, Moment, PlanAdherence.InPlan,
            new MealGroupOrigin("Restaurant", null), [new MealGroupItem(PolloId, 150m)])),
            IntakeError.InvalidEntryOrigin);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task The_retroactive_window_applies_as_to_any_interactive_log()
    {
        var command = new LogMealGroupManuallyCommand(PatientId, DateTimeOffset.UtcNow.AddDays(-5), null,
            new MealGroupOrigin(EntryOrigin.MealIdea, null), [new MealGroupItem(PolloId, 150m)]);

        AssertFailure(await Diary(windowHours: 48).Handle(command), IntakeError.RetroactiveLoggingWindowExceeded);
    }

    [Fact]
    public void The_diary_resource_shows_the_group_and_its_origin_and_a_single_entry_shows_none()
    {
        var grouped = new DiaryEntry(PatientId, new LocalTimestamp(Moment), new Provenance(Provenance.Manual),
            new SyncState(SyncState.Synced));
        var groupId = Guid.NewGuid();
        grouped.JoinMealGroup(groupId, new EntryOrigin(EntryOrigin.MealIdea));
        Identity.Assign(grouped, new DiaryEntryId(1));
        var alone = Identity.Assign(new DiaryEntry(PatientId, new LocalTimestamp(Moment),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Synced)), new DiaryEntryId(2));

        var resource = DiaryEntryResourceAssembler.ToResource(grouped);
        Assert.Equal(groupId, resource.MealGroupId);
        Assert.Equal("MealIdea", resource.Origin);
        Assert.Null(DiaryEntryResourceAssembler.ToResource(alone).MealGroupId);
        Assert.Throws<InvalidOperationException>(() => grouped.JoinMealGroup(Guid.NewGuid(), null));
    }

    private DiaryEntryCommandService Diary(int windowHours = 0)
    {
        return new DiaryEntryCommandService(_diary, _unitOfWork,
            Fakes.Catalog(Fakes.Food(PolloId, "Pechuga de pollo sin piel cocida", 165m),
                Fakes.Food(CamoteId, "Camote amarillo sancochado", 90m), Fakes.Food(LechugaId, "Lechuga", 15m)),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Intake:RetroactiveLoggingWindowHours"] = windowHours.ToString() }).Build(),
            NullLogger<DiaryEntryCommandService>.Instance, _mediator);
    }

    private static LogMealGroupManuallyCommand IdeaLog(string? answer = null)
    {
        return new LogMealGroupManuallyCommand(PatientId, Moment, answer,
            new MealGroupOrigin(EntryOrigin.MealIdea, "991-1"),
            [new MealGroupItem(PolloId, 150m), new MealGroupItem(CamoteId, 120m), new MealGroupItem(LechugaId, 100m)]);
    }

    private static MealGroupLogOutcome Success(Result<MealGroupLogOutcome, IntakeError> result)
    {
        return Assert.IsType<Result<MealGroupLogOutcome, IntakeError>.Success>(result).Value;
    }

    private static void AssertFailure(Result<MealGroupLogOutcome, IntakeError> result, IntakeError expected)
    {
        Assert.Equal(expected, Assert.IsType<Result<MealGroupLogOutcome, IntakeError>.Failure>(result).Error);
    }
}
