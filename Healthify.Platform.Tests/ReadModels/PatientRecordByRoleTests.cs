using System.Security.Claims;
using System.Text.Json;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST;
using Healthify.Platform.ReadModels.Interfaces.REST.Resources;
using Healthify.Platform.Shared.Resources;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Healthify.Platform.Tests.ReadModels;

/// <summary>
///     RM-4 acceptance: <c>GET /patients/{id}/record</c> returns one resource per role. The practitioner's (PAC-3)
///     carries the active diagnosis, the evaluations with their BMI and the clinical weight; the patient's (PT20)
///     never carries a diagnosis, a calculation basis, a BMI or a BMI category (§12-#9), and is built without even
///     asking for them. Both keep every field of the original record. Referrals carry Open/Closed (§12-#8).
/// </summary>
public class PatientRecordByRoleTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const string DiagnosisCode = "OverweightGradeI";
    private const string DiagnosisStatement = "Sobrepeso grado I";

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);

    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly ICareRelationshipContextFacade _care = Substitute.For<ICareRelationshipContextFacade>();
    private readonly INutritionalCareContextFacade _nutritionalCare = Substitute.For<INutritionalCareContextFacade>();
    private readonly IIntakeContextFacade _intake = Substitute.For<IIntakeContextFacade>();
    private readonly IMonitoringContextFacade _monitoring = Substitute.For<IMonitoringContextFacade>();

    public PatientRecordByRoleTests()
    {
        _iam.GetUserById(PatientId, Arg.Any<CancellationToken>())
            .Returns(new UserIdentityItem(PatientId, "ana@example.com", "Patient", "Ana", "Flores"));
        _iam.GetUserById(PractitionerId, Arg.Any<CancellationToken>())
            .Returns(new UserIdentityItem(PractitionerId, "lucia@example.com", "Practitioner", "Lucía", "Paredes"));
        _care.GetActiveCareLinkByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new CareLinkStatusItem(3, PatientId, PractitionerId, true, true, 3,
                new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero)));
        _care.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);

        _nutritionalCare.GetActiveTargetsByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new ActiveTargetsItem(PatientId, 3, Now.AddDays(-14), 1850m, 110m, 210m, 60m,
                ["ReduceSalt", "Caminar 20 min"], ["LactoseFree"],
                [new ActiveGuidelineItem("ReduceSalt", null), new ActiveGuidelineItem(null, "Caminar 20 min")], []));
        _nutritionalCare.GetActiveDiagnosis(PatientId, Arg.Any<CancellationToken>())
            .Returns(new ActiveDiagnosisItem(5, DiagnosisCode, DiagnosisStatement, new DateTimeOffset(2026, 3, 3, 0, 0,
                0, TimeSpan.Zero)));
        _nutritionalCare.GetClinicalEvaluations(PatientId, Arg.Any<CancellationToken>()).Returns(
        [
            new ClinicalEvaluationItem(1, new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero), 76.0m, 26.9m,
                DiagnosisCode, 91m, true),
            new ClinicalEvaluationItem(2, new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero), 74.2m, 26.3m,
                DiagnosisCode, 88m, false)
        ]);
        _nutritionalCare.GetLatestClinicalMeasurement(PatientId, Arg.Any<CancellationToken>())
            .Returns(new ClinicalMeasurementItem(new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero), 74.2m,
                ["Fasting"], "en ayunas"));

        _monitoring.GetReferrals(PatientId, Arg.Any<CancellationToken>()).Returns(
        [
            new ReferralItem(7, "Endocrinología", "Control de tiroides", PractitionerId,
                new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero))
        ]);
        _monitoring.GetComplianceSummary(PatientId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(new ComplianceSummaryItem(5, 0, 1, 1, 6, 7));
        _monitoring.GetNextFollowUpsByPatientIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, NextFollowUpItem>
            {
                [PatientId] = new(4, PatientId, Now.AddDays(5), "InPerson", ["Fasting"], Now.AddDays(-10),
                    PractitionerId)
            });
        _intake.GetWeightTrendSummary(PatientId, 4, Arg.Any<CancellationToken>())
            .Returns(new WeightTrendSummaryItem(-0.3m, -1.2m, 20));
    }

    [Fact]
    public async Task The_patient_record_never_carries_the_diagnosis_the_basis_or_the_bmi()
    {
        var resource = Assert.IsType<PatientOwnRecordResource>(
            Assert.IsType<OkObjectResult>(await Controller(PatientId, "Patient").GetPatientRecord(PatientId, null))
                .Value);

        // Built without asking: the diagnosis and the BMI never reach the patient's composition.
        await _nutritionalCare.DidNotReceiveWithAnyArgs().GetActiveDiagnosis(default);
        await _nutritionalCare.DidNotReceiveWithAnyArgs().GetClinicalEvaluations(default);

        var json = JsonSerializer.Serialize<object>(resource);
        foreach (var forbidden in new[]
                 {
                     "diagnos", "bmi", "basis", "rationale", "equation", "deficit", "activityfactor", "category",
                     DiagnosisCode.ToLowerInvariant(), DiagnosisStatement.ToLowerInvariant(), "26.3", "26.9"
                 })
            Assert.DoesNotContain(forbidden, json.ToLowerInvariant());
    }

    [Fact]
    public void No_type_the_patient_record_is_built_from_names_a_diagnosis_or_a_bmi()
    {
        foreach (var type in Reachable(typeof(PatientOwnRecordResource)))
        foreach (var property in type.GetProperties())
            Assert.False(
                new[] { "Diagnosis", "Bmi", "Basis", "Category", "Rationale" }
                    .Any(f => property.Name.Contains(f, StringComparison.OrdinalIgnoreCase)),
                $"{type.Name}.{property.Name} must not reach the patient");
    }

    [Fact]
    public async Task The_patient_reads_their_practitioner_numbers_plan_and_referrals()
    {
        var resource = Assert.IsType<PatientOwnRecordResource>(
            Assert.IsType<OkObjectResult>(await Controller(PatientId, "Patient").GetPatientRecord(PatientId, null))
                .Value);

        Assert.Equal(("Lucía Paredes", "Active"), (resource.Practitioner!.FullName, resource.Practitioner.LinkStatus));
        Assert.Equal(4, resource.NextFollowUp!.FollowUpId);
        Assert.Equal((1850m, 3), (resource.MyNumbers.EnergyTargetKcal, resource.MyNumbers.PlanVersion));
        Assert.Equal((5, 7), (resource.MyNumbers.Compliance!.Met, resource.MyNumbers.Compliance.Total));
        Assert.Equal(74.2m, resource.MyNumbers.ClinicalWeight!.Kg);
        Assert.Equal(-0.3m, resource.MyNumbers.WeightSlopeKgPerWeek);
        Assert.Equal([new RecordGuidelineResource("ReduceSalt", null), new RecordGuidelineResource(null, "Caminar 20 min")],
            resource.Plan!.Guidelines);
        Assert.Equal(["LactoseFree"], resource.Plan.Restrictions);
        Assert.Equal("Open", Assert.Single(resource.Referrals).Status);
        // Every field of the original record is still there.
        Assert.Equal(PatientId, resource.PatientId);
        Assert.Equal(3, resource.Intervention!.PlanVersion);
    }

    [Fact]
    public async Task The_practitioner_reads_the_diagnosis_the_evaluations_and_the_clinical_weight()
    {
        var resource = Assert.IsType<PractitionerPatientRecordResource>(
            Assert.IsType<OkObjectResult>(
                await Controller(PractitionerId, "Practitioner").GetPatientRecord(PatientId, null)).Value);

        Assert.Equal((DiagnosisCode, DiagnosisStatement), (resource.ActiveDiagnosis!.Code, resource.ActiveDiagnosis.Statement));
        Assert.Equal([2, 1], resource.Evaluations.Select(e => e.AssessmentId));
        Assert.True(resource.Evaluations[^1].IsFirst);
        Assert.Equal(88m, resource.Evaluations[0].WaistCm);
        Assert.Equal((74.2m, -1.8m), (resource.ClinicalWeight!.LatestKg, resource.ClinicalWeight.DeltaKgSinceFirst));
        Assert.Equal(new RecordBmiResource(26.3m, DiagnosisCode), resource.Bmi);
        Assert.Equal((5, 7), (resource.Compliance!.Met, resource.Compliance.Total));
        Assert.Equal(PatientId, resource.PatientId);
    }

    [Fact]
    public async Task Another_patient_or_an_unlinked_practitioner_cannot_read_it()
    {
        var otherPatient = await Controller(PatientId + 1, "Patient").GetPatientRecord(PatientId, null);
        var unlinked = await Controller(PractitionerId + 1, "Practitioner").GetPatientRecord(PatientId, null);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(otherPatient).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(unlinked).StatusCode);
    }

    private PatientRecordController Controller(int userId, string role)
    {
        var composer = new PatientRecordComposer(_iam, _care, _nutritionalCare, _intake, _monitoring,
            new FixedTimeProvider(Now));
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(c => new LocalizedString((string)c[0], (string)c[0]));
        return new PatientRecordController(composer, _care, localizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)],
                        "test"))
                }
            }
        };
    }

    /// <summary>The resource type and every resource type its properties reach.</summary>
    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type)) continue;
            foreach (var property in type.GetProperties())
            {
                var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (propertyType.IsGenericType) propertyType = propertyType.GetGenericArguments()[0];
                if (propertyType.Namespace?.StartsWith("Healthify.Platform") == true) pending.Push(propertyType);
            }
        }

        return seen;
    }
}
