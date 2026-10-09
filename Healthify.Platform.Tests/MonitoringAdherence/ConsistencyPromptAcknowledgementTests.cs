using System.Security.Claims;
using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-7. Issuing the consistency prompt is not showing it: the alert counts as shown to the patient only when
///     their app acknowledges it painted the card (PT3), and the practitioner is told only N days after that
///     (Patient First Always, Patient Prompt Required Before Escalation).
/// </summary>
public class ConsistencyPromptAcknowledgementTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    private readonly IConsistencyIndexRepository _indices = Substitute.For<IConsistencyIndexRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ICareRelationshipContextFacade _care = Substitute.For<ICareRelationshipContextFacade>();

    public ConsistencyPromptAcknowledgementTests()
    {
        _care.GetActiveCareLinkByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new CareLinkStatusItem(1, PatientId, PractitionerId, true, true, null));
    }

    [Fact]
    public void Prompting_issues_the_prompt_without_marking_it_shown()
    {
        var index = InAlert();

        Assert.True(index.PromptPatient());

        Assert.NotNull(index.PromptIssuedAt);
        Assert.Null(index.ShownToPatientAt);
        Assert.True(index.IsPatientPromptPending);
        Assert.False(index.PromptPatient());
        // Business rule: Patient Prompt Required Before Escalation, still enforced by the aggregate.
        Assert.Throws<InvalidOperationException>(() => index.Escalate());
    }

    [Fact]
    public void Only_an_issued_prompt_is_acknowledged_and_the_first_moment_is_kept()
    {
        var index = InAlert();
        Assert.Throws<InvalidOperationException>(() => index.MarkShownToPatient(DateTimeOffset.UtcNow));

        index.PromptPatient();
        var first = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.True(index.MarkShownToPatient(first));
        Assert.False(index.MarkShownToPatient(DateTimeOffset.UtcNow));

        Assert.Equal(first, index.ShownToPatientAt);
        Assert.False(index.IsPatientPromptPending);
    }

    [Fact]
    public async Task The_acknowledgement_records_the_moment_and_publishes_after_the_commit()
    {
        var index = InAlert();
        index.PromptPatient();
        _indices.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(index);

        var result = await Service().Handle(new AcknowledgeConsistencyPromptCommand(PatientId));

        Assert.IsType<Result<ConsistencyIndex, MonitoringError>.Success>(result);
        Assert.NotNull(index.ShownToPatientAt);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        Assert.Single(Fakes.Published(_mediator).OfType<ConsistencyPromptAcknowledged>());
    }

    [Fact]
    public async Task Without_a_prompt_there_is_nothing_to_acknowledge()
    {
        _indices.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(InAlert());

        var withoutPrompt = await Service().Handle(new AcknowledgeConsistencyPromptCommand(PatientId));
        _indices.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((ConsistencyIndex?)null);
        var withoutIndex = await Service().Handle(new AcknowledgeConsistencyPromptCommand(PatientId));

        Assert.Equal(MonitoringError.ConsistencyPromptNotIssued,
            Assert.IsType<Result<ConsistencyIndex, MonitoringError>.Failure>(withoutPrompt).Error);
        Assert.Equal(MonitoringError.ConsistencyPromptNotIssued,
            Assert.IsType<Result<ConsistencyIndex, MonitoringError>.Failure>(withoutIndex).Error);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync();
    }

    [Fact]
    public async Task A_prompt_issued_but_not_seen_never_reaches_the_practitioner()
    {
        var index = InAlert(weeksInAlert: 4);
        index.PromptPatient();
        _indices.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(index);

        var result = await Service().Handle(new EscalateToPractitionerCommand(PatientId));

        Assert.Equal(MonitoringError.PatientPromptRequiredBeforeEscalation,
            Assert.IsType<Result<ConsistencyIndex, MonitoringError>.Failure>(result).Error);
        Assert.Null(index.EscalatedAt);
        Assert.Empty(Fakes.Published(_mediator).OfType<AlertEscalatedToPractitioner>());
    }

    [Fact]
    public async Task The_practitioner_is_told_only_N_days_after_the_patient_saw_it()
    {
        var index = InAlert(weeksInAlert: 4);
        index.PromptPatient();
        index.MarkShownToPatient(DateTimeOffset.UtcNow.AddDays(-2));
        _indices.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(index);

        var tooRecent = await Service().Handle(new EscalateToPractitionerCommand(PatientId));
        Assert.Equal(MonitoringError.PatientAcknowledgementTooRecent,
            Assert.IsType<Result<ConsistencyIndex, MonitoringError>.Failure>(tooRecent).Error);

        var seenLongAgo = InAlert(weeksInAlert: 4);
        seenLongAgo.PromptPatient();
        seenLongAgo.MarkShownToPatient(DateTimeOffset.UtcNow.AddDays(-8));
        _indices.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(seenLongAgo);

        var escalated = await Service().Handle(new EscalateToPractitionerCommand(PatientId));
        Assert.IsType<Result<ConsistencyIndex, MonitoringError>.Success>(escalated);
        Assert.NotNull(seenLongAgo.EscalatedAt);
    }

    [Fact]
    public void Returning_to_normal_clears_both_moments_so_the_next_episode_asks_again()
    {
        var index = InAlert();
        index.PromptPatient();
        index.MarkShownToPatient(DateTimeOffset.UtcNow);

        // Steady weight and intake on target: nothing unexplained.
        index.Recompute(Series(80m, 80m), Days(1800m), 1.5m);

        Assert.True(index.State.IsNormal);
        Assert.Null(index.PromptIssuedAt);
        Assert.Null(index.ShownToPatientAt);
    }

    [Fact]
    public void The_consistency_card_says_whether_the_prompt_is_pending()
    {
        var index = InAlert();
        Assert.False(ConsistencyIndexResourceAssembler.ToResource(index).PatientPromptPending);

        index.PromptPatient();
        Assert.True(ConsistencyIndexResourceAssembler.ToResource(index).PatientPromptPending);

        index.MarkShownToPatient(DateTimeOffset.UtcNow);
        Assert.False(ConsistencyIndexResourceAssembler.ToResource(index).PatientPromptPending);
    }

    [Fact]
    public async Task Only_the_patient_acknowledges_their_own_prompt()
    {
        var commands = Substitute.For<IConsistencyIndexCommandService>();
        var localizer = Substitute.For<IStringLocalizer<MonitoringMessages>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString((string)c[0], (string)c[0]));
        var controller = new ConsistencyPromptController(commands, localizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "99"), new Claim(ClaimTypes.Role, "Patient")], "test"))
                }
            }
        };

        Assert.IsType<ForbidResult>(await controller.AcknowledgePrompt(PatientId));
        await commands.DidNotReceiveWithAnyArgs().Handle(default(AcknowledgeConsistencyPromptCommand)!);
    }

    private ConsistencyIndexCommandService Service()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Monitoring:ConsistencyAlertThreshold"] = "1.5"
        }).Build();
        return new ConsistencyIndexCommandService(_indices, Substitute.For<IEvaluationWindowRepository>(),
            Substitute.For<IIntakeContextFacade>(), _care, _unitOfWork, configuration,
            NullLogger<ConsistencyIndexCommandService>.Instance, _mediator);
    }

    /// <summary>An index in Alert: 10 kg lost in two weeks with the intake on target.</summary>
    private static ConsistencyIndex InAlert(int weeksInAlert = 0)
    {
        var index = new ConsistencyIndex(PatientId);
        index.Recompute(Series(80m, 70m), Days(1800m), 1.5m);
        Assert.True(index.IsInAlert);
        typeof(ConsistencyIndex).GetProperty(nameof(ConsistencyIndex.AlertSinceAt))!
            .SetValue(index, DateTimeOffset.UtcNow.AddDays(-7 * weeksInAlert));
        return index;
    }

    private static List<(DateOnly Date, decimal ValueKg)> Series(decimal fromKg, decimal toKg)
    {
        var start = new DateOnly(2026, 9, 1);
        return [(start, fromKg), (start.AddDays(14), toKg)];
    }

    private static List<DailyCompliance> Days(decimal observedKcal)
    {
        var start = new DateOnly(2026, 9, 1);
        return Enumerable.Range(0, 15).Select(i => new DailyCompliance(start.AddDays(i), DailyCompliance.Met,
            observedKcal, 1800m, 1, 3, DateTimeOffset.UtcNow)).ToList();
    }
}
