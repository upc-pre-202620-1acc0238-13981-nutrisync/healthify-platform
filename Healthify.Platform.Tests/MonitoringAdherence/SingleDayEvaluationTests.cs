using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
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

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-1. Every way of writing to the diary leads Monitoring to evaluate the day it touched
///     exactly once per request. The real diary command service publishes into a mediator wired to
///     the real Monitoring policies, and every evaluation of a day (normal or re-evaluation) is counted.
/// </summary>
public class SingleDayEvaluationTests
{
    private const int PatientId = 10;
    private const int CevicheId = 12;
    private const int LomoId = 15;

    private static readonly DateTimeOffset Moment = new(DateTime.UtcNow.Date.AddHours(-10), TimeSpan.Zero);
    private static readonly DateOnly Day = DateOnly.FromDateTime(Moment.Date);

    private readonly IEvaluationWindowCommandService _windows = Substitute.For<IEvaluationWindowCommandService>();
    private readonly IDiaryEntryRepository _diary = Substitute.For<IDiaryEntryRepository>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private int _nextEntryId = 1;

    public SingleDayEvaluationTests()
    {
        _windows.Handle(Arg.Any<EvaluateDayCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<EvaluationWindow, MonitoringError>.Failure(MonitoringError.DayAlreadyEvaluated));
        _windows.Handle(Arg.Any<ReEvaluateWindowCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<EvaluationWindow, MonitoringError>.Failure(MonitoringError.DayAlreadyEvaluated));
        _windows.Handle(Arg.Any<MarkUnloggedDaysBeforeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<EvaluationWindow, MonitoringError>.Failure(MonitoringError.EvaluationWindowNotFound));

        _diary.AddAsync(Arg.Do<DiaryEntry>(e => Identity.Assign(e, new DiaryEntryId(_nextEntryId++))),
            Arg.Any<CancellationToken>());
        _diary.FindByClientEntryIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DiaryEntry?)null);

        var scopes = Fakes.ScopeFactoryWith(_windows);
        Fakes.Route(_mediator, new OnMealLoggedHandler(scopes, NullLogger<OnMealLoggedHandler>.Instance));
        Fakes.Route(_mediator, new OnEstimateConfirmedByPatientHandler(scopes,
            NullLogger<OnEstimateConfirmedByPatientHandler>.Instance));
        Fakes.Route(_mediator, new OnOffPlanEntryLoggedHandler(scopes,
            NullLogger<OnOffPlanEntryLoggedHandler>.Instance));
        Fakes.Route(_mediator, new OnEntrySynchronizedHandler(scopes,
            NullLogger<OnEntrySynchronizedHandler>.Instance));
        Fakes.Route(_mediator, new OnDiaryBatchSynchronizedHandler(scopes,
            NullLogger<OnDiaryBatchSynchronizedHandler>.Instance));
    }

    [Theory]
    [InlineData(PlanAdherence.InPlan)]
    [InlineData(PlanAdherence.OffPlan)]
    public async Task Manual_log_evaluates_its_day_exactly_once(string answer)
    {
        await Diary().Handle(new LogMealManuallyCommand(PatientId, Moment, CevicheId, 280m, answer));

        Assert.Equal(1, EvaluationsOf(Day));
    }

    [Fact]
    public async Task Photo_log_confirmed_as_proposed_evaluates_its_day_exactly_once()
    {
        await Diary().Handle(PhotoLog(new PhotoConfirmation(PhotoConfirmation.AsProposed, null, null)));

        Assert.Equal(1, EvaluationsOf(Day));
    }

    [Fact]
    public async Task Photo_log_adjusted_evaluates_its_day_exactly_once()
    {
        await Diary().Handle(PhotoLog(new PhotoConfirmation(PhotoConfirmation.Adjusted, LomoId, 300m)));

        Assert.Equal(1, EvaluationsOf(Day));
    }

    [Fact]
    public async Task Photo_log_without_confirmation_evaluates_its_day_exactly_once()
    {
        await Diary().Handle(PhotoLog(null));

        Assert.Equal(1, EvaluationsOf(Day));
    }

    [Fact]
    public async Task Synchronization_of_several_entries_of_one_day_evaluates_it_exactly_once()
    {
        PendingDiaryEntry[] batch =
        [
            new(Guid.NewGuid(), Moment, Provenance.Manual, null, CevicheId, 280m, null, true, "InPlan"),
            new(Guid.NewGuid(), Moment.AddHours(3), Provenance.Photo, "p", LomoId, 300m, 0.7m, false, null),
            new(Guid.NewGuid(), Moment.AddHours(5), Provenance.OffPlan, null, null, null, null),
            new(Guid.NewGuid(), Moment.AddHours(6), Provenance.Manual, null, LomoId, 150m, null, null, null)
        ];

        await Diary().Handle(new SyncPendingEntriesCommand(PatientId, batch));

        Assert.Equal(1, EvaluationsOf(Day));
    }

    [Fact]
    public async Task Synchronization_spanning_two_days_evaluates_each_day_exactly_once()
    {
        PendingDiaryEntry[] batch =
        [
            new(Guid.NewGuid(), Moment.AddDays(-1), Provenance.Manual, null, CevicheId, 280m, null, true, "InPlan"),
            new(Guid.NewGuid(), Moment.AddDays(-1).AddHours(2), Provenance.Manual, null, LomoId, 100m, null, true,
                "OffPlan"),
            new(Guid.NewGuid(), Moment, Provenance.Manual, null, CevicheId, 200m, null, true, "InPlan")
        ];

        await Diary().Handle(new SyncPendingEntriesCommand(PatientId, batch));

        Assert.Equal(1, EvaluationsOf(Day.AddDays(-1)));
        Assert.Equal(1, EvaluationsOf(Day));
    }

    private int EvaluationsOf(DateOnly day)
    {
        var calls = _windows.ReceivedCalls().Select(c => c.GetArguments()[0]).ToList();
        return calls.OfType<EvaluateDayCommand>().Count(c => c.PatientId == PatientId && c.Date == day)
               + calls.OfType<ReEvaluateWindowCommand>().Count(c => c.PatientId == PatientId && c.Date == day);
    }

    private DiaryEntryCommandService Diary()
    {
        return new DiaryEntryCommandService(_diary, Substitute.For<IUnitOfWork>(),
            Fakes.Catalog(Fakes.Food(CevicheId, "Ceviche", 120m), Fakes.Food(LomoId, "Lomo saltado", 180m)),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Intake:RetroactiveLoggingWindowHours"] = "0" }).Build(),
            NullLogger<DiaryEntryCommandService>.Instance, _mediator);
    }

    private static LogMealByPhotoCommand PhotoLog(PhotoConfirmation? confirmation)
    {
        return new LogMealByPhotoCommand(PatientId, Moment, "photo-ref", CevicheId, 320m, 0.8m, confirmation,
            confirmation is null ? null : PlanAdherence.InPlan);
    }
}
