using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Application.Internal.CommandServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.CareRelationship;

/// <summary>
///     Characterization of the rule Patient Cannot Self Link (Subflow 2.2), checked before the
///     single-use token is burned.
/// </summary>
public class PatientCannotSelfLinkTests
{
    private const int PractitionerId = 42;

    private readonly IInvitationRepository _invitationRepository = Substitute.For<IInvitationRepository>();
    private readonly ICareLinkRepository _careLinkRepository = Substitute.For<ICareLinkRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Invitation _invitation;

    public PatientCannotSelfLinkTests()
    {
        _invitation = Identity.Assign(
            new Invitation(new IssueInvitationCommand(PractitionerId, DateTimeOffset.UtcNow.AddDays(1))),
            new InvitationId(1));
        _invitationRepository.FindByTokenAsync(Arg.Any<InvitationToken>(), Arg.Any<CancellationToken>())
            .Returns(_invitation);
        // CR-1: the redemption reads the unclosed link itself (it may have to revoke it), instead of
        // only asking whether one exists. No link here, as before.
        _careLinkRepository.FindUnclosedByPatientIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((CareLink?)null);
    }

    [Fact]
    public async Task The_issuer_cannot_redeem_their_own_invitation_and_the_token_survives()
    {
        var result = await CreateService().Handle(
            new RedeemInvitationCommand(_invitation.Token.Value, PractitionerId));

        var failure = Assert.IsType<Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure>(result);
        Assert.Equal(CareRelationshipError.PatientCannotSelfLink, failure.Error);
        Assert.False(_invitation.IsRedeemed);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task Another_person_can_redeem_the_same_invitation()
    {
        var result = await CreateService().Handle(
            new RedeemInvitationCommand(_invitation.Token.Value, PractitionerId + 1));

        Assert.IsType<Result<InvitationRedemptionOutcome, CareRelationshipError>.Success>(result);
        Assert.True(_invitation.IsRedeemed);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_issuer_cannot_self_link_even_when_confirming_a_switch_and_nothing_is_revoked()
    {
        // CR-1: the self-link guard runs before the replace-active-link path.
        var currentLink = Identity.Assign(
            new CareLink(new EstablishCareLinkCommand(PractitionerId, PractitionerId + 7, 99)),
            new CareLinkId(5));
        _careLinkRepository.FindUnclosedByPatientIdAsync(PractitionerId, Arg.Any<CancellationToken>())
            .Returns(currentLink);

        var result = await CreateService().Handle(
            new RedeemInvitationCommand(_invitation.Token.Value, PractitionerId, ReplaceActiveLink: true));

        var failure = Assert.IsType<Result<InvitationRedemptionOutcome, CareRelationshipError>.Failure>(result);
        Assert.Equal(CareRelationshipError.PatientCannotSelfLink, failure.Error);
        Assert.False(_invitation.IsRedeemed);
        Assert.False(currentLink.IsRevoked);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    private InvitationCommandService CreateService()
    {
        return new InvitationCommandService(_invitationRepository, _careLinkRepository, _unitOfWork,
            Substitute.For<IIamContextFacade>(), NullLogger<InvitationCommandService>.Instance, _mediator);
    }
}
