using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     The first day of a targets snapshot is read on the patient's own clock (the offset their device declared
///     that day), with the clinical calendar as fallback, never the UTC date. Targets published at 20:00 in Lima
///     (01:00 UTC of the next day) are in force on the patient's own evening, and the repair re-evaluates the
///     logged days that were missed while the UTC date was used.
/// </summary>
public class ClinicalDayEvaluationTests
{
    private const int PatientId = 10;

    private static readonly DateOnly Monday = new(2026, 10, 19);

    /// <summary>Monday 20:00 in Lima.</summary>
    private static readonly DateTimeOffset PublishedMondayEvening = new(2026, 10, 20, 1, 0, 0, TimeSpan.Zero);

    private readonly HashSet<DateOnly> _daysWithEntries = [];
    private readonly EvaluationWindow _window;
    private readonly IEvaluationWindowRepository _repository = Substitute.For<IEvaluationWindowRepository>();
    private readonly IIntakeContextFacade _intake = Substitute.For<IIntakeContextFacade>();

    public ClinicalDayEvaluationTests()
    {
        _window = new EvaluationWindow(new OpenEvaluationWindowCommand(PatientId, 1), 7, Monday);
        Identity.Assign(_window, new WindowId(1));
        _window.TakeSnapshot(new TargetsSnapshot(1, 1800m, 119m, 195m, 60m, PublishedMondayEvening));

        _repository.FindOpenByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_window);
        _intake.GetDailyIntakeSummary(PatientId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var date = call.ArgAt<DateOnly>(1);
                var logged = _daysWithEntries.Contains(date);
                return new DailyIntakeSummaryItem(PatientId, date, logged ? 1000m : 0m, 0m, 0m, 0m,
                    logged ? 1 : 0, 0, logged);
            });
        _intake.GetDiaryEntryMoments(PatientId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var date = call.ArgAt<DateOnly>(1);
                IReadOnlyList<DiaryEntryMomentItem> moments = _declared.TryGetValue(date, out var moment)
                    ? [new DiaryEntryMomentItem(date, moment, true, "InPlan")]
                    : [];
                return moments;
            });
    }

    /// <summary>The moment each day's meal was declared on the patient's device, offset included.</summary>
    private readonly Dictionary<DateOnly, DateTimeOffset> _declared = [];

    private static IFollowUpCalendar Lima =>
        new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.FindSystemTimeZoneById("America/Lima"));

    private static IFollowUpCalendar Utc => new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc);

    [Fact]
    public void An_evening_in_Lima_is_still_that_day_although_UTC_has_turned_the_page()
    {
        Assert.Equal(Monday, Lima.LocalDateOf(PublishedMondayEvening));
        Assert.Equal(Monday, _window.TargetsSnapshots[0].EffectiveFromIn(Lima));
        Assert.Equal(Monday.AddDays(1), _window.TargetsSnapshots[0].EffectiveFromIn(Utc));
    }

    [Fact]
    public async Task Targets_published_in_the_evening_evaluate_the_meals_of_that_same_evening()
    {
        _daysWithEntries.Add(Monday);

        var result = await Service(Lima).Handle(new EvaluateDayCommand(PatientId, Monday));

        Assert.True(result.IsSuccess);
        Assert.Equal(DailyCompliance.Short, _window.DayEvaluation(Monday)?.Outcome);
    }

    [Fact]
    public async Task With_the_UTC_date_the_same_evening_had_no_targets_in_force()
    {
        _daysWithEntries.Add(Monday);

        var result = await Service(Utc).Handle(new EvaluateDayCommand(PatientId, Monday));

        Assert.Equal(MonitoringError.TargetsSnapshotMissing,
            Assert.IsType<Result<EvaluationWindow, MonitoringError>.Failure>(result).Error);
        Assert.Null(_window.DayEvaluation(Monday));
    }

    [Fact]
    public async Task The_repair_re_evaluates_only_the_logged_days_and_a_second_run_changes_nothing()
    {
        // Monday was filled in as unlogged although the diary held a meal; Wednesday was never evaluated.
        _window.RecordDayEvaluation(DailyCompliance.Evaluate(Monday, false, 0, 0m, _window.TargetsSnapshots[0]));
        _daysWithEntries.UnionWith([Monday, Monday.AddDays(2)]);
        var service = Service(Lima);

        var first = await service.Handle(new ReEvaluateLoggedDaysCommand(PatientId, Monday.AddDays(-3),
            Monday.AddDays(3)));

        Assert.Equal(2, Assert.IsType<Result<int, MonitoringError>.Success>(first).Value);
        Assert.Equal(DailyCompliance.Short, _window.DayEvaluation(Monday)?.Outcome);
        Assert.Equal(DailyCompliance.Short, _window.DayEvaluation(Monday.AddDays(2))?.Outcome);
        // A quiet day is never marked by the repair, and today is never marked ahead of time.
        Assert.Null(_window.DayEvaluation(Monday.AddDays(1)));
        Assert.Null(_window.DayEvaluation(Monday.AddDays(3)));

        var second = await service.Handle(new ReEvaluateLoggedDaysCommand(PatientId, Monday.AddDays(-3),
            Monday.AddDays(3)));

        Assert.Equal(0, Assert.IsType<Result<int, MonitoringError>.Success>(second).Value);
    }

    [Fact]
    public async Task A_patient_in_Mexico_has_the_targets_that_same_evening_whatever_the_clinical_zone()
    {
        // Published at 19:00 on Monday in Mexico City; the meal at 21:00 there is already Tuesday in UTC.
        Log(new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.FromHours(-6)));

        var result = await Service(Utc).Handle(new EvaluateDayCommand(PatientId, Monday));

        Assert.True(result.IsSuccess);
        Assert.Equal(DailyCompliance.Short, _window.DayEvaluation(Monday)?.Outcome);
    }

    [Fact]
    public async Task A_patient_in_Madrid_reads_the_publication_on_their_own_clock()
    {
        // Monday 20:00 in Lima is already 03:00 on Tuesday in Madrid.
        var tuesday = Monday.AddDays(1);
        Log(new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.FromHours(2)));
        Log(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(2)));
        var service = Service(Lima);

        // Their Monday night came before the targets existed; their Tuesday is the first day with them.
        var monday = await service.Handle(new EvaluateDayCommand(PatientId, Monday));
        var tuesdayResult = await service.Handle(new EvaluateDayCommand(PatientId, tuesday));

        Assert.Equal(MonitoringError.TargetsSnapshotMissing,
            Assert.IsType<Result<EvaluationWindow, MonitoringError>.Failure>(monday).Error);
        Assert.True(tuesdayResult.IsSuccess);
        Assert.Equal(DailyCompliance.Short, _window.DayEvaluation(tuesday)?.Outcome);
    }

    [Fact]
    public async Task The_repair_rejects_an_inverted_range()
    {
        var result = await Service(Lima).Handle(new ReEvaluateLoggedDaysCommand(PatientId, Monday, Monday.AddDays(-1)));

        Assert.Equal(MonitoringError.InvalidComplianceRange,
            Assert.IsType<Result<int, MonitoringError>.Failure>(result).Error);
    }

    private void Log(DateTimeOffset declared)
    {
        var date = DateOnly.FromDateTime(declared.DateTime);
        _daysWithEntries.Add(date);
        _declared[date] = declared;
    }

    private EvaluationWindowCommandService Service(IFollowUpCalendar calendar)
    {
        return new EvaluationWindowCommandService(_repository, _intake, Substitute.For<IUnitOfWork>(),
            new ConfigurationBuilder().Build(), NullLogger<EvaluationWindowCommandService>.Instance,
            Substitute.For<IMediator>(), calendar);
    }
}
