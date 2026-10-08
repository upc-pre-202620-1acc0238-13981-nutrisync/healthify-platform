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
///     MA-1. A synchronised batch marks Unlogged the empty days before its most recent day, gaps
///     between its own days included, and a later batch that brings one of those days re-evaluates it
///     once.
/// </summary>
public class BatchGapDaysTests
{
    private const int PatientId = 10;

    private readonly DateOnly _day4;
    private readonly HashSet<DateOnly> _daysWithEntries = [];
    private readonly EvaluationWindow _window;
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly EvaluationWindowCommandService _windows;

    public BatchGapDaysTests()
    {
        // Day 7 is yesterday; the window opened three days before day 1.
        _day4 = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-4);
        var from = Day(1).AddDays(-3);

        _window = new EvaluationWindow(new OpenEvaluationWindowCommand(PatientId, 1), 7, from);
        Identity.Assign(_window, new WindowId(1));
        _window.TakeSnapshot(new TargetsSnapshot(1, 1800m, 119m, 195m, 60m,
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));

        var repository = Substitute.For<IEvaluationWindowRepository>();
        repository.FindOpenByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_window);

        var intake = Substitute.For<IIntakeContextFacade>();
        intake.GetDailyIntakeSummary(PatientId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var date = call.ArgAt<DateOnly>(1);
                var logged = _daysWithEntries.Contains(date);
                return new DailyIntakeSummaryItem(PatientId, date, logged ? 1000m : 0m, 0m, 0m, 0m,
                    logged ? 1 : 0, 0, logged);
            });

        _windows = new EvaluationWindowCommandService(repository, intake, Substitute.For<IUnitOfWork>(),
            new ConfigurationBuilder().Build(), NullLogger<EvaluationWindowCommandService>.Instance, _mediator);
    }

    [Fact]
    public async Task A_gap_between_batch_days_is_marked_unlogged_and_a_later_entry_re_evaluates_it_once()
    {
        // Day 4, online.
        _daysWithEntries.Add(Day(4));
        await _windows.Handle(new EvaluateDayCommand(PatientId, Day(4)));
        _mediator.ClearReceivedCalls();

        // First batch: days 5 and 7. Day 6 has nothing.
        _daysWithEntries.UnionWith([Day(5), Day(7)]);
        await Synchronize(Day(5), Day(7));

        Assert.Equal(DailyCompliance.Short, Outcome(5));
        Assert.Equal(DailyCompliance.Unlogged, Outcome(6));
        Assert.Equal(DailyCompliance.Short, Outcome(7));
        Assert.Equal(DailyCompliance.Short, Outcome(4));

        // The batch's own days are evaluated before the marking, so they never pass through Unlogged.
        var firstBatch = Fakes.Published(_mediator).OfType<DailyComplianceComputed>().ToList();
        Assert.DoesNotContain(firstBatch, e => e.Date != Day(6) && e.Outcome == DailyCompliance.Unlogged);
        Assert.Single(firstBatch, e => e.Date == Day(6));
        _mediator.ClearReceivedCalls();

        // Second batch: an entry of day 6 that had stayed in another device queue.
        _daysWithEntries.Add(Day(6));
        await Synchronize(Day(6));

        Assert.Equal(DailyCompliance.Short, Outcome(6));
        Assert.Single(_window.DailyComplianceSeries, d => d.Date == Day(6));

        var secondBatch = Fakes.Published(_mediator);
        Assert.Single(secondBatch.OfType<WindowReEvaluated>(), e => e.Date == Day(6));
        Assert.Single(secondBatch.OfType<DayEvaluated>(), e => e.Date == Day(6));
        Assert.Single(secondBatch.OfType<DailyComplianceComputed>(), e => e.Date == Day(6));
        Assert.Equal(3, secondBatch.Count);

        // Nothing else moved.
        Assert.Equal(DailyCompliance.Short, Outcome(5));
        Assert.Equal(DailyCompliance.Short, Outcome(7));
    }

    [Fact]
    public async Task Days_before_the_oldest_batch_day_are_marked_too()
    {
        _daysWithEntries.Add(Day(1));
        await _windows.Handle(new EvaluateDayCommand(PatientId, Day(1)));

        _daysWithEntries.UnionWith([Day(5), Day(7)]);
        await Synchronize(Day(5), Day(7));

        Assert.All([2, 3, 4, 6], n => Assert.Equal(DailyCompliance.Unlogged, Outcome(n)));
        Assert.Equal(DailyCompliance.Short, Outcome(1));
    }

    private DateOnly Day(int n)
    {
        return _day4.AddDays(n - 4);
    }

    private string? Outcome(int n)
    {
        return _window.DayEvaluation(Day(n))?.Outcome;
    }

    private Task Synchronize(params DateOnly[] days)
    {
        return new OnDiaryBatchSynchronizedHandler(Fakes.ScopeFactoryWith<IEvaluationWindowCommandService>(_windows),
                NullLogger<OnDiaryBatchSynchronizedHandler>.Instance)
            .Handle(new DiaryBatchSynchronized(PatientId, days), CancellationToken.None);
    }
}
