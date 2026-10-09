using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-2. Scheduling validates preparation and modality first, each to its own error, before the care link is
///     asked or anything is written; the agenda carries the name of each patient from one batch read of Iam.
/// </summary>
public class ScheduleFollowUpPreparationTests
{
    private readonly InMemoryScheduledFollowUps _agenda = new(new ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo.Utc));

    [Fact]
    public async Task An_instruction_outside_the_list_is_rejected_before_the_link_is_asked()
    {
        var result = await _agenda.Commands.Handle(new ScheduleFollowUpCommand(10, 20,
            DateTimeOffset.UtcNow.AddDays(2), ["Fasting", "Ayunas"]));

        Assert.Equal(MonitoringError.UnknownPreparationInstruction,
            Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Failure>(result).Error);
        await _agenda.CareRelationship.DidNotReceiveWithAnyArgs().IsCareLinkActive(default, default, default);
        Assert.Empty(_agenda.Rows);
    }

    [Fact]
    public async Task An_unknown_modality_is_its_own_error()
    {
        var result = await _agenda.Commands.Handle(new ScheduleFollowUpCommand(10, 20,
            DateTimeOffset.UtcNow.AddDays(2), ["Fasting"], "Telefono"));

        Assert.Equal(MonitoringError.UnknownConsultationModality,
            Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Failure>(result).Error);
        Assert.Empty(_agenda.Rows);
    }

    [Fact]
    public async Task A_valid_visit_is_stored_with_its_preparation_and_modality()
    {
        var result = await _agenda.Commands.Handle(new ScheduleFollowUpCommand(10, 20,
            DateTimeOffset.UtcNow.AddDays(2), ["Fasting", "LightClothing"]));

        var followUp = Assert.IsType<Result<ScheduledFollowUp, MonitoringError>.Success>(result).Value;
        Assert.Equal(["Fasting", "LightClothing"], followUp.Preparation.Select(p => p.Value));
        Assert.Equal("InPerson", followUp.Modality.Value);
        Assert.Same(followUp, Assert.Single(_agenda.Rows));
    }

    [Fact]
    public async Task The_agenda_reads_the_names_once_and_filters_by_state()
    {
        var upcoming = _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(2));
        var done = _agenda.Add(11, 20, DateTimeOffset.UtcNow.AddDays(-2));
        done.MarkCompleted(5, DateTimeOffset.UtcNow.AddDays(-2));
        _agenda.Add(12, 99, DateTimeOffset.UtcNow.AddDays(1));
        _agenda.Iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
            {
                [10] = new(10, "ana@correo.com", "Patient", "Ana", "Flores"),
                [11] = new(11, "luz@correo.com", "Patient", "Luz", "Ramírez")
            });

        var all = await _agenda.Queries.Handle(new GetPractitionerAgendaQuery(20));
        var next = await _agenda.Queries.Handle(
            new GetPractitionerAgendaQuery(20, new FollowUpState(FollowUpState.Scheduled)));

        Assert.Equal(["Luz Ramírez", "Ana Flores"], all.Select(e => e.PatientFullName));
        Assert.Same(upcoming, Assert.Single(next).FollowUp);
        await _agenda.Iam.Received(2).GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_Iam_the_agenda_is_still_shown_without_names()
    {
        _agenda.Add(10, 20, DateTimeOffset.UtcNow.AddDays(2));
        _agenda.Iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>());

        var entry = Assert.Single(await _agenda.Queries.Handle(new GetPractitionerAgendaQuery(20)));

        Assert.Null(entry.PatientFullName);
        var resource = ScheduledFollowUpResourceAssembler.ToResource(entry);
        Assert.Equal("InPerson", resource.Modality);
        Assert.Empty(resource.Preparation);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("scheduled", true)]
    [InlineData("Cancelled", true)]
    [InlineData("Pending", false)]
    public void The_state_filter_must_be_a_follow_up_state(string? state, bool valid)
    {
        Assert.Equal(valid, PractitionerAgendaQueryAssembler.TryToQuery(20, state, null, out _));
    }
}
