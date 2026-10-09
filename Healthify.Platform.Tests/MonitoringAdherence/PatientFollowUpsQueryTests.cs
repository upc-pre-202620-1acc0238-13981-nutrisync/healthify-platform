using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-3. The patient reads their next visit (PT3, PT20) and their past ones (PT25 "ANTERIORES"), with the name
///     of the practitioner from one batch read of Iam, and nothing clinical.
/// </summary>
public class PatientFollowUpsQueryTests
{
    private readonly InMemoryScheduledFollowUps _agenda = new(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));

    public PatientFollowUpsQueryTests()
    {
        _agenda.Iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
            {
                [20] = new(20, "rosa@correo.com", "Practitioner", "Rosa", "Medina")
            });
    }

    [Fact]
    public async Task Next_is_the_scheduled_visit_with_the_name_of_the_practitioner()
    {
        var past = _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(-30), "Fasting");
        past.MarkCompleted(1, DateTimeOffset.UtcNow.AddDays(-30));
        var upcoming = _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(3), "Fasting", "LightClothing");
        _agenda.Add(11, 20, DateTimeOffset.UtcNow.AddDays(1));

        var next = await _agenda.Queries.Handle(new GetNextFollowUpByPatientIdQuery(10));

        Assert.NotNull(next);
        Assert.Same(upcoming, next.FollowUp);
        Assert.Equal("Rosa Medina", next.PractitionerFullName);
        var resource = PatientFollowUpResourceAssembler.ToResource(next);
        Assert.Equal(["Fasting", "LightClothing"], resource.Preparation);
        Assert.Equal("InPerson", resource.Modality);
        Assert.Equal("Scheduled", resource.State);
    }

    [Fact]
    public async Task Without_a_visit_on_the_calendar_next_is_null()
    {
        var done = _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(-3));
        done.MarkCompleted(1, DateTimeOffset.UtcNow.AddDays(-3));

        Assert.Null(await _agenda.Queries.Handle(new GetNextFollowUpByPatientIdQuery(10)));
    }

    [Fact]
    public async Task The_list_filters_by_state_most_recent_first_and_only_for_that_patient()
    {
        var first = _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(-60));
        first.MarkCompleted(1, DateTimeOffset.UtcNow.AddDays(-60));
        var second = _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(-10));
        second.MarkCompleted(2, DateTimeOffset.UtcNow.AddDays(-10));
        _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(5));
        var other = _agenda.Add(11, 20, DateTimeOffset.UtcNow.AddDays(-5));
        other.MarkCompleted(3, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.True(PatientFollowUpsQueryAssembler.TryToQuery(10, "completed", out var query));
        var completed = await _agenda.Queries.Handle(query);
        var all = await _agenda.Queries.Handle(new GetFollowUpsByPatientIdQuery(10));

        Assert.Equal([second, first], completed.Select(e => e.FollowUp));
        Assert.Equal(3, all.Count);
        await _agenda.Iam.Received(2).GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void An_unknown_state_filter_is_refused()
    {
        Assert.False(PatientFollowUpsQueryAssembler.TryToQuery(10, "Pendiente", out _));
        Assert.True(PatientFollowUpsQueryAssembler.TryToQuery(10, null, out var all));
        Assert.Null(all.State);
    }

    [Fact]
    public async Task When_Iam_cannot_answer_the_visit_is_still_shown_without_the_name()
    {
        _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(3));
        _agenda.Iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>());

        var next = await _agenda.Queries.Handle(new GetNextFollowUpByPatientIdQuery(10));

        Assert.NotNull(next);
        Assert.Null(next.PractitionerFullName);
    }

    [Fact]
    public void The_resource_carries_nothing_clinical()
    {
        var names = typeof(PatientFollowUpResource).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Diagnos") || n.Contains("Note") || n.Contains("Rationale"));
    }
}
