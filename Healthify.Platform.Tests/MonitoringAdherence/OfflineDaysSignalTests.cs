using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-1. A patient logs day 1 online, spends days 2 to 4 offline, and synchronises on day 5 with
///     entries of day 5 only. The signals must come out exactly as if days 2 to 4 had been marked
///     Unlogged by a live evaluation, and the series must show those days as Unlogged.
/// </summary>
public class OfflineDaysSignalTests
{
    private const int PatientId = 10;

    private enum Path
    {
        /// <summary>Day 5 evaluated live: days 2 to 4 are marked Unlogged. The reference.</summary>
        LiveReference,

        /// <summary>Day 5 arrives in a synchronised batch, through the real batch policy.</summary>
        Synchronized,

        /// <summary>Day 5 only re-evaluated, with nothing marking days 2 to 4 (before this fix).</summary>
        ReEvaluationOnly
    }

    [Theory]
    [InlineData(2)] // day 1 inside the 7-day horizon: a sustained deviation is detected
    [InlineData(3)] // three days since the last logged day: a logging gap is detected
    public async Task Synchronized_days_give_the_same_signals_as_days_marked_unlogged(int daysSinceDay5)
    {
        var reference = await Run(Path.LiveReference, daysSinceDay5);
        var synced = await Run(Path.Synchronized, daysSinceDay5);
        var reEvaluatedOnly = await Run(Path.ReEvaluationOnly, daysSinceDay5);

        Assert.Equal(reference.Signals, synced.Signals);
        // Point 1 of the review: a day never evaluated and an Unlogged day already produced the same
        // signals; the difference was only in the series that is shown.
        Assert.Equal(reference.Signals, reEvaluatedOnly.Signals);

        if (daysSinceDay5 == 2) Assert.True(reference.Signals.Sustained);
        if (daysSinceDay5 == 3) Assert.True(reference.Signals.GapFlagged);
    }

    [Fact]
    public async Task The_batch_marks_the_offline_days_unlogged_and_leaves_day_1_untouched()
    {
        var reference = await Run(Path.LiveReference, 2);
        var synced = await Run(Path.Synchronized, 2);
        var reEvaluatedOnly = await Run(Path.ReEvaluationOnly, 2);

        Assert.Equal(reference.Outcomes, synced.Outcomes);
        Assert.Equal([DailyCompliance.Unlogged, DailyCompliance.Unlogged, DailyCompliance.Unlogged],
            synced.Outcomes.Where(o => o.Day is >= 2 and <= 4).Select(o => o.Outcome));
        Assert.DoesNotContain(reEvaluatedOnly.Outcomes, o => o.Day is >= 2 and <= 4);

        Assert.Equal(DailyCompliance.Short, synced.Outcomes.Single(o => o.Day == 1).Outcome);
        Assert.Same(synced.Day1BeforeSync, synced.Day1AfterSync);
    }

    [Fact]
    public async Task Marking_twice_changes_nothing_the_second_time()
    {
        var run = await Run(Path.Synchronized, 2);

        var again = await run.Windows.Handle(new MarkUnloggedDaysBeforeCommand(PatientId, run.Day5));

        Assert.True(again.IsSuccess);
        Assert.Empty(Fakes.Published(run.Mediator));
    }

    private static async Task<RunResult> Run(Path path, int daysSinceDay5)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var day5 = today.AddDays(-daysSinceDay5);
        var day1 = day5.AddDays(-4);
        var from = day1.AddDays(-5);

        var window = new EvaluationWindow(new OpenEvaluationWindowCommand(PatientId, 1), 7, from);
        Identity.Assign(window, new WindowId(1));
        window.TakeSnapshot(new TargetsSnapshot(1, 1800m, 119m, 195m, 60m,
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));

        var windowRepository = Substitute.For<IEvaluationWindowRepository>();
        windowRepository.FindOpenByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(window);

        // Day 1 and day 5 hold entries that fell short of the target; nothing else has any.
        var intake = Substitute.For<IIntakeContextFacade>();
        intake.GetDailyIntakeSummary(PatientId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var date = call.ArgAt<DateOnly>(1);
                var logged = date == day1 || date == day5;
                return new DailyIntakeSummaryItem(PatientId, date, logged ? 1000m : 0m, 0m, 0m, 0m,
                    logged ? 2 : 0, 0, logged);
            });

        var mediator = Substitute.For<IMediator>();
        var windows = new EvaluationWindowCommandService(windowRepository, intake, Substitute.For<IUnitOfWork>(),
            new ConfigurationBuilder().Build(), NullLogger<EvaluationWindowCommandService>.Instance, mediator);

        // Day 1, online.
        await windows.Handle(new EvaluateDayCommand(PatientId, day1));
        var day1BeforeSync = window.DayEvaluation(day1);

        // Day 5.
        switch (path)
        {
            case Path.LiveReference:
                await windows.Handle(new EvaluateDayCommand(PatientId, day5));
                break;
            case Path.Synchronized:
                await new OnDiaryBatchSynchronizedHandler(Fakes.ScopeFactoryWith<IEvaluationWindowCommandService>(windows),
                        NullLogger<OnDiaryBatchSynchronizedHandler>.Instance)
                    .Handle(new DiaryBatchSynchronized(PatientId, [day5]), CancellationToken.None);
                break;
            case Path.ReEvaluationOnly:
                await windows.Handle(new ReEvaluateWindowCommand(PatientId, day5));
                break;
        }

        var outcomes = window.DailyComplianceSeries
            .Select(d => (Day: d.Date.DayNumber - day1.DayNumber + 1, d.Outcome))
            .ToList();
        var signals = await Signals(windowRepository, windows);
        mediator.ClearReceivedCalls();

        return new RunResult(signals, outcomes, day1BeforeSync, window.DayEvaluation(day1), windows, mediator, day5);
    }

    /// <summary>Runs the real deviation and logging-gap detectors over the window as it stands.</summary>
    private static async Task<SignalResult> Signals(IEvaluationWindowRepository windowRepository,
        EvaluationWindowCommandService windows)
    {
        var deviationRepository = Substitute.For<IDeviationRepository>();
        Deviation? stored = null;
        await deviationRepository.AddAsync(Arg.Do<Deviation>(d => stored = Identity.Assign(d, new DeviationId(1))),
            Arg.Any<CancellationToken>());
        deviationRepository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_ => stored);

        var mediator = Substitute.For<IMediator>();
        var deviations = new DeviationCommandService(deviationRepository, windowRepository,
            Substitute.For<IUnitOfWork>(), new ConfigurationBuilder().Build(),
            NullLogger<DeviationCommandService>.Instance, mediator);

        var detected = await deviations.Handle(new DetectDeviationCommand(PatientId));
        if (detected.IsSuccess) await deviations.Handle(new FlagSustainedDeviationCommand(1));

        await windows.Handle(new FlagLoggingGapCommand(PatientId, DateOnly.FromDateTime(DateTime.UtcNow)));

        var published = Fakes.Published(mediator);
        return new SignalResult(
            detected.IsSuccess,
            stored?.LoggedDaysConsidered,
            stored?.DeviatingDaysConsidered,
            stored?.Direction.Value,
            published.OfType<SustainedDeviationDetected>().Any(),
            windowRepository.FindOpenByPatientIdAsync(PatientId).Result!.LastLoggingGapFlaggedOn is not null);
    }

    private sealed record SignalResult(
        bool DeviationDetected,
        int? LoggedDaysConsidered,
        int? DeviatingDaysConsidered,
        string? Direction,
        bool Sustained,
        bool GapFlagged);

    private sealed record RunResult(
        SignalResult Signals,
        List<(int Day, string Outcome)> Outcomes,
        DailyCompliance? Day1BeforeSync,
        DailyCompliance? Day1AfterSync,
        EvaluationWindowCommandService Windows,
        IMediator Mediator,
        DateOnly Day5);
}
