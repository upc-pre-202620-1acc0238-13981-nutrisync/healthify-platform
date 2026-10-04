using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.REST;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-8 acceptance: the patient reads their plan versions with "Qué cambió", the summary travels in
///     ActiveTargetsUpdated to the Intake cache, and the patient never receives the diagnosis or the calculation
///     basis (Diagnosis And Basis Never Leave The Context).
/// </summary>
public class PatientPlanVersionsTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static readonly string[] ForbiddenForThePatient =
    [
        "CalculationBasis", "DiagnosisId", "Diagnosis", "OverrideReason", "ChangeReason", "Outcome", "Proposal",
        "Rationale", "Basis", "Equation", "ReferenceWeight", "ActivityFactor", "Deficit", "Bmi", "PractitionerId"
    ];

    private readonly InMemoryNutritionalCare _care = new(Today);

    public PatientPlanVersionsTests()
    {
        _care.Seed(ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
            Today));
    }

    [Fact]
    public async Task The_second_consultation_stores_what_changed_and_the_patient_cache_receives_it()
    {
        var first = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        var second = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second-publication");

        Assert.Empty(first.Plan.ChangesFromPrevious!);
        var changes = second.Plan.ChangesFromPrevious!;
        Assert.Contains(changes, c => c.Type == PlanChange.EnergyChanged &&
                                      c.From == first.Plan.PrescribedTargets!.EnergyKcal &&
                                      c.To == second.Plan.PrescribedTargets!.EnergyKcal);
        // Same guidelines and restrictions in both publications: nothing added or removed.
        Assert.DoesNotContain(changes, c => c.Type is PlanChange.GuidelineAdded or PlanChange.RestrictionAdded);

        var updated = Fakes.Published(_care.Mediator).OfType<ActiveTargetsUpdated>().Last();
        Assert.Equal(changes.Select(c => (c.Type, c.Code, c.Macro, c.From, c.To)),
            updated.ChangesFromPrevious!.Select(c => (c.Type, c.Code, c.Macro, c.From, c.To)));

        var cache = Assert.Single(_care.Caches);
        Assert.Equal(changes.Select(c => (c.Type, c.Code, c.Macro, c.From, c.To)),
            cache.ChangesFromPrevious!.Select(c => (c.Type, c.Code, c.Macro, c.From, c.To)));
    }

    [Fact]
    public async Task The_patient_lists_published_versions_only_most_recent_first()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second-publication");
        // A third consultation left at step 3: its draft never reached the patient.
        var third = await ConsultationFlow.StartAsync(_care.Consultation, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(_care.Consultation, third, PractitionerId, 73m);
        await ConsultationFlow.DiagnoseAsync(_care.Consultation, third, PractitionerId, DiagnosisCode.OverweightGradeI);
        await ConsultationFlow.TargetsAsync(_care.Consultation, third, PractitionerId);

        var versions = await _care.PlanQueries.Handle(new GetPatientPlanVersionsQuery(PatientId));

        Assert.Equal([2, 1], versions.Select(v => v.Version));
        Assert.True(versions[0].IsActive);
        Assert.False(versions[1].IsActive);
        Assert.Empty(versions[1].ChangesFromPrevious);
        Assert.NotEmpty(versions[0].ChangesFromPrevious);
        Assert.Equal([DietaryRestriction.LactoseFree], versions[0].Restrictions);
    }

    [Fact]
    public async Task A_version_published_before_NC_8_gets_its_changes_on_read_without_writing_them()
    {
        var v1 = PlanScenario.PublishedBeforeNc6(1, PatientId, PractitionerId, 1, 1, ["Caminar 20 min"], ["Vegan"]);
        v1.Supersede();
        var v2 = PlanScenario.PublishedBeforeNc6(2, PatientId, PractitionerId, 1, 2, ["Caminar 20 min"], []);
        _care.Plans.AddRange([v1, v2]);

        var versions = await _care.PlanQueries.Handle(new GetPatientPlanVersionsQuery(PatientId));

        Assert.Equal([PlanChange.NoTargetsChanged(), PlanChange.RestrictionWasRemoved(DietaryRestriction.Vegan)],
            versions[0].ChangesFromPrevious);
        Assert.Null(v2.ChangesFromPrevious);
    }

    [Fact]
    public void The_patient_resource_and_its_DTO_never_carry_the_diagnosis_or_the_basis()
    {
        foreach (var type in new[] { typeof(PatientPlanVersionResource), typeof(PatientPlanVersion) })
        {
            var names = type.GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain(names, n => ForbiddenForThePatient.Any(f => n.Contains(f, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task The_serialized_response_says_nothing_about_the_diagnosis_or_the_basis()
    {
        await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        var second = await ConsultationFlow.CompleteAsync(_care.Consultation, PatientId, PractitionerId, 74.2m,
            DiagnosisCode.OverweightGradeI, "second-publication");

        var versions = await _care.PlanQueries.Handle(new GetPatientPlanVersionsQuery(PatientId));
        var json = JsonSerializer.Serialize(versions.Select(PatientPlanVersionResourceAssembler.ToResource));

        Assert.DoesNotContain("basis", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosis", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("override", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DiagnosisCode.OverweightGradeI, json);
        Assert.DoesNotContain(second.Plan.CalculationBasis.Equation.Value, json);
        Assert.DoesNotContain(second.Plan.ChangeReason!.Value, json);
    }

    [Fact]
    public async Task Only_the_patient_reads_their_own_versions()
    {
        var authorize = typeof(PatientPlanVersionsController).GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal("Patient", authorize.Roles);

        var queries = Substitute.For<INutritionPlanQueryService>();
        var controller = new PatientPlanVersionsController(queries)
        {
            ControllerContext = new ControllerContext { HttpContext = AuthenticatedAs(PatientId + 1) }
        };

        Assert.IsType<ForbidResult>(await controller.GetMyPlanVersions(PatientId));
        await queries.DidNotReceiveWithAnyArgs().Handle(default(GetPatientPlanVersionsQuery)!);
    }

    private static DefaultHttpContext AuthenticatedAs(int userId)
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Patient")],
                "test"))
        };
    }
}
