using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.Acl;
using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Application.Internal.CommandServices;
using Healthify.Platform.CareRelationship.Application.Internal.EventHandlers;
using Healthify.Platform.CareRelationship.Application.Internal.QueryServices;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.CareRelationship.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.CareRelationship;

/// <summary>
///     IA-1. The patient's AI functions: coherent with the consent switch (CR-2), 409 when one is turned on without
///     it, all on when it is granted, all off (and the audit purged) when it is withdrawn, and the consent policy
///     the AI pipeline asks on every generation.
/// </summary>
public class AiPreferencesTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    private readonly IAiPreferencesRepository _preferences = Substitute.For<IAiPreferencesRepository>();
    private readonly ICareLinkRepository _careLinks = Substitute.For<ICareLinkRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private AiPreferences? _stored;

    public AiPreferencesTests()
    {
        _preferences.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_ => _stored);
        _preferences.When(r => r.AddAsync(Arg.Any<AiPreferences>(), Arg.Any<CancellationToken>()))
            .Do(call => _stored = call.Arg<AiPreferences>());
    }

    [Fact]
    public void A_function_cannot_be_turned_on_without_consent_but_can_always_be_turned_off()
    {
        var preferences = new AiPreferences(PatientId);

        Assert.Throws<InvalidOperationException>(() => preferences.Change(true, false, false, false));
        Assert.False(preferences.Change(false, false, false, false)); // nothing changed
        Assert.True(preferences.Change(true, false, true, true));
        Assert.True(preferences.Change(false, false, false, false));
        Assert.True(preferences.EnableAll());
        Assert.False(preferences.EnableAll());
        Assert.True(preferences.DisableAll());
        Assert.False(preferences.AnyEnabled);
        Assert.Throws<ArgumentException>(() => new AiPreferences(0));
    }

    [Fact]
    public async Task Turning_a_function_on_without_ai_consent_is_a_409_ai_consent_required_to_enable_feature()
    {
        Consent(granted: true, aiProcessing: false);

        var result = await Service().Handle(new UpdateAiPreferencesCommand(PatientId, false, true, false));

        Assert.Equal(CareRelationshipError.AiConsentRequiredToEnableFeature, Error(result));
        Assert.Null(_stored);
        Assert.Empty(Fakes.Published(_mediator));

        var localizer = Substitute.For<IStringLocalizer<CareRelationshipMessages>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>()));
        var http = Assert.IsType<ObjectResult>(CareRelationshipActionResultAssembler.ToAiPreferencesResult(result, localizer));
        Assert.Equal(StatusCodes.Status409Conflict, http.StatusCode);
        Assert.Equal("AiConsentRequiredToEnableFeature", Assert.IsType<ProblemDetails>(http.Value).Detail);
    }

    [Fact]
    public async Task Without_any_link_turning_everything_off_is_accepted()
    {
        var result = await Service().Handle(new UpdateAiPreferencesCommand(PatientId, false, false, false));

        var status = Success(result);
        Assert.False(status.ConsentGranted);
        Assert.NotNull(_stored);
        Assert.Empty(Fakes.Published(_mediator)); // a new row with everything off is not a change
    }

    [Fact]
    public async Task With_ai_consent_the_patient_picks_functions_and_each_change_is_published()
    {
        Consent(granted: true, aiProcessing: true);
        var service = Service();

        var status = Success(await service.Handle(new UpdateAiPreferencesCommand(PatientId, true, false, true)));
        Success(await service.Handle(new UpdateAiPreferencesCommand(PatientId, true, false, true))); // same again

        Assert.Equal(new AiPreferencesStatus(PatientId, true, true, false, true), status);
        var changed = Assert.IsType<AiPreferencesChanged>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal((true, false, true),
            (changed.WeeklySummaryEnabled, changed.MealIdeasEnabled, changed.SuggestedQuestionsEnabled));
    }

    [Fact]
    public async Task Granting_ai_consent_turns_the_three_functions_on_and_withdrawing_it_turns_them_off()
    {
        var service = Service();

        var granted = Success(await service.Handle(new SyncAiPreferencesWithConsentCommand(PatientId, true)));
        // Changed on purpose by IN-7: granting the consent also turns meal photo recognition on (four functions).
        Assert.Equal(new AiPreferencesStatus(PatientId, true, true, true, true, true), granted);

        _stored!.Change(false, true, false, true); // the patient later keeps only meal ideas
        var withdrawn = Success(await service.Handle(new SyncAiPreferencesWithConsentCommand(PatientId, false)));
        Assert.False(withdrawn.ConsentGranted);
        Assert.False(_stored.AnyEnabled);

        var reactivated = Success(await service.Handle(new SyncAiPreferencesWithConsentCommand(PatientId, true)));
        Assert.True(reactivated.WeeklySummaryEnabled && reactivated.MealIdeasEnabled &&
                    reactivated.SuggestedQuestionsEnabled && reactivated.MealPhotoRecognitionEnabled);
        Assert.Equal(3, Fakes.Published(_mediator).OfType<AiPreferencesChanged>().Count());
    }

    [Fact]
    public async Task A_withdrawal_for_a_patient_who_never_had_preferences_writes_nothing()
    {
        var result = await Service().Handle(new SyncAiPreferencesWithConsentCommand(PatientId, false));

        Assert.True(result.IsSuccess);
        Assert.Null(_stored);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
    }

    [Fact]
    public async Task The_query_reports_functions_as_off_while_the_consent_is_missing()
    {
        _stored = new AiPreferences(PatientId);
        _stored.EnableAll();
        Consent(granted: true, aiProcessing: false); // e.g. a sync that has not run yet

        var status = await new AiPreferencesQueryService(_preferences, _careLinks)
            .Handle(new GetAiPreferencesByPatientIdQuery(PatientId));

        Assert.Equal(new AiPreferencesStatus(PatientId, false, false, false, false), status);
    }

    [Fact]
    public async Task The_consent_handler_syncs_the_preferences_and_purges_the_audit_on_withdrawal()
    {
        var commands = Substitute.For<IAiPreferencesCommandService>();
        commands.Handle(Arg.Any<SyncAiPreferencesWithConsentCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<AiPreferencesStatus, CareRelationshipError>.Success(
                new AiPreferencesStatus(PatientId, false, false, false, false)));
        var log = new InMemoryAiGenerationLog();
        log.Seed(AiFeature.WeeklySummary, PatientId, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow);
        log.Seed(AiFeature.DiagnosisSuggestion, PatientId, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow, 20);
        log.Seed(AiFeature.WeeklySummary, PatientId + 1, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow);
        var handler = new OnAiProcessingConsentChangedHandler(
            Fakes.ScopeFactoryWith((typeof(IAiPreferencesCommandService), commands), (typeof(IAiGenerationLog), log)),
            NullLogger<OnAiProcessingConsentChangedHandler>.Instance);

        await handler.Handle(new AiProcessingConsentChanged(PatientId, true, DateTimeOffset.UtcNow), default);
        Assert.Equal(3, log.Rows.Count); // granting purges nothing

        await handler.Handle(new AiProcessingConsentChanged(PatientId, false, DateTimeOffset.UtcNow), default);

        await commands.Received(1).Handle(new SyncAiPreferencesWithConsentCommand(PatientId, false),
            Arg.Any<CancellationToken>());
        Assert.Equal(PatientId + 1, Assert.Single(log.Rows).SubjectPatientId);
    }

    [Fact]
    public async Task Turning_a_function_off_purges_only_its_rows()
    {
        var log = new InMemoryAiGenerationLog();
        log.Seed(AiFeature.WeeklySummary, PatientId, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow);
        log.Seed(AiFeature.MealIdeas, PatientId, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow);
        log.Seed(AiFeature.PractitionerMonitoringSummary, PatientId, AiGenerationStatus.Succeeded,
            DateTimeOffset.UtcNow, PractitionerId);
        var handler = new OnAiPreferencesChangedHandler(Fakes.ScopeFactoryWith<IAiGenerationLog>(log),
            NullLogger<OnAiPreferencesChangedHandler>.Instance);

        await handler.Handle(new AiPreferencesChanged(PatientId, true, false, true, DateTimeOffset.UtcNow), default);

        Assert.Equal([AiFeature.WeeklySummary.Name, AiFeature.PractitionerMonitoringSummary.Name],
            log.Rows.Select(r => r.Feature));
    }

    [Fact]
    public async Task A_failing_purge_never_escapes_the_handler()
    {
        var log = Substitute.For<IAiGenerationLog>();
        log.PurgeForPatientAsync(default, default, default).ReturnsForAnyArgs(Task.FromException<int>(new TimeoutException()));
        var handler = new OnAiPreferencesChangedHandler(Fakes.ScopeFactoryWith(log),
            NullLogger<OnAiPreferencesChangedHandler>.Instance);

        await handler.Handle(new AiPreferencesChanged(PatientId, false, false, false, DateTimeOffset.UtcNow), default);
    }

    [Theory]
    // consent, weekly, meal ideas, questions, feature, expected
    [InlineData(false, true, true, true, "WeeklySummary", false)]
    [InlineData(true, true, false, false, "WeeklySummary", true)]
    [InlineData(true, true, false, false, "MealIdeas", false)]
    [InlineData(true, false, false, true, "SuggestedQuestions", true)]
    // Practitioner functions: the consent only (§12-#5), whatever the patient's own tools are.
    [InlineData(true, false, false, false, "DiagnosisSuggestion", true)]
    [InlineData(true, false, false, false, "PlanAdjustmentProposal", true)]
    [InlineData(false, true, true, true, "PractitionerMonitoringSummary", false)]
    public async Task The_consent_policy_asks_consent_and_preference_for_patient_functions_and_consent_only_for_the_practitioner(
        bool consent, bool weekly, bool meal, bool questions, string feature, bool expected)
    {
        var queries = Substitute.For<IAiPreferencesQueryService>();
        queries.Handle(new GetAiPreferencesByPatientIdQuery(PatientId), Arg.Any<CancellationToken>())
            .Returns(AiPreferencesStatus.Of(PatientId, consent, weekly, meal, questions));

        var allowed = await new CareRelationshipAiConsentPolicy(queries)
            .IsAllowedAsync(PatientId, AiFeature.FromName(feature)!);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task The_consent_policy_answers_no_when_the_lookup_fails()
    {
        var queries = Substitute.For<IAiPreferencesQueryService>();
        queries.Handle(Arg.Any<GetAiPreferencesByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));

        Assert.False(await new CareRelationshipAiConsentPolicy(queries)
            .IsAllowedAsync(PatientId, AiFeature.DiagnosisSuggestion));
    }

    private AiPreferencesCommandService Service()
    {
        return new AiPreferencesCommandService(_preferences, _careLinks, _unitOfWork, TimeProvider.System,
            NullLogger<AiPreferencesCommandService>.Instance, _mediator);
    }

    private void Consent(bool granted, bool aiProcessing)
    {
        var link = Identity.Assign(new CareLink(new EstablishCareLinkCommand(PatientId, PractitionerId, 1)),
            new CareLinkId(1));
        if (granted) link.GrantConsent(new GrantConsentCommand(1, PatientId, "diary", aiProcessing));
        _careLinks.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(link);
    }

    private static AiPreferencesStatus Success(Result<AiPreferencesStatus, CareRelationshipError> result)
    {
        return Assert.IsType<Result<AiPreferencesStatus, CareRelationshipError>.Success>(result).Value;
    }

    private static CareRelationshipError? Error<T>(Result<T, CareRelationshipError> result)
    {
        return result is Result<T, CareRelationshipError>.Failure f ? f.Error : null;
    }
}
