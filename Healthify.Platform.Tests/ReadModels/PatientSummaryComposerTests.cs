using System.Security.Claims;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
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
///     RM-2. The patient summary (PAC-1) composed from the five facades: PAC-0 when there is no baseline, "desde
///     la última consulta" from the last published consultation (or 7 days / 4 weeks before the first one), and
///     a quiet facade leaves only its own section empty.
/// </summary>
public class PatientSummaryComposerTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 18);

    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly ICareRelationshipContextFacade _care = Substitute.For<ICareRelationshipContextFacade>();
    private readonly INutritionalCareContextFacade _nutritionalCare = Substitute.For<INutritionalCareContextFacade>();
    private readonly IIntakeContextFacade _intake = Substitute.For<IIntakeContextFacade>();
    private readonly IMonitoringContextFacade _monitoring = Substitute.For<IMonitoringContextFacade>();

    public PatientSummaryComposerTests()
    {
        _iam.GetUserById(PatientId, Arg.Any<CancellationToken>())
            .Returns(new UserIdentityItem(PatientId, "ana@correo.com", "Patient", "Ana", "Flores"));
        _care.GetActiveCareLinkByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new CareLinkStatusItem(3, PatientId, PractitionerId, true, true, 2,
                new DateTimeOffset(2026, 3, 12, 14, 0, 0, TimeSpan.Zero)));
        _monitoring.GetNextFollowUpsByPatientIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, NextFollowUpItem>());
    }

    [Fact]
    public async Task A_patient_without_baseline_gets_Baseline_null_so_the_app_shows_PAC_0()
    {
        _nutritionalCare.GetBaselineSummary(PatientId, Arg.Any<CancellationToken>())
            .Returns((PatientBaselineSummaryItem?)null);

        var resource = PatientSummaryResourceAssembler.ToResource(await Composer().Compose(PatientId));

        Assert.Null(resource.Baseline);
        Assert.Null(resource.ActivePlanVersion);
        Assert.Equal("Ana Flores", resource.FullName);
        Assert.Equal("Active", resource.LinkStatus);
        Assert.Equal(new DateTimeOffset(2026, 3, 12, 14, 0, 0, TimeSpan.Zero), resource.LinkedSince);
    }

    [Fact]
    public async Task With_baseline_plan_visit_and_consultation_in_progress_every_section_is_filled()
    {
        _nutritionalCare.GetBaselineSummary(PatientId, Arg.Any<CancellationToken>())
            .Returns(new PatientBaselineSummaryItem(PatientId, "Female", 31, 168m, ["Hypothyroidism"]));
        _nutritionalCare.GetActiveTargetsByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new ActiveTargetsItem(PatientId, 3, Now.AddDays(-14), 1796m, 90m, 200m, 60m, [], []));
        _nutritionalCare.GetConsultationInProgress(PatientId, Arg.Any<CancellationToken>())
            .Returns(new ConsultationInProgressItem(44, 1, "Measurement", Now.AddHours(-1)));
        _monitoring.GetNextFollowUpsByPatientIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, NextFollowUpItem>
            {
                [PatientId] = new(8, PatientId, Now.AddDays(5), "InPerson", ["Fasting"], Now.AddDays(-10))
            });

        var resource = PatientSummaryResourceAssembler.ToResource(await Composer().Compose(PatientId));

        Assert.Equal(("Female", 31, 168m), (resource.Baseline!.BiologicalSex, resource.Baseline.AgeYears,
            resource.Baseline.HeightCm));
        Assert.Equal(["Hypothyroidism"], resource.Baseline.Conditions);
        Assert.Equal(3, resource.ActivePlanVersion);
        Assert.Equal((44, 1, "Measurement"), (resource.ConsultationInProgress!.ConsultationId,
            resource.ConsultationInProgress.StepNumber, resource.ConsultationInProgress.StepName));
        Assert.Equal(8, resource.NextFollowUp!.FollowUpId);
        Assert.Equal(["Fasting"], resource.NextFollowUp.Preparation);
    }

    [Fact]
    public async Task Since_the_last_consultation_starts_on_the_day_it_was_published()
    {
        var lastConsultation = new DateTimeOffset(2026, 9, 3, 16, 0, 0, TimeSpan.Zero);
        _nutritionalCare.GetLastCompletedConsultationAt(PatientId, Arg.Any<CancellationToken>())
            .Returns(lastConsultation);
        _intake.GetWeightTrendSummary(PatientId, 3, Arg.Any<CancellationToken>())
            .Returns(new WeightTrendSummaryItem(-0.3m, -0.6m, 12));
        _monitoring.GetComplianceSummary(PatientId, new DateOnly(2026, 9, 3), Today, Arg.Any<CancellationToken>())
            .Returns(new ComplianceSummaryItem(10, 1, 2, 3, 13, 16));

        var since = PatientSummaryResourceAssembler.ToResource(await Composer().Compose(PatientId))
            .SinceLastConsultation;

        // 3 to 18 September: 16 days, 3 weeks of trend.
        Assert.Equal(new DateOnly(2026, 9, 3), since.FromDate);
        Assert.Equal(3, since.TrendWeeks);
        Assert.Equal(-0.3m, since.WeightSlopeKgPerWeek);
        Assert.Equal(new ComplianceRatioResource(10, 16), since.Compliance);
    }

    [Fact]
    public async Task Before_the_first_consultation_it_covers_the_last_7_days_and_4_weeks()
    {
        _nutritionalCare.GetLastCompletedConsultationAt(PatientId, Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        _intake.GetWeightTrendSummary(PatientId, 4, Arg.Any<CancellationToken>())
            .Returns(new WeightTrendSummaryItem(-0.3m, -1.2m, 20));
        _monitoring.GetComplianceSummary(PatientId, new DateOnly(2026, 9, 12), Today, Arg.Any<CancellationToken>())
            .Returns(new ComplianceSummaryItem(5, 0, 1, 1, 6, 7));

        var since = PatientSummaryResourceAssembler.ToResource(await Composer().Compose(PatientId))
            .SinceLastConsultation;

        Assert.Equal(new DateOnly(2026, 9, 12), since.FromDate);
        Assert.Equal(4, since.TrendWeeks);
        Assert.Equal(new ComplianceRatioResource(5, 7), since.Compliance);
    }

    [Fact]
    public async Task A_quiet_facade_leaves_only_its_own_section_empty()
    {
        _iam.GetUserById(PatientId, Arg.Any<CancellationToken>()).Returns((UserIdentityItem?)null);
        _intake.GetWeightTrendSummary(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((WeightTrendSummaryItem?)null);
        _monitoring.GetComplianceSummary(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>()).Returns((ComplianceSummaryItem?)null);
        _nutritionalCare.GetBaselineSummary(PatientId, Arg.Any<CancellationToken>())
            .Returns(new PatientBaselineSummaryItem(PatientId, "Female", 31, 168m, []));

        var resource = PatientSummaryResourceAssembler.ToResource(await Composer().Compose(PatientId));

        Assert.Null(resource.FullName);
        Assert.Null(resource.SinceLastConsultation.WeightSlopeKgPerWeek);
        Assert.Null(resource.SinceLastConsultation.Compliance);
        Assert.NotNull(resource.Baseline);
        Assert.NotNull(resource.LinkedSince);
    }

    [Fact]
    public async Task A_practitioner_without_a_live_link_gets_403_and_nothing_is_composed()
    {
        _care.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(false);
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString(c.Arg<string>(), c.Arg<string>()));
        var controller = new PatientSummaryController(Composer(), _care, localizer)
        {
            ControllerContext = new ControllerContext { HttpContext = AuthenticatedAs(PractitionerId) }
        };

        var result = await controller.GetPatientSummary(PatientId);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        await _nutritionalCare.DidNotReceiveWithAnyArgs().GetBaselineSummary(default, default);
    }

    [Fact]
    public async Task The_linked_practitioner_gets_the_summary()
    {
        _care.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        _nutritionalCare.GetBaselineSummary(PatientId, Arg.Any<CancellationToken>())
            .Returns((PatientBaselineSummaryItem?)null);
        var controller = new PatientSummaryController(Composer(), _care,
            Substitute.For<IStringLocalizer<SharedResource>>())
        {
            ControllerContext = new ControllerContext { HttpContext = AuthenticatedAs(PractitionerId) }
        };

        var result = await controller.GetPatientSummary(PatientId);

        var summary = Assert.IsType<PatientSummaryResource>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(PatientId, summary.PatientId);
    }

    private PatientSummaryComposer Composer()
    {
        return new PatientSummaryComposer(_iam, _care, _nutritionalCare, _intake, _monitoring,
            new FixedTimeProvider(Now));
    }

    private static DefaultHttpContext AuthenticatedAs(int userId)
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Practitioner")],
                "test"))
        };
    }
}
