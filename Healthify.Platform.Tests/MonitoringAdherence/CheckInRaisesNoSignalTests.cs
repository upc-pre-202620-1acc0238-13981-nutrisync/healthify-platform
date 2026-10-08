using Cortex.Mediator.Notifications;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-4, ethical rule. The check in is voluntary: it raises no signal, never enters the consistency index nor
///     a deviation, and "Hard" does not escalate. The rule is kept by absence, and these tests pin the absence.
/// </summary>
public class CheckInRaisesNoSignalTests
{
    [Fact]
    public void Nothing_in_the_platform_subscribes_to_the_check_in()
    {
        var handlers = typeof(PreVisitCheckIn).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                                                   i.GetGenericTypeDefinition() == typeof(INotificationHandler<>) &&
                                                   i.GetGenericArguments()[0] == typeof(PreVisitCheckInSubmitted)))
            .ToList();

        Assert.Empty(handlers);
    }

    [Fact]
    public void The_command_service_cannot_reach_windows_deviations_or_the_consistency_index()
    {
        var dependencies = typeof(PreVisitCheckInCommandService).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.NotEmpty(dependencies);
        Assert.DoesNotContain(dependencies, n => n.Contains("EvaluationWindow") || n.Contains("Deviation") ||
                                                 n.Contains("ConsistencyIndex") || n.Contains("ReviewItem"));
    }

    [Fact]
    public void Nothing_that_evaluates_or_escalates_reads_a_check_in()
    {
        var readers = typeof(PreVisitCheckIn).Assembly.GetTypes()
            .Where(t => t.Name.Contains("Deviation") || t.Name.Contains("ConsistencyIndex") ||
                        t.Name.Contains("EvaluationWindow") || t.Name.Contains("Escalation"))
            .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType.Name.Contains("CheckIn")))
            .ToList();

        Assert.Empty(readers);
    }

    [Fact]
    public async Task A_hard_check_in_publishes_its_internal_event_and_nothing_else()
    {
        var agenda = new InMemoryScheduledFollowUps(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));
        var visit = agenda.Add(10, 20, agenda.Clock.Now.AddDays(3));

        await agenda.CheckInCommands.Handle(new SubmitPreVisitCheckInCommand(visit.Id.Value, 10, "Hard",
            ["Dinners", "Weekends", "EatingOut", "Schedules", "Cravings"]));

        Assert.IsType<PreVisitCheckInSubmitted>(Assert.Single(Fakes.Published(agenda.Mediator)));
        Assert.True(visit.IsScheduled);
    }
}
