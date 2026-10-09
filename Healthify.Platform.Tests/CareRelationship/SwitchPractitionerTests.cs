using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.Acl;
using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Application.Internal.CommandServices;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.CareRelationship;

/// <summary>
///     CR-1 - switching practitioners. Redeeming another practitioner's invitation with the explicit
///     confirmation revokes the current link (reason SwitchedPractitioner) in the same commit; without
///     it, the conflict answer is unchanged.
/// </summary>
public class SwitchPractitionerTests
{
    private const int PatientId = 7;
    private const int PreviousPractitionerId = 42;
    private const int NewPractitionerId = 43;

    private readonly IInvitationRepository _invitationRepository = Substitute.For<IInvitationRepository>();
    private readonly ICareLinkRepository _careLinkRepository = Substitute.For<ICareLinkRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly CareLink _previousLink;
    private readonly CareLink _newLink;
    private Invitation _invitation;

    public SwitchPractitionerTests()
    {
        _previousLink = ConsentedLink(PreviousPractitionerId, 1);
        _newLink = Identity.Assign(
            new CareLink(new EstablishCareLinkCommand(PatientId, NewPractitionerId, 2)), new CareLinkId(2));
        _invitation = InvitationFrom(NewPractitionerId);

        // First read: the guard sees the previous link. Second read: the answer sees the one the
        // Invitation Redeemed policy established.
        _careLinkRepository.FindUnclosedByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns(_previousLink, _newLink);
    }

    [Fact]
    public async Task Without_the_flag_an_existing_link_is_still_a_conflict_and_nothing_changes()
    {
        var result = await Redeem(replaceActiveLink: false);

        AssertFailure(result, CareRelationshipError.PatientAlreadyHasActiveLink);
        Assert.False(_previousLink.IsRevoked);
        Assert.False(_invitation.IsRedeemed);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task Without_the_flag_the_same_practitioner_also_gets_the_original_conflict()
    {
        _invitation = InvitationFrom(PreviousPractitionerId);

        var result = await Redeem(replaceActiveLink: false);

        AssertFailure(result, CareRelationshipError.PatientAlreadyHasActiveLink);
    }

    [Fact]
    public async Task With_the_flag_the_same_practitioner_cannot_replace_their_own_link()
    {
        _invitation = InvitationFrom(PreviousPractitionerId);

        var result = await Redeem(replaceActiveLink: true);

        AssertFailure(result, CareRelationshipError.AlreadyLinkedToThisPractitioner);
        Assert.False(_previousLink.IsRevoked);
        Assert.False(_invitation.IsRedeemed);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task With_the_flag_the_previous_link_is_revoked_and_the_invitation_redeemed_in_one_commit()
    {
        var result = await Redeem(replaceActiveLink: true);

        var success = Assert.IsType<Result<InvitationRedemptionOutcome, CareRelationshipError>.Success>(result);
        Assert.Same(_newLink, success.Value.CareLink);

        Assert.NotNull(_previousLink.RevokedAt);
        Assert.Equal(RevocationReason.SwitchedPractitioner, _previousLink.RevocationReason);
        Assert.False(_previousLink.IsActive);
        Assert.True(_invitation.IsRedeemed);

        _careLinkRepository.Received(1).Update(_previousLink);
        _invitationRepository.Received(1).Update(_invitation);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revocation_is_published_after_the_commit_and_before_the_redemption()
    {
        var order = new List<string>();
        _unitOfWork.When(u => u.CompleteAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("commit"));
        _mediator.When(m => m.PublishAsync(Arg.Any<CareLinkRevoked>(), Arg.Any<CancellationToken>()))
            .Do(_ => order.Add(nameof(CareLinkRevoked)));
        _mediator.When(m => m.PublishAsync(Arg.Any<InvitationRedeemed>(), Arg.Any<CancellationToken>()))
            .Do(_ => order.Add(nameof(InvitationRedeemed)));

        await Redeem(replaceActiveLink: true);

        // Monitoring closes the open window on the revocation and opens the new one on the link the
        // redemption policy establishes; it refuses a second open window, so this order matters.
        Assert.Equal(["commit", nameof(CareLinkRevoked), nameof(InvitationRedeemed)], order);

        var revoked = Assert.IsType<CareLinkRevoked>(Fakes.Published(_mediator)[0]);
        Assert.Equal(1, revoked.CareLinkId);
        Assert.Equal(PatientId, revoked.PatientId);
        Assert.Equal(PreviousPractitionerId, revoked.PractitionerId);
        Assert.Equal(_previousLink.RevokedAt, revoked.RevokedAt);
        Assert.Equal(RevocationReason.SwitchedPractitionerValue, revoked.Reason);

        var redeemed = Assert.IsType<InvitationRedeemed>(Fakes.Published(_mediator)[1]);
        Assert.Equal(PatientId, redeemed.PatientId);
        Assert.Equal(NewPractitionerId, redeemed.PractitionerId);
    }

    [Fact]
    public async Task With_the_flag_and_no_current_link_the_redemption_is_the_usual_one()
    {
        _careLinkRepository.FindUnclosedByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns((CareLink?)null, _newLink);

        var result = await Redeem(replaceActiveLink: true);

        Assert.IsType<Result<InvitationRedemptionOutcome, CareRelationshipError>.Success>(result);
        Assert.IsType<InvitationRedeemed>(Assert.Single(Fakes.Published(_mediator)));
        _careLinkRepository.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public void The_new_link_starts_without_consent()
    {
        Assert.Null(_newLink.Consent);
        Assert.False(_newLink.IsActive);
        Assert.False(_newLink.IsRevoked);
    }

    [Fact]
    public async Task The_revocation_closes_the_previous_evaluation_window()
    {
        var windows = Substitute.For<IEvaluationWindowCommandService>();
        var handler = new OnCareLinkRevokedHandler(Fakes.ScopeFactoryWith(windows),
            NullLogger<OnCareLinkRevokedHandler>.Instance);
        Fakes.Route(_mediator, handler);

        await Redeem(replaceActiveLink: true);

        await windows.Received(1).Handle(
            new CloseEvaluationWindowCommand(PatientId, _previousLink.RevokedAt!.Value, 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_previous_practitioner_loses_access()
    {
        await Redeem(replaceActiveLink: true);

        // After the switch the only link that could grant access is the new one: inactive until
        // consent, and with another practitioner once consent arrives.
        var queries = Substitute.For<ICareLinkQueryService>();
        var facade = new CareRelationshipContextFacade(queries);

        queries.Handle(Arg.Any<GetActiveCareLinkByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((CareLink?)null);
        Assert.False(await facade.IsCareLinkActive(PatientId, PreviousPractitionerId));

        _newLink.GrantConsent(new GrantConsentCommand(2, PatientId, "diary,self-weigh-ins,active-targets"));
        queries.Handle(Arg.Any<GetActiveCareLinkByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(_newLink);
        Assert.False(await facade.IsCareLinkActive(PatientId, PreviousPractitionerId));
        Assert.True(await facade.IsCareLinkActive(PatientId, NewPractitionerId));
    }

    [Fact]
    public async Task Withdrawing_consent_still_revokes_with_its_own_reason()
    {
        var link = ConsentedLink(PreviousPractitionerId, 3);
        _careLinkRepository.FindByIdAsync(3, Arg.Any<CancellationToken>()).Returns(link);
        var service = new CareLinkCommandService(_careLinkRepository, _unitOfWork,
            Substitute.For<IIamContextFacade>(), NullLogger<CareLinkCommandService>.Instance, _mediator);

        await service.Handle(new RevokeCareLinkCommand(3));

        Assert.Equal(RevocationReason.ConsentWithdrawn, link.RevocationReason);
        var revoked = Assert.IsType<CareLinkRevoked>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal(RevocationReason.ConsentWithdrawnValue, revoked.Reason);
    }

    [Fact]
    public void A_link_cannot_be_revoked_twice()
    {
        _previousLink.Revoke(RevocationReason.SwitchedPractitioner);

        Assert.Throws<InvalidOperationException>(() => _previousLink.Revoke(RevocationReason.ConsentWithdrawn));
        Assert.Equal(RevocationReason.SwitchedPractitioner, _previousLink.RevocationReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Discharged")]
    [InlineData("Whatever")]
    public void Revocation_reason_accepts_only_the_known_values(string value)
    {
        Assert.Throws<ArgumentException>(() => new RevocationReason(value));
    }

    [Fact]
    public void Revocation_reason_normalises_the_known_values()
    {
        Assert.Equal(RevocationReason.SwitchedPractitioner, new RevocationReason("switchedpractitioner"));
    }

    private Task<Result<InvitationRedemptionOutcome, CareRelationshipError>> Redeem(bool replaceActiveLink)
    {
        _invitationRepository.FindByTokenAsync(Arg.Any<InvitationToken>(), Arg.Any<CancellationToken>())
            .Returns(_invitation);
        var service = new InvitationCommandService(_invitationRepository, _careLinkRepository, _unitOfWork,
            Substitute.For<IIamContextFacade>(), NullLogger<InvitationCommandService>.Instance, _mediator);
        return service.Handle(new RedeemInvitationCommand(_invitation.Token.Value, PatientId, replaceActiveLink));
    }

    private static Invitation InvitationFrom(int practitionerId)
    {
        return Identity.Assign(
            new Invitation(new IssueInvitationCommand(practitionerId, DateTimeOffset.UtcNow.AddDays(1))),
            new InvitationId(10 + practitionerId));
    }

    private static CareLink ConsentedLink(int practitionerId, int id)
    {
        var link = Identity.Assign(
            new CareLink(new EstablishCareLinkCommand(PatientId, practitionerId, 1)), new CareLinkId(id));
        link.GrantConsent(new GrantConsentCommand(id, PatientId, "diary,self-weigh-ins,active-targets"));
        return link;
    }

    private static void AssertFailure(Result<InvitationRedemptionOutcome, CareRelationshipError> result,
        CareRelationshipError expected)
    {
        var failure = Assert.IsType<Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure>(result);
        Assert.Equal(expected, failure.Error);
    }
}
