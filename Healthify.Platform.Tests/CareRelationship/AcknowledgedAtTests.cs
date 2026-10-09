using Healthify.Platform.CareRelationship.Application.Acl;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;

namespace Healthify.Platform.Tests.CareRelationship;

/// <summary>
///     CR-3. The acknowledgement of the targets records when it was given (PAC-4 "Ana las vio el mismo día"), and
///     the moment reaches the read status and the facade.
/// </summary>
public class AcknowledgedAtTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    [Fact]
    public void Acknowledging_records_the_moment_and_nothing_else_about_the_plan()
    {
        var link = ActiveLink();
        link.MarkTargetsPending(3);
        Assert.Null(link.LastAcknowledgedAt);

        var before = DateTimeOffset.UtcNow;
        link.AcknowledgeActiveTargets(3);

        Assert.Equal(3, link.LastAcknowledgedVersion);
        Assert.NotNull(link.LastAcknowledgedAt);
        Assert.InRange(link.LastAcknowledgedAt!.Value, before, DateTimeOffset.UtcNow);
        Assert.Null(link.PendingTargetsVersion);
    }

    [Fact]
    public void A_rejected_acknowledgement_does_not_record_a_moment()
    {
        var link = ActiveLink();
        link.MarkTargetsPending(2);

        Assert.Throws<ArgumentException>(() => link.AcknowledgeActiveTargets(3));
        Assert.Null(link.LastAcknowledgedAt);
    }

    [Fact]
    public async Task The_read_status_and_the_facade_carry_the_moment()
    {
        var link = ActiveLink();
        link.MarkTargetsPending(1);
        link.AcknowledgeActiveTargets(1);

        var status = TargetsReadStatusResourceAssembler.ToResource(link);
        Assert.Equal(link.LastAcknowledgedAt, status.LastAcknowledgedAt);
        Assert.False(status.HasPendingAcknowledgement);

        var queries = Substitute.For<ICareLinkQueryService>();
        queries.Handle(Arg.Any<GetActiveCareLinkByPatientIdQuery>(), Arg.Any<CancellationToken>()).Returns(link);
        var item = await new CareRelationshipContextFacade(queries).GetActiveCareLinkByPatientId(PatientId);

        Assert.Equal(link.LastAcknowledgedAt, item!.LastAcknowledgedAt);
        Assert.Equal(1, item.LastAcknowledgedVersion);
    }

    private static CareLink ActiveLink()
    {
        var link = Identity.Assign(new CareLink(new EstablishCareLinkCommand(PatientId, PractitionerId, 1)),
            new CareLinkId(1));
        link.GrantConsent(new GrantConsentCommand(1, PatientId, "diary,self-weigh-ins,active-targets"));
        return link;
    }
}
