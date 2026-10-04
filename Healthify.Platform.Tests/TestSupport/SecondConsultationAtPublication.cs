using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     NC-7. A patient with an active diagnosis (1) and an active version (v1) from a first consultation, and a
///     second consultation at step 4: its diagnosis (2) is pending and its draft (v2) prescribed.
/// </summary>
public sealed class SecondConsultationAtPublication
{
    public const int PatientId = 10;
    public const int PractitionerId = 20;
    public const int ConsultationId = 41;

    public SecondConsultationAtPublication()
    {
        var today = new DateOnly(2026, 9, 18);
        Iam.IsPractitioner(PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        CareRelationship.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);

        var baseline = ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female",
            168m, today);
        var older = ConsultationScenario.MeasuredAssessment(90, 40, PractitionerId, baseline, today.AddMonths(-6), 80m);
        ActiveDiagnosis = ConsultationScenario.Diagnosis(1, older, DiagnosisCode.ObesityGradeI);
        ActivePlan = PlanScenario.Prescribed(400, PatientId, PractitionerId, 1, 1);
        ActivePlan.PublishWith([], []);

        var assessment = ConsultationScenario.MeasuredAssessment(101, ConsultationId, PractitionerId, baseline, today,
            74.2m);
        Pending = ConsultationScenario.PendingDiagnosis(2, assessment, DiagnosisCode.OverweightGradeI, ConsultationId);
        Draft = PlanScenario.Prescribed(401, PatientId, PractitionerId, 2, 2);
        Consultation = ConsultationScenario.AtPublication(ConsultationId, PatientId, PractitionerId, 101, 2, 401);

        Consultations.FindByIdAsync(ConsultationId, Arg.Any<CancellationToken>()).Returns(Consultation);
        Diagnoses.FindByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Pending);
        Diagnoses.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(ActiveDiagnosis);
        Plans.FindByIdAsync(401, Arg.Any<CancellationToken>()).Returns(Draft);
        Plans.FindActiveByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(ActivePlan);

        Service = new ConsultationCommandService(Consultations, Substitute.For<IPatientBaselineRepository>(),
            Substitute.For<INutritionalAssessmentRepository>(), Diagnoses, Plans, UnitOfWork, Iam, CareRelationship,
            new FixedClinicalDate(today), Substitute.For<IBmrCalculator>(),
            Substitute.For<IDefaultTargetParametersPolicy>(), NullLogger<ConsultationCommandService>.Instance,
            Mediator);
    }

    public IConsultationRepository Consultations { get; } = Substitute.For<IConsultationRepository>();
    public INutritionalDiagnosisRepository Diagnoses { get; } = Substitute.For<INutritionalDiagnosisRepository>();
    public INutritionPlanRepository Plans { get; } = Substitute.For<INutritionPlanRepository>();
    public TransactionalUnitOfWork UnitOfWork { get; } = new();
    public IIamContextFacade Iam { get; } = Substitute.For<IIamContextFacade>();
    public ICareRelationshipContextFacade CareRelationship { get; } = Substitute.For<ICareRelationshipContextFacade>();
    public IMediator Mediator { get; } = Substitute.For<IMediator>();

    public NutritionalDiagnosis ActiveDiagnosis { get; }
    public NutritionalDiagnosis Pending { get; }
    public NutritionPlan ActivePlan { get; }
    public NutritionPlan Draft { get; }
    public Consultation Consultation { get; }
    public ConsultationCommandService Service { get; }

    public PublishFromConsultationCommand Publish()
    {
        return new PublishFromConsultationCommand(ConsultationId, PractitionerId, [], [Guideline.ReduceSalt], []);
    }
}
