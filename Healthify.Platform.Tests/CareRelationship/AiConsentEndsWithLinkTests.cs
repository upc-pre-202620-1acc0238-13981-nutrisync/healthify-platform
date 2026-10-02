using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.CommandServices;
using Healthify.Platform.CareRelationship.Application.Internal.CommandServices;
using Healthify.Platform.CareRelationship.Application.Internal.EventHandlers;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.CareRelationship;

/// <summary>
///     CR-2, rule "AI Processing Ends With The Link". Revoking a link (any reason, a switch of practitioner included)
///     or discharging the patient turns off the AI consent of that link in the same commit and publishes AI
///     Processing Consent Changed (false): the preferences go off and the AI audit of the patient is purged. Granting
///     AI consent on the new link turns the preferences on again.
/// </summary>
public class AiConsentEndsWithLinkTests
{
    private const int PatientId = 7;
    private const int PreviousPractitionerId = 42;
    private const int NewPractitionerId = 43;
    private const string Scope = "diary,self-weigh-ins,active-targets";

    private readonly ICareLinkRepository _careLinks = Substitute.For<ICareLinkRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IAiPreferencesRepository _preferencesRepository = Substitute.For<IAiPreferencesRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly InMemoryAiGenerationLog _log = new();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly AiPreferences _preferences = new(PatientId);

    public AiConsentEndsWithLinkTests()
    {
        _preferences.EnableAll();
        _preferencesRepository.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(_preferences);
        _iam.IsPractitioner(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        _log.Seed(AiFeature.WeeklySummary, PatientId, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow);
        _log.Seed(AiFeature.DiagnosisSuggestion, PatientId, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow,
            PreviousPractitionerId);
        _log.Seed(AiFeature.WeeklySummary, PatientId + 1, AiGenerationStatus.Succeeded, DateTimeOffset.UtcNow);

        // The real policy of this context, each time in a scope of its own, as Cortex.Mediator delivers it.
        var preferencesService = new AiPreferencesCommandService(_preferencesRepository, _careLinks, _unitOfWork,
            TimeProvider.System, NullLogger<AiPreferencesCommandService>.Instance, _mediator);
        Fakes.Route(_mediator, new OnAiProcessingConsentChangedHandler(
            Fakes.ScopeFactoryWith((typeof(IAiPreferencesCommandService), preferencesService),
                (typeof(IAiGenerationLog), _log)),
            NullLogger<OnAiProcessingConsentChangedHandler>.Instance));
    }

    [Theory]
    [InlineData(RevocationReason.SwitchedPractitionerValue)]
    [InlineData(RevocationReason.ConsentWithdrawnValue)]
    public void Revoking_for_any_reason_turns_the_ai_consent_of_the_link_off(string reason)
    {
        var link = Link(PreviousPractitionerId, 1, aiProcessing: true);

        link.Revoke(new RevocationReason(reason));

        Assert.False(link.ConsentAiProcessingGranted);
        Assert.Equal(link.RevokedAt, link.ConsentAiProcessingDecidedAt);
        Assert.False(link.HasAiProcessingConsent);
    }

    [Fact]
    public void Discharging_turns_the_ai_consent_off_and_a_link_without_it_keeps_its_ai_decision_untouched()
    {
        var withAi = Link(PreviousPractitionerId, 1, aiProcessing: true);
        withAi.Discharge(new ClinicalReason("Objetivos alcanzados"));
        Assert.False(withAi.ConsentAiProcessingGranted);
        Assert.Equal(withAi.DischargedAt, withAi.ConsentAiProcessingDecidedAt);

        var withoutAi = Link(PreviousPractitionerId, 2, aiProcessing: false);
        withoutAi.Discharge(new ClinicalReason("Objetivos alcanzados"));
        Assert.Null(withoutAi.ConsentAiProcessingDecidedAt);
    }

    [Fact]
    public async Task Switching_practitioner_turns_ai_off_purges_and_the_new_link_turns_it_on_again()
    {
        var previous = Link(PreviousPractitionerId, 1, aiProcessing: true);
        var next = Identity.Assign(new CareLink(new EstablishCareLinkCommand(PatientId, NewPractitionerId, 2)),
            new CareLinkId(2));
        _careLinks.FindUnclosedByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(previous, next);
        var invitation = Identity.Assign(
            new Invitation(new IssueInvitationCommand(NewPractitionerId, DateTimeOffset.UtcNow.AddDays(1))),
            new InvitationId(50));
        _invitations.FindByTokenAsync(Arg.Any<InvitationToken>(), Arg.Any<CancellationToken>()).Returns(invitation);

        var result = await new InvitationCommandService(_invitations, _careLinks, _unitOfWork, _iam,
                NullLogger<InvitationCommandService>.Instance, _mediator)
            .Handle(new RedeemInvitationCommand(invitation.Token.Value, PatientId, true));

        Assert.True(result.IsSuccess);
        Assert.False(previous.ConsentAiProcessingGranted);
        var published = Fakes.Published(_mediator);
        var revoked = published.FindIndex(e => e is CareLinkRevoked);
        var aiOff = published.FindIndex(e => e is AiProcessingConsentChanged { Granted: false });
        var redeemed = published.FindIndex(e => e is InvitationRedeemed);
        Assert.True(revoked < aiOff && aiOff < redeemed, string.Join(", ", published.Select(e => e.GetType().Name)));
        Assert.Equal(previous.RevokedAt, ((AiProcessingConsentChanged)published[aiOff]).At);
        AssertPreferencesOffAndAuditPurged();

        // The patient grants consent, with the AI switch on, on the link with the new practitioner.
        _careLinks.FindByIdAsync(2, Arg.Any<CancellationToken>()).Returns(next);
        await CareLinks().Handle(new GrantConsentCommand(2, PatientId, Scope, true));

        Assert.True(next.HasAiProcessingConsent);
        Assert.True(_preferences is { WeeklySummaryEnabled: true, MealIdeasEnabled: true, SuggestedQuestionsEnabled: true });
    }

    [Fact]
    public async Task Discharging_with_ai_on_publishes_the_ai_withdrawal_after_the_discharge_and_purges()
    {
        var link = Link(PreviousPractitionerId, 1, aiProcessing: true);
        _careLinks.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(link);

        var result = await CareLinks().Handle(new DischargePatientCommand(1, PreviousPractitionerId, "Objetivos alcanzados"));

        Assert.True(result.IsSuccess);
        var published = Fakes.Published(_mediator);
        Assert.IsType<TreatmentDischarged>(published[0]);
        var aiOff = Assert.IsType<AiProcessingConsentChanged>(published[1]);
        Assert.Equal((PatientId, false, link.DischargedAt!.Value), (aiOff.PatientId, aiOff.Granted, aiOff.At));
        AssertPreferencesOffAndAuditPurged();
    }

    [Fact]
    public async Task Discharging_with_ai_off_publishes_no_ai_event_and_purges_nothing()
    {
        var link = Link(PreviousPractitionerId, 1, aiProcessing: false);
        _careLinks.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(link);

        await CareLinks().Handle(new DischargePatientCommand(1, PreviousPractitionerId, "Objetivos alcanzados"));

        Assert.IsType<TreatmentDischarged>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal(3, _log.Rows.Count);
    }

    [Fact]
    public async Task Revoking_after_a_withdrawal_does_not_publish_the_ai_withdrawal_twice()
    {
        var link = Link(PreviousPractitionerId, 1, aiProcessing: true);
        _careLinks.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(link);
        var service = CareLinks();

        await service.Handle(new WithdrawConsentCommand(1, PatientId));
        await service.Handle(new RevokeCareLinkCommand(1));

        Assert.Single(Fakes.Published(_mediator).OfType<AiProcessingConsentChanged>());
        Assert.Single(Fakes.Published(_mediator).OfType<CareLinkRevoked>());
    }

    private void AssertPreferencesOffAndAuditPurged()
    {
        Assert.False(_preferences.AnyEnabled);
        Assert.Equal(PatientId + 1, Assert.Single(_log.Rows).SubjectPatientId);
    }

    private CareLinkCommandService CareLinks()
    {
        return new CareLinkCommandService(_careLinks, _unitOfWork, _iam, NullLogger<CareLinkCommandService>.Instance,
            _mediator);
    }

    private static CareLink Link(int practitionerId, int id, bool aiProcessing)
    {
        var link = Identity.Assign(new CareLink(new EstablishCareLinkCommand(PatientId, practitionerId, 1)),
            new CareLinkId(id));
        link.GrantConsent(new GrantConsentCommand(id, PatientId, Scope, aiProcessing));
        return link;
    }
}
