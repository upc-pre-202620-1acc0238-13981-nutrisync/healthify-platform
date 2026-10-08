using System.Security.Claims;
using Healthify.Platform.CareRelationship.Application.Acl;
using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Acl;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST;
using Healthify.Platform.ReadModels.Interfaces.REST.Resources;
using Healthify.Platform.ReadModels.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Resources;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Healthify.Platform.Tests.ReadModels;

/// <summary>
///     RM-1. The roster of a practitioner (PR1): one row per Active or PendingConsent link with the name, the
///     care status (HasBaseline, version in force, consultation in progress, open item), IsNew and the next
///     visit, read with one batch call per facade, never one per patient.
/// </summary>
public class PatientRosterComposerTests
{
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly ICareRelationshipContextFacade _care = Substitute.For<ICareRelationshipContextFacade>();
    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly INutritionalCareContextFacade _nutritionalCare = Substitute.For<INutritionalCareContextFacade>();
    private readonly IMonitoringContextFacade _monitoring = Substitute.For<IMonitoringContextFacade>();

    [Fact]
    public async Task Every_facade_is_read_once_for_the_whole_roster()
    {
        var since = new DateTimeOffset(2026, 3, 12, 14, 0, 0, TimeSpan.Zero);
        _care.GetRosterCareLinks(PractitionerId, Arg.Any<CancellationToken>()).Returns([
            new RosterCareLinkItem(1, 10, "Active", since),
            new RosterCareLinkItem(2, 11, "Active", since.AddMonths(3)),
            new RosterCareLinkItem(3, 12, "PendingConsent", since.AddMonths(5))
        ]);
        _iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
            {
                [10] = new(10, "ana@correo.com", "Patient", "Ana", "Flores"),
                [11] = new(11, "luz@correo.com", "Patient", "Luz", "Ramírez")
            });
        _nutritionalCare.GetCareStatusByPatientIds(PractitionerId, Arg.Any<IEnumerable<int>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, PatientCareStatusItem>
            {
                [10] = new(10, true, 3, false, true),
                [11] = new(11, false, null, true, false),
                [12] = new(12, true, null, false, false)
            });
        _monitoring.GetNextFollowUpsByPatientIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, NextFollowUpItem>
            {
                [10] = new(8, 10, since.AddMonths(7), "InPerson", [], since)
            });

        var roster = (await Composer().Compose(PractitionerId))
            .Select(PatientRosterItemResourceAssembler.ToResource).ToList();

        await _iam.Received(1).GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
        await _nutritionalCare.Received(1).GetCareStatusByPatientIds(PractitionerId, Arg.Any<IEnumerable<int>>(),
            Arg.Any<CancellationToken>());
        await _monitoring.Received(1).GetNextFollowUpsByPatientIds(Arg.Any<IEnumerable<int>>(),
            Arg.Any<CancellationToken>());
        await _iam.DidNotReceiveWithAnyArgs().GetUserById(default, default);

        Assert.Equal([10, 11, 12], roster.Select(r => r.PatientId));

        var ana = roster[0];
        Assert.Equal(("Ana Flores", 'A', "Active", since), (ana.FullName, ana.Initial, ana.LinkStatus, ana.LinkedSince));
        Assert.True(ana.HasBaseline);
        Assert.Equal(3, ana.ActivePlanVersion);
        Assert.False(ana.IsNew);
        Assert.True(ana.HasOpenReviewItem);
        Assert.Equal(since.AddMonths(7), ana.NextFollowUpAt);

        var luz = roster[1];
        Assert.False(luz.HasBaseline);
        Assert.True(luz.HasConsultationInProgress);
        Assert.True(luz.IsNew);
        Assert.Null(luz.NextFollowUpAt);

        // "Luz Ramírez · Vinculada desde 28 ago. 2026 · sin plan · Nueva": with baseline but no plan, still new.
        var pending = roster[2];
        Assert.Equal(("PendingConsent", true, null as int?, true), (pending.LinkStatus, pending.HasBaseline,
            pending.ActivePlanVersion, pending.IsNew));
        Assert.Equal(("", '?'), (pending.FullName, pending.Initial));
    }

    [Fact]
    public async Task An_empty_roster_asks_nobody_else()
    {
        _care.GetRosterCareLinks(PractitionerId, Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await Composer().Compose(PractitionerId));

        await _iam.DidNotReceiveWithAnyArgs().GetUsersByIds(default!, default);
        await _nutritionalCare.DidNotReceiveWithAnyArgs().GetCareStatusByPatientIds(default, default!, default);
    }

    [Fact]
    public async Task The_roster_of_another_practitioner_is_403()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString(c.Arg<string>(), c.Arg<string>()));
        var controller = new PatientRosterController(Composer(), localizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "21"), new Claim(ClaimTypes.Role, "Practitioner")],
                        "test"))
                }
            }
        };

        var result = await controller.GetPatientRoster(PractitionerId);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        await _care.DidNotReceiveWithAnyArgs().GetRosterCareLinks(default, default);
    }

    [Fact]
    public async Task Only_active_and_pending_consent_links_reach_the_roster()
    {
        var active = Link(1, 10);
        active.GrantConsent(new GrantConsentCommand(1, 10, "Full"));
        var pending = Link(2, 11);
        var withdrawn = Link(3, 12);
        withdrawn.GrantConsent(new GrantConsentCommand(3, 12, "Full"));
        withdrawn.WithdrawConsent();
        var revoked = Link(4, 13);
        revoked.Revoke(RevocationReason.SwitchedPractitioner);
        var discharged = Link(5, 14);
        discharged.GrantConsent(new GrantConsentCommand(5, 14, "Full"));
        discharged.Discharge(new ClinicalReason("Metas alcanzadas"));
        var queries = Substitute.For<ICareLinkQueryService>();
        queries.Handle(Arg.Any<GetCareLinksByPractitionerIdQuery>(), Arg.Any<CancellationToken>())
            .Returns([active, pending, withdrawn, revoked, discharged]);

        var links = await new CareRelationshipContextFacade(queries).GetRosterCareLinks(PractitionerId);

        Assert.Equal([(10, "Active"), (11, "PendingConsent")], links.Select(l => (l.PatientId, l.LinkStatus)));
    }

    [Fact]
    public async Task The_care_status_comes_from_one_batch_read_per_kind()
    {
        var care = new InMemoryNutritionalCare(Today);
        care.Seed(ConsultationScenario.Baseline(10, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m, Today));
        care.Seed(ConsultationScenario.Baseline(11, PractitionerId, new DateOnly(1990, 1, 1), "Male", 175m, Today));
        await ConsultationFlow.CompleteAsync(care.Consultation, 10, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "publication");
        await ConsultationFlow.StartAsync(care.Consultation, 11, PractitionerId);
        var reviewItems = Substitute.For<IReviewItemQueryService>();
        reviewItems.Handle(Arg.Any<GetOpenReviewItemsByPractitionerIdQuery>(), Arg.Any<CancellationToken>())
            .Returns([new ReviewItem(new OpenReviewItemCommand(12, SignalType.SustainedDeviation, "3 días"),
                PractitionerId)]);
        var facade = new NutritionalCareContextFacade(care.PlanQueries, reviewItems, care.BaselineQueries,
            care.Queries, new FixedClinicalDate(Today),
            Substitute.For<INutritionalAssessmentQueryService>(), Substitute.For<INutritionalDiagnosisQueryService>());

        var statuses = await facade.GetCareStatusByPatientIds(PractitionerId, [10, 11, 12]);

        Assert.Equal(new PatientCareStatusItem(10, true, 1, false, false), statuses[10]);
        Assert.Equal(new PatientCareStatusItem(11, true, null, true, false), statuses[11]);
        Assert.Equal(new PatientCareStatusItem(12, false, null, false, true), statuses[12]);
    }

    private static CareLink Link(int id, int patientId)
    {
        return Identity.Assign(new CareLink(new EstablishCareLinkCommand(patientId, PractitionerId, 99)),
            new CareLinkId(id));
    }

    private PatientRosterComposer Composer()
    {
        return new PatientRosterComposer(_care, _iam, _nutritionalCare, _monitoring);
    }
}
