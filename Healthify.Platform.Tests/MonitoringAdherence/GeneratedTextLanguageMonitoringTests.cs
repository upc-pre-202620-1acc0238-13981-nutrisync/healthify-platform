using System.Text.Json;
using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     X-2, Monitoring side. The signals carry their evidence as numbers next to the English sentence (kept as the
///     legacy fallback), so the clinical context stores them for the client to word in the reader's language; and an
///     AI suggested question of the check in keeps the language it was generated in, while a question the patient
///     wrote is their own text and has none.
/// </summary>
public class GeneratedTextLanguageMonitoringTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    // ---- Signals ----

    [Fact]
    public async Task An_escalation_publishes_the_state_dates_and_weeks_in_alert_with_its_sentence()
    {
        var indices = Substitute.For<IConsistencyIndexRepository>();
        var mediator = Substitute.For<IMediator>();
        var care = Substitute.For<ICareRelationshipContextFacade>();
        care.GetActiveCareLinkByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new CareLinkStatusItem(1, PatientId, PractitionerId, true, true, null));
        var index = InAlert(weeksInAlert: 4);
        index.PromptPatient();
        var shown = DateTimeOffset.UtcNow.AddDays(-8);
        index.MarkShownToPatient(shown);
        indices.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(index);

        var service = new ConsistencyIndexCommandService(indices, Substitute.For<IEvaluationWindowRepository>(),
            Substitute.For<IIntakeContextFacade>(), care, Substitute.For<IUnitOfWork>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monitoring:ConsistencyAlertThreshold"] = "1.5"
            }).Build(),
            NullLogger<ConsistencyIndexCommandService>.Instance, mediator);

        Assert.True((await service.Handle(new EscalateToPractitionerCommand(PatientId))).IsSuccess);

        var escalated = Assert.Single(Fakes.Published(mediator).OfType<AlertEscalatedToPractitioner>());
        Assert.Equal("Alert", escalated.State);
        Assert.Equal(index.AlertSinceAt, escalated.AlertSinceAt);
        Assert.Equal(shown, escalated.ShownToPatientAt);
        Assert.Equal(4, escalated.WeeksInAlert);
        Assert.Equal(index.Value, escalated.Value);
        Assert.StartsWith("Consistency index", escalated.Evidence);
    }

    [Fact]
    public void Weeks_in_alert_are_whole_weeks_and_there_are_none_outside_an_alert()
    {
        var index = InAlert(weeksInAlert: 3);

        Assert.Equal(3, index.WeeksInAlertAt(DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(2, index.WeeksInAlertAt(DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.Null(new ConsistencyIndex(PatientId).WeeksInAlertAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_sustained_deviation_publishes_its_kcal_per_day()
    {
        var deviation = Identity.Assign(new Deviation(new WindowId(1), PatientId,
            new DeviationMagnitude(0.4m, 718.4m), new DeviationDirection("Below"), 9, 5), new DeviationId(1));
        var deviations = Substitute.For<IDeviationRepository>();
        deviations.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(deviation);
        var mediator = Substitute.For<IMediator>();
        var service = new DeviationCommandService(deviations, Substitute.For<IEvaluationWindowRepository>(),
            Substitute.For<IUnitOfWork>(), new ConfigurationBuilder().Build(),
            NullLogger<DeviationCommandService>.Instance, mediator);

        await service.Handle(new FlagSustainedDeviationCommand(1));

        var detected = Assert.Single(Fakes.Published(mediator).OfType<SustainedDeviationDetected>());
        Assert.Equal(718.4m, detected.MagnitudeEnergyKcal);
        Assert.Equal((5, 9), (detected.DeviatingDaysConsidered, detected.LoggedDaysConsidered));
    }

    // ---- Check in questions ----

    [Fact]
    public void An_AI_suggested_question_keeps_its_language_and_the_patients_own_has_none()
    {
        Assert.Equal("en", new PatientQuestion("How do I plan dinners?", "AiSuggested", 42, "EN").Language);
        Assert.Null(new PatientQuestion("¿Puedo comer fuera?", "Patient", null, "es").Language);
        Assert.Null(new PatientQuestion("¿Cómo armo cenas?", "AiSuggested", 42).Language);
        Assert.Throws<ArgumentException>(() => new PatientQuestion("¿Cómo armo cenas?", "AiSuggested", 42, "fr"));
    }

    [Fact]
    public void Questions_stored_before_X2_read_back_without_a_language()
    {
        // The JSON of pre_visit_check_ins.questions, as the EF mapping serializes it.
        var before = """[{"Text":"¿Cómo armo cenas con más proteína?","Origin":"AiSuggested","AiGenerationId":42}]""";

        var question = Assert.Single(JsonSerializer.Deserialize<List<PatientQuestion>>(before)!);

        Assert.Equal(("AiSuggested", 42L, (string?)null), (question.Origin, question.AiGenerationId, question.Language));
        var roundTrip = JsonSerializer.Deserialize<List<PatientQuestion>>(JsonSerializer.Serialize(
            new List<PatientQuestion> { new("How do I plan dinners?", "AiSuggested", 42, "en") }))!;
        Assert.Equal("en", Assert.Single(roundTrip).Language);
    }

    [Fact]
    public void A_suggestion_sent_without_its_language_takes_the_language_of_the_request()
    {
        var resource = new SubmitPreVisitCheckInResource("Good", [], [
            new CheckInQuestionInputResource("How do I plan dinners?", "AiSuggested", 42),
            new CheckInQuestionInputResource("¿Cómo armo cenas?", "AiSuggested", 43, "es"),
            new CheckInQuestionInputResource("¿Puedo comer fuera?")
        ]);

        var command = SubmitPreVisitCheckInCommandAssembler.ToCommand(7, PatientId, resource, "en");
        var questions = PreVisitCheckIn.QuestionsOf(command.Questions);

        Assert.Equal(["en", "es", null], questions.Select(q => q.Language));
        // A culture outside the app's languages is not kept.
        var unknown = SubmitPreVisitCheckInCommandAssembler.ToCommand(7, PatientId, resource, "pt");
        Assert.Null(PreVisitCheckIn.QuestionsOf(unknown.Questions)[0].Language);
    }

    /// <summary>An index in Alert: 10 kg lost in two weeks with the intake on target.</summary>
    private static ConsistencyIndex InAlert(int weeksInAlert)
    {
        var index = new ConsistencyIndex(PatientId);
        var start = new DateOnly(2026, 9, 1);
        index.Recompute([(start, 80m), (start.AddDays(14), 70m)],
            Enumerable.Range(0, 15).Select(i => new DailyCompliance(start.AddDays(i), DailyCompliance.Met, 1800m,
                1800m, 1, 3, DateTimeOffset.UtcNow)).ToList(), 1.5m);
        Assert.True(index.IsInAlert);
        typeof(ConsistencyIndex).GetProperty(nameof(ConsistencyIndex.AlertSinceAt))!
            .SetValue(index, DateTimeOffset.UtcNow.AddDays(-7 * weeksInAlert));
        return index;
    }
}
