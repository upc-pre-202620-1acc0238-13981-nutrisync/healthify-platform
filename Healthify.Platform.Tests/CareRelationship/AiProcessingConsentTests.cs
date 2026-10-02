using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.Acl;
using Healthify.Platform.CareRelationship.Application.Internal.CommandServices;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.CareRelationship.Interfaces.REST.Resources;
using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.CareRelationship;

/// <summary>
///     CR-2. The consent to AI processing lives inside the consent, is off unless the patient turns it on, can be
///     changed on its own, and goes off with the whole consent.
/// </summary>
public class AiProcessingConsentTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const string Scope = "diary,self-weigh-ins,active-targets";

    private readonly ICareLinkRepository _repository = Substitute.For<ICareLinkRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    [Fact]
    public void The_value_object_refuses_ai_processing_without_a_live_consent()
    {
        var at = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => new Consent(false, Scope, at, at, true, at));
        Assert.Throws<ArgumentException>(() => new Consent(true, Scope, at, null, true));
        Assert.Throws<ArgumentException>(() => new Consent(true, Scope, at, null, true, at.AddMinutes(-1)));
        var consent = new Consent(true, Scope, at, null, true, at);
        Assert.True(consent.AiProcessingGranted);
        Assert.Throws<ArgumentException>(() =>
            consent.Withdraw(at.AddMinutes(1)).WithAiProcessing(true, at.AddMinutes(2)));
    }

    [Fact]
    public void Granting_consent_without_the_switch_leaves_ai_off_and_undecided()
    {
        var link = Link();

        link.GrantConsent(new GrantConsentCommand(1, PatientId, Scope));

        Assert.True(link.IsActive);
        Assert.False(link.ConsentAiProcessingGranted);
        Assert.Null(link.ConsentAiProcessingDecidedAt);
        Assert.False(link.HasAiProcessingConsent);
    }

    [Fact]
    public void Granting_consent_with_the_switch_records_the_ai_decision_with_it()
    {
        var link = Link();

        link.GrantConsent(new GrantConsentCommand(1, PatientId, Scope, true));

        Assert.True(link.Consent!.AiProcessingGranted);
        Assert.Equal(link.ConsentGrantedAt, link.ConsentAiProcessingDecidedAt);
        Assert.True(link.HasAiProcessingConsent);
    }

    [Fact]
    public void Withdrawing_the_whole_consent_also_turns_ai_off()
    {
        var link = Link();
        link.GrantConsent(new GrantConsentCommand(1, PatientId, Scope, true));

        link.WithdrawConsent();

        Assert.False(link.ConsentAiProcessingGranted);
        Assert.Equal(link.ConsentWithdrawnAt, link.ConsentAiProcessingDecidedAt);
        Assert.False(link.HasAiProcessingConsent);
        Assert.Equal(Scope, link.Consent!.Scope);
    }

    [Fact]
    public void Ai_can_be_turned_on_only_on_an_active_link_and_off_always()
    {
        var pending = Link();
        Assert.Throws<InvalidOperationException>(() => pending.GrantAiProcessing());
        Assert.False(pending.RevokeAiProcessing());

        var active = Link();
        active.GrantConsent(new GrantConsentCommand(1, PatientId, Scope));
        Assert.True(active.GrantAiProcessing());
        Assert.False(active.GrantAiProcessing()); // already on
        Assert.True(active.RevokeAiProcessing()); // off is always possible
        Assert.False(active.RevokeAiProcessing()); // already off
        active.GrantAiProcessing();
        active.Discharge(new ClinicalReason("Objetivos alcanzados"));
        Assert.False(active.ConsentAiProcessingGranted); // the discharge ended it (AiConsentEndsWithLinkTests)
        Assert.Throws<InvalidOperationException>(() => active.GrantAiProcessing());
    }

    [Fact]
    public async Task Granting_consent_with_the_switch_publishes_both_events()
    {
        var link = Link();
        _repository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(link);

        var result = await Service().Handle(new GrantConsentCommand(1, PatientId, Scope, true));

        Assert.True(result.IsSuccess);
        var published = Fakes.Published(_mediator);
        Assert.True(Assert.IsType<ConsentGranted>(published[0]).AiProcessingGranted);
        var changed = Assert.IsType<AiProcessingConsentChanged>(published[1]);
        Assert.Equal((PatientId, true), (changed.PatientId, changed.Granted));
    }

    [Fact]
    public async Task Granting_consent_without_the_switch_publishes_no_ai_event()
    {
        _repository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Link());

        await Service().Handle(new GrantConsentCommand(1, PatientId, Scope));

        Assert.IsType<ConsentGranted>(Assert.Single(Fakes.Published(_mediator)));
    }

    [Fact]
    public async Task Changing_the_ai_consent_persists_and_publishes_only_real_changes()
    {
        var link = ActiveLink();
        _repository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(link);
        var service = Service();

        Assert.True((await service.Handle(new ChangeAiProcessingConsentCommand(1, PatientId, true))).IsSuccess);
        Assert.True((await service.Handle(new ChangeAiProcessingConsentCommand(1, PatientId, true))).IsSuccess);
        Assert.True((await service.Handle(new ChangeAiProcessingConsentCommand(1, PatientId, false))).IsSuccess);

        var events = Fakes.Published(_mediator).Cast<AiProcessingConsentChanged>().ToList();
        Assert.Equal([true, false], events.Select(e => e.Granted));
        Assert.Equal(link.ConsentAiProcessingDecidedAt, events[1].At);
        await _unitOfWork.Received(2).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_the_linked_patient_changes_it_and_never_on_a_closed_link()
    {
        var link = ActiveLink();
        _repository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(link);

        Assert.Equal(CareRelationshipError.CareLinkNotFound,
            Error(await Service().Handle(new ChangeAiProcessingConsentCommand(1, PatientId + 1, true))));

        link.Discharge(new ClinicalReason("Alta"));
        Assert.Equal(CareRelationshipError.DischargedLinkCannotBeReactivated,
            Error(await Service().Handle(new ChangeAiProcessingConsentCommand(1, PatientId, true))));

        var pending = Link();
        _repository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(pending);
        Assert.Equal(CareRelationshipError.CareLinkNotActive,
            Error(await Service().Handle(new ChangeAiProcessingConsentCommand(1, PatientId, true))));
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Fact]
    public async Task Withdrawing_consent_with_ai_on_publishes_the_ai_withdrawal_after_the_consent_one()
    {
        var link = ActiveLink(aiProcessing: true);
        _repository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(link);

        await Service().Handle(new WithdrawConsentCommand(1, PatientId));

        var published = Fakes.Published(_mediator);
        Assert.IsType<ConsentWithdrawn>(published[0]);
        var changed = Assert.IsType<AiProcessingConsentChanged>(published[1]);
        Assert.False(changed.Granted);
        Assert.Equal(link.ConsentWithdrawnAt, changed.At);
    }

    [Fact]
    public async Task Withdrawing_consent_with_ai_off_publishes_no_ai_event()
    {
        _repository.FindByIdAsync(1, Arg.Any<CancellationToken>()).Returns(ActiveLink());

        await Service().Handle(new WithdrawConsentCommand(1, PatientId));

        Assert.IsType<ConsentWithdrawn>(Assert.Single(Fakes.Published(_mediator)));
    }

    [Fact]
    public async Task The_facade_answers_the_ai_consent_of_the_active_link_and_degrades_to_false()
    {
        var queries = Substitute.For<ICareLinkQueryService>();
        var facade = new CareRelationshipContextFacade(queries);

        queries.Handle(Arg.Any<GetActiveCareLinkByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(ActiveLink(aiProcessing: true));
        Assert.True(await facade.HasAiProcessingConsent(PatientId));

        queries.Handle(Arg.Any<GetActiveCareLinkByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(ActiveLink());
        Assert.False(await facade.HasAiProcessingConsent(PatientId));

        queries.Handle(Arg.Any<GetActiveCareLinkByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((CareLink?)null);
        Assert.False(await facade.HasAiProcessingConsent(PatientId));

        queries.Handle(Arg.Any<GetActiveCareLinkByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));
        Assert.False(await facade.HasAiProcessingConsent(PatientId));
    }

    [Fact]
    public void The_resource_switch_is_optional_and_false_by_default_and_the_link_shows_it()
    {
        var command = GrantConsentCommandAssembler.ToCommand(1, PatientId, new GrantConsentResource(Scope));
        Assert.False(command.AiProcessingGranted);
        Assert.True(GrantConsentCommandAssembler
            .ToCommand(1, PatientId, new GrantConsentResource(Scope, true)).AiProcessingGranted);

        var resource = CareLinkResourceAssembler.ToResource(ActiveLink(aiProcessing: true));
        Assert.True(resource.AiProcessingGranted);
        Assert.NotNull(resource.AiProcessingDecidedAt);
    }

    private CareLinkCommandService Service()
    {
        return new CareLinkCommandService(_repository, _unitOfWork, Substitute.For<IIamContextFacade>(),
            NullLogger<CareLinkCommandService>.Instance, _mediator);
    }

    private static CareLink Link()
    {
        return Identity.Assign(new CareLink(new EstablishCareLinkCommand(PatientId, PractitionerId, 1)),
            new CareLinkId(1));
    }

    private static CareLink ActiveLink(bool aiProcessing = false)
    {
        var link = Link();
        link.GrantConsent(new GrantConsentCommand(1, PatientId, Scope, aiProcessing));
        return link;
    }

    private static CareRelationshipError? Error<T>(Result<T, CareRelationshipError> result)
    {
        return result is Result<T, CareRelationshipError>.Failure f ? f.Error : null;
    }
}
