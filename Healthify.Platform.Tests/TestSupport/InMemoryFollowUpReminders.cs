using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.Shared.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     The appointment reminder policy with the real command service, planner and agenda over in-memory
///     repositories. The only fakes are the edges: accounts (Iam), the mail notifier and the unit of work. Nothing
///     here can send a real email.
/// </summary>
public sealed class InMemoryFollowUpReminders
{
    public const int PatientId = 10;
    public const int PractitionerId = 20;
    public const string PatientEmail = "ana@example.com";
    public const string PractitionerEmail = "marta@example.com";

    private static readonly TimeZoneInfo PracticeZone =
        TimeZoneInfo.CreateCustomTimeZone("practice", TimeSpan.FromHours(-5), "practice", "practice");

    public InMemoryFollowUpReminders(params (string Key, string? Value)[] configuration)
    {
        Agenda = new InMemoryScheduledFollowUps(new ClinicalTimeZoneFollowUpCalendar(PracticeZone));
        Repository = new ReminderRepository(Agenda.Rows, Logs);
        Clock = new ClinicalTimeZoneFollowUpCalendar(PracticeZone);

        Iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var ids = call.Arg<IEnumerable<int>>().ToHashSet();
            return (IReadOnlyDictionary<int, UserIdentityItem>)Accounts.Where(a => ids.Contains(a.Key))
                .ToDictionary(a => a.Key, a => a.Value);
        });
        Iam.GetPreferredLanguage(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Languages.TryGetValue(call.Arg<int>(), out var language) ? language : null);

        Notifier.IsConfigured.Returns(true);
        Notifier.SendAsync(Arg.Any<FollowUpReminderMessage>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (FailNextDeliveries > 0)
            {
                FailNextDeliveries--;
                throw new InvalidOperationException("The mail server refused the message.");
            }

            Sent.Add(call.Arg<FollowUpReminderMessage>());
            return true;
        });

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration.Select(c =>
                new KeyValuePair<string, string?>($"Reminders:FollowUp:{c.Key}", c.Value)))
            .Build();
        Service = new FollowUpReminderCommandService(Repository, Iam, Notifier, Clock, UnitOfWork, Configuration,
            NullLogger<FollowUpReminderCommandService>.Instance);
    }

    public InMemoryScheduledFollowUps Agenda { get; }
    public IFollowUpReminderRepository Repository { get; }
    public ClinicalTimeZoneFollowUpCalendar Clock { get; }
    public IConfiguration Configuration { get; }
    public IFollowUpReminderCommandService Service { get; }
    public IIamContextFacade Iam { get; } = Substitute.For<IIamContextFacade>();
    public IFollowUpReminderNotifier Notifier { get; } = Substitute.For<IFollowUpReminderNotifier>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    /// <summary>Every delivered reminder, in order.</summary>
    public List<FollowUpReminderMessage> Sent { get; } = [];

    /// <summary>The recorded reminders.</summary>
    public List<FollowUpReminderLog> Logs { get; } = [];

    /// <summary>How many of the next deliveries throw, as an SMTP outage would.</summary>
    public int FailNextDeliveries { get; set; }

    public Dictionary<int, UserIdentityItem> Accounts { get; } = new()
    {
        [PatientId] = new UserIdentityItem(PatientId, PatientEmail, "Patient", "Ana", "Flores"),
        [PractitionerId] = new UserIdentityItem(PractitionerId, PractitionerEmail, "Practitioner", "Marta", "Quispe")
    };

    public Dictionary<int, string> Languages { get; } = new() { [PatientId] = "es", [PractitionerId] = "es" };

    /// <summary>A visit on the agenda at <paramref name="scheduledFor" />, booked long ago.</summary>
    public ScheduledFollowUp Visit(DateTimeOffset scheduledFor, int patientId = PatientId, params string[] preparation)
    {
        return Agenda.Add(patientId, PractitionerId, scheduledFor, preparation);
    }

    /// <summary>One cycle of the policy at <paramref name="now" />.</summary>
    public Task<FollowUpReminderCycleResult> RunAt(DateTimeOffset now)
    {
        return Service.Handle(new SendDueFollowUpRemindersCommand(now));
    }

    private sealed class ReminderRepository(List<ScheduledFollowUp> visits, List<FollowUpReminderLog> logs)
        : IFollowUpReminderRepository
    {
        public Task<IReadOnlyList<ScheduledFollowUp>> ListScheduledBetweenAsync(DateTimeOffset after,
            DateTimeOffset until, int batchSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ScheduledFollowUp>>(visits
                .Where(v => v.IsScheduled && v.ScheduledFor > after && v.ScheduledFor <= until)
                .OrderBy(v => v.ScheduledFor).Take(batchSize).ToList());
        }

        public Task<IReadOnlyList<FollowUpReminderLog>> ListLogsAsync(IReadOnlyCollection<int> followUpIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FollowUpReminderLog>>(
                logs.Where(l => followUpIds.Contains(l.FollowUpId)).ToList());
        }

        public Task AddLogAsync(FollowUpReminderLog log, CancellationToken cancellationToken = default)
        {
            logs.Add(log);
            return Task.CompletedTask;
        }
    }
}
