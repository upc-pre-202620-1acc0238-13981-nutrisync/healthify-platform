using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Mailing;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;
using Healthify.Platform.Shared.Domain.Services;
using Healthify.Platform.Shared.Infrastructure.Mailing;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Healthify.Platform.Tests.TestSupport.InMemoryFollowUpReminders;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     The edges of the reminder policy: the SMTP sender (never reaches a network here), the notifier that renders
///     the template, and the background worker that runs the cycle. Mail is always a substitute.
/// </summary>
public class FollowUpReminderDeliveryTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
    }

    // ---------------------------------------------------------------- the SMTP sender

    [Theory]
    [InlineData(null, null)]
    [InlineData("smtp.example.com", null)]
    [InlineData(null, "no-reply@healthify.test")]
    [InlineData("%SMTP_HOST%", "%SMTP_FROM%")]
    [InlineData("", "")]
    public async Task Without_a_host_and_a_sender_address_nothing_is_sent_and_nothing_throws(string? host,
        string? from)
    {
        var sender = new SmtpMailSender(Config(("Smtp:Host", host), ("Smtp:From", from)),
            NullLogger<SmtpMailSender>.Instance);

        Assert.False(sender.IsConfigured);
        Assert.False(await sender.SendHtmlAsync("ana@example.com", "Ana", "Asunto", "<p>Hola</p>"));
    }

    [Fact]
    public void With_a_host_and_a_sender_address_it_is_configured()
    {
        var sender = new SmtpMailSender(
            Config(("Smtp:Host", "smtp.example.com"), ("Smtp:From", "no-reply@healthify.test")),
            NullLogger<SmtpMailSender>.Instance);

        Assert.True(sender.IsConfigured);
    }

    // ---------------------------------------------------------------- the notifier

    private static FollowUpReminderMessage Message(string modality = ConsultationModality.InPerson)
    {
        return new FollowUpReminderMessage(ReminderRecipient.Patient, "ana@example.com",
            "Ana Flores", "es", "Ana Flores", "Marta Quispe", new DateTimeOffset(2026, 11, 20, 10, 0, 0,
                TimeSpan.FromHours(-5)), modality, [PreparationInstruction.Fasting], 1440, false);
    }

    [Fact]
    public async Task The_notifier_renders_the_template_and_hands_it_to_the_mail_sender()
    {
        var mail = Substitute.For<IMailSender>();
        mail.IsConfigured.Returns(true);
        mail.SendHtmlAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(true);
        var notifier = new SmtpFollowUpReminderNotifier(mail,
            Config(("Reminders:FollowUp:PracticeAddress", "Av. Primavera 123")));

        var sent = await notifier.SendAsync(Message());

        Assert.True(sent);
        Assert.True(notifier.IsConfigured);
        await mail.Received(1).SendHtmlAsync("ana@example.com", "Ana Flores",
            Arg.Is<string>(s => s.Contains("viernes 20 de noviembre, 10:00")),
            Arg.Is<string>(h => h.Contains("Av. Primavera 123") && h.Contains("Ven en ayunas")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_notifier_reports_that_nothing_was_sent_when_the_sender_is_not_configured()
    {
        var mail = Substitute.For<IMailSender>();
        mail.IsConfigured.Returns(false);
        mail.SendHtmlAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(false);
        var notifier = new SmtpFollowUpReminderNotifier(mail, Config());

        Assert.False(notifier.IsConfigured);
        Assert.False(await notifier.SendAsync(Message()));
    }

    [Fact]
    public async Task A_refusing_mail_server_surfaces_to_the_policy_which_does_not_record_the_reminder()
    {
        var mail = Substitute.For<IMailSender>();
        mail.IsConfigured.Returns(true);
        mail.SendHtmlAsync(default!, default, default!, default!, default).ThrowsAsyncForAnyArgs(
            new InvalidOperationException("550 mailbox unavailable"));
        var notifier = new SmtpFollowUpReminderNotifier(mail, Config());

        await Assert.ThrowsAsync<InvalidOperationException>(() => notifier.SendAsync(Message()));
    }

    // ---------------------------------------------------------------- the real policy through the real notifier

    [Fact]
    public async Task The_whole_policy_with_the_real_notifier_sends_one_rendered_email_per_reminder_through_a_mock_mailer()
    {
        var now = DateTimeOffset.UtcNow;
        var mail = Substitute.For<IMailSender>();
        mail.IsConfigured.Returns(true);
        mail.SendHtmlAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(true);
        var reminders = new InMemoryFollowUpReminders(("NotifyPractitioner", "true"));
        var service = new Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices
            .FollowUpReminderCommandService(reminders.Repository, reminders.Iam,
                new SmtpFollowUpReminderNotifier(mail, reminders.Configuration), reminders.Clock,
                reminders.UnitOfWork, reminders.Configuration,
                NullLogger<Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices.FollowUpReminderCommandService>.Instance);
        reminders.Visit(now.AddHours(20));

        var first = await service.Handle(new SendDueFollowUpRemindersCommand(now));
        var second = await service.Handle(new SendDueFollowUpRemindersCommand(now.AddMinutes(10)));

        Assert.Equal(2, first.Sent);
        Assert.Equal(0, second.Sent);
        await mail.Received(1).SendHtmlAsync(PatientEmail, "Ana Flores",
            Arg.Is<string>(s => s.StartsWith("Recordatorio: tu consulta")), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await mail.Received(1).SendHtmlAsync(PractitionerEmail, "Marta Quispe",
            Arg.Is<string>(s => s.StartsWith("Consulta con Ana Flores")), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(2, reminders.Logs.Count);
    }

    // ---------------------------------------------------------------- the background worker

    private sealed class SignallingService(IFollowUpReminderCommandService inner, TaskCompletionSource called)
        : IFollowUpReminderCommandService
    {
        public int Calls { get; private set; }

        public async Task<FollowUpReminderCycleResult> Handle(SendDueFollowUpRemindersCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            try
            {
                return await inner.Handle(command, cancellationToken);
            }
            finally
            {
                called.TrySetResult();
            }
        }
    }

    private static async Task RunWorkerOnceAsync(IFollowUpReminderCommandService service,
        TaskCompletionSource called, params (string Key, string? Value)[] configuration)
    {
        var worker = new FollowUpReminderHostedService(
            Fakes.ScopeFactoryWith<IFollowUpReminderCommandService>(service),
            Config(configuration), NullLogger<FollowUpReminderHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_worker_runs_a_cycle_as_soon_as_it_starts_and_stops_cleanly()
    {
        var now = DateTimeOffset.UtcNow;
        var reminders = new InMemoryFollowUpReminders();
        reminders.Visit(now.AddHours(20));
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new SignallingService(reminders.Service, called);

        await RunWorkerOnceAsync(service, called);

        Assert.Equal(1, service.Calls);
        Assert.Single(reminders.Sent);
    }

    [Fact]
    public async Task A_cycle_that_throws_does_not_bring_the_worker_down()
    {
        var failing = Substitute.For<IFollowUpReminderCommandService>();
        failing.Handle(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new SignallingService(failing, called);

        await RunWorkerOnceAsync(service, called);

        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_disabled_worker_never_opens_a_scope()
    {
        var factory = Substitute.For<IServiceScopeFactory>();
        var worker = new FollowUpReminderHostedService(factory, Config(("Reminders:FollowUp:Enabled", "false")),
            NullLogger<FollowUpReminderHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        factory.DidNotReceive().CreateScope();
    }
}
