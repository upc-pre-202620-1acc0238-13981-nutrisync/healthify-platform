using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     RM-4 (DECISIÓN §12-#8). A referral is Open ("en curso") until the practitioner who recorded it closes it by
///     hand; it is closed once.
/// </summary>
public class ReferralClosureTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    private readonly IReferralRepository _referrals = Substitute.For<IReferralRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Referral _referral;

    public ReferralClosureTests()
    {
        _referral = Identity.Assign(
            new Referral(new RecordReferralCommand(PatientId, PractitionerId, "Endocrinología", "Control de tiroides")),
            new ReferralId(7));
        _referrals.FindByIdAsync(7, Arg.Any<CancellationToken>()).Returns(_referral);
    }

    [Fact]
    public void A_new_referral_is_open_and_closes_once()
    {
        Assert.Equal(Referral.Open, _referral.Status);
        Assert.Null(_referral.ClosedAt);

        _referral.Close(DateTimeOffset.UtcNow);

        Assert.Equal(Referral.Closed, _referral.Status);
        Assert.NotNull(_referral.ClosedAt);
        Assert.Throws<InvalidOperationException>(() => _referral.Close(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task The_practitioner_who_recorded_it_closes_it_and_the_event_follows_the_commit()
    {
        var result = await Service().Handle(new CloseReferralCommand(7, PractitionerId));

        Assert.False(Assert.IsType<Result<Referral, MonitoringError>.Success>(result).Value.IsOpen);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        Assert.Single(Fakes.Published(_mediator).OfType<ReferralClosed>());
    }

    [Fact]
    public async Task Another_practitioner_does_not_find_it_and_a_closed_one_is_a_conflict()
    {
        var other = await Service().Handle(new CloseReferralCommand(7, PractitionerId + 1));
        _referral.Close(DateTimeOffset.UtcNow);
        var again = await Service().Handle(new CloseReferralCommand(7, PractitionerId));

        Assert.Equal(MonitoringError.ReferralNotFound,
            Assert.IsType<Result<Referral, MonitoringError>.Failure>(other).Error);
        Assert.Equal(MonitoringError.ReferralAlreadyClosed,
            Assert.IsType<Result<Referral, MonitoringError>.Failure>(again).Error);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync();
    }

    private ReferralCommandService Service()
    {
        return new ReferralCommandService(_referrals, Substitute.For<ICareRelationshipContextFacade>(), _unitOfWork,
            NullLogger<ReferralCommandService>.Instance, _mediator);
    }
}
