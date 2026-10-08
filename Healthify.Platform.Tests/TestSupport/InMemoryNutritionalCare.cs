using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;
using Healthify.Platform.NutritionalCare.Application.Internal.QueryServices;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Infrastructure.Ai.Lexicon;
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;
using Healthify.Platform.Shared.Infrastructure.Ai.Fakes;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;
using Microsoft.Extensions.Configuration;
using Healthify.Platform.Tests.NutritionalCare;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     The Nutritional Care side of the platform with real command and query services and in-memory
///     repositories, wired to the real policy chain NutritionPlanPublished → PublishActiveTargets →
///     ActiveTargetsUpdated → Intake cache. Every save checks the invariants the database could not enforce on
///     its own: never two active diagnoses, two active versions or two consultations in progress for a patient,
///     and never two pending diagnoses for a consultation.
/// </summary>
public sealed class InMemoryNutritionalCare
{
    public InMemoryNutritionalCare(DateOnly today)
    {
        UnitOfWork = new InvariantCheckingUnitOfWork(this);
        Iam.IsPractitioner(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        CareRelationship.IsCareLinkActive(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        CareRelationship.GetActiveCareLinkByPatientId(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new CareLinkStatusItem(1, call.ArgAt<int>(0), 20, true, true, null));

        var consultations = new ConsultationRepository(this);
        var baselines = new BaselineRepository(this);
        var assessments = new AssessmentRepository(this);
        var diagnoses = new DiagnosisRepository(this);
        var plans = new PlanRepository(this);

        var planService = new NutritionPlanCommandService(plans, diagnoses, assessments, UnitOfWork,
            new BmrCalculator(), NullLogger<NutritionPlanCommandService>.Instance, Mediator);
        PlanCommands = planService;
        var cacheService = new ActiveTargetsCacheCommandService(new CacheRepository(this), UnitOfWork,
            CareRelationship, NullLogger<ActiveTargetsCacheCommandService>.Instance, Mediator);

        // The fan-out of a publication, as Cortex.Mediator runs it: each publish awaits its handler.
        Fakes.Route(Mediator, new OnNutritionPlanPublishedHandler(
            Fakes.ScopeFactoryWith<INutritionPlanCommandService>(planService),
            NullLogger<OnNutritionPlanPublishedHandler>.Instance));
        // NC-9/NC-10: the adjustment publishes the contract through its own policy.
        Fakes.Route(Mediator, new OnNutritionPlanAdjustedHandler(
            Fakes.ScopeFactoryWith<INutritionPlanCommandService>(planService),
            NullLogger<OnNutritionPlanAdjustedHandler>.Instance));
        Fakes.Route(Mediator, new OnActiveTargetsUpdatedIntakeHandler(
            Fakes.ScopeFactoryWith<IActiveTargetsCacheCommandService>(cacheService),
            NullLogger<OnActiveTargetsUpdatedIntakeHandler>.Instance));

        Consultation = new ConsultationCommandService(consultations, baselines, assessments, diagnoses, plans,
            UnitOfWork, Iam, CareRelationship, new FixedClinicalDate(today), new BmrCalculator(),
            DefaultTargetParametersPolicyTests.Policy(), NullLogger<ConsultationCommandService>.Instance, Mediator);
        Queries = new ConsultationQueryService(consultations, assessments, diagnoses,
            Substitute.For<IDefaultGuidelinesProvider>(), plans, Monitoring);
        // NC-10/IA-8: the review inbox, the real AI pipeline with a fake model, and the acceptance.
        var reviewItems = new ReviewItemRepository(this);
        ReviewItemStore = reviewItems;
        AiConsent.IsAllowedAsync(Arg.Any<int>(), Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(true);
        var clinicalDate = new FixedClinicalDate(today);
        var floor = new ConfiguredCalorieFloorPolicy(new ConfigurationBuilder().Build());
        var reader = new PlanAdjustmentInputReader(plans, diagnoses, baselines, assessments, Monitoring, Intake, Iam,
            floor, clinicalDate);
        var aiSettings = new ConfiguredAiSettings(AiConfiguration, NullLogger<ConfiguredAiSettings>.Instance);
        var pipeline = new AiGenerationPipeline(aiSettings, AiConsent,
            PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly), Model, AiLog, TimeProvider.System,
            NullLogger<AiGenerationPipeline>.Instance);
        ReviewItems = new ReviewItemCommandService(reviewItems, UnitOfWork, CareRelationship,
            NullLogger<ReviewItemCommandService>.Instance, Mediator, reader,
            new PlanAdjustmentProposer(pipeline, EmbeddedPatientMessageLexicon.Instance), Monitoring, clinicalDate,
            TimeProvider.System, ProposalQueue, aiSettings, AiConsent);
        PlanProposals = new PlanProposalCommandService(reviewItems, plans, diagnoses, UnitOfWork, Iam,
            CareRelationship, reader, floor, clinicalDate, NullLogger<PlanProposalCommandService>.Instance, Mediator);
        ReviewItemQueries = new ReviewItemQueryService(reviewItems, Iam, ProposalQueue, plans);

        PlanQueries = new NutritionPlanQueryService(plans);
        BaselineQueries = new PatientBaselineQueryService(baselines);
        AssessmentQueries = new NutritionalAssessmentQueryService(assessments);
        DiagnosisQueries = new NutritionalDiagnosisQueryService(diagnoses);
    }

    public List<Consultation> Consultations { get; } = [];
    public List<PatientBaseline> Baselines { get; } = [];
    public List<NutritionalAssessment> Assessments { get; } = [];
    public List<NutritionalDiagnosis> Diagnoses { get; } = [];
    public List<NutritionPlan> Plans { get; } = [];
    public List<ActiveTargetsCache> Caches { get; } = [];
    public List<ReviewItem> ReviewItemRows { get; } = [];

    /// <summary>NC-10. The inbox repository over <see cref="ReviewItemRows" />.</summary>
    public IReviewItemRepository ReviewItemStore { get; }

    /// <summary>NC-10. Open, resolve, generate the proposal, open the rechecks.</summary>
    public IReviewItemCommandService ReviewItems { get; }

    /// <summary>NC-10. The acceptance: the only path from a proposal to a version.</summary>
    public IPlanProposalCommandService PlanProposals { get; }

    public ReviewItemQueryService ReviewItemQueries { get; }
    public IPlanProposalGenerationQueue ProposalQueue { get; } = new InMemoryPlanProposalGenerationQueue();

    /// <summary>IA-8. The model, the audit, the patient's consent and the AI settings (on by default).</summary>
    public FakeLanguageModelClient Model { get; } = new();

    public InMemoryAiGenerationLog AiLog { get; } = new();
    public IAiConsentPolicy AiConsent { get; } = Substitute.For<IAiConsentPolicy>();

    public IConfigurationRoot AiConfiguration { get; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Enabled"] = "true" }).Build();

    /// <summary>IA-8. Intake, for the weight trend and the moments of the diary; empty unless a test says otherwise.</summary>
    public IIntakeContextFacade Intake { get; } = Substitute.For<IIntakeContextFacade>();

    /// <summary>Every broken invariant seen at a save, with the save that broke it.</summary>
    public List<string> Violations { get; } = [];

    public InvariantCheckingUnitOfWork UnitOfWork { get; }
    public IIamContextFacade Iam { get; } = Substitute.For<IIamContextFacade>();
    public ICareRelationshipContextFacade CareRelationship { get; } = Substitute.For<ICareRelationshipContextFacade>();
    public IMediator Mediator { get; } = Substitute.For<IMediator>();

    /// <summary>MA-4. Monitoring, for the check in of EV-2; returns null unless a test says otherwise.</summary>
    public IMonitoringContextFacade Monitoring { get; } = Substitute.For<IMonitoringContextFacade>();
    public IConsultationCommandService Consultation { get; }

    /// <summary>NC-9. The plan command service (stand-alone publication, adjustment) over the same store.</summary>
    public INutritionPlanCommandService PlanCommands { get; }
    public ConsultationQueryService Queries { get; }
    public NutritionPlanQueryService PlanQueries { get; }
    public PatientBaselineQueryService BaselineQueries { get; }

    /// <summary>RM-4. The assessments, for the facade's evaluations.</summary>
    public NutritionalAssessmentQueryService AssessmentQueries { get; }

    /// <summary>RM-4. The active diagnosis, for the facade.</summary>
    public NutritionalDiagnosisQueryService DiagnosisQueries { get; }

    /// <summary>RM-4. The Nutritional Care facade over these services, as other contexts read it.</summary>
    public NutritionalCareContextFacade Facade(DateOnly today)
    {
        return new NutritionalCareContextFacade(PlanQueries, Substitute.For<IReviewItemQueryService>(),
            BaselineQueries, Queries, new FixedClinicalDate(today), AssessmentQueries, DiagnosisQueries);
    }

    public void Seed(PatientBaseline baseline)
    {
        Baselines.Add(Identity.Assign(baseline, new PatientBaselineId(Baselines.Count + 1)));
    }

    private void CheckInvariants(int save)
    {
        foreach (var patient in Diagnoses.Select(d => d.PatientId).Concat(Plans.Select(p => p.PatientId))
                     .Concat(Consultations.Select(c => c.PatientId)).Distinct())
        {
            if (Diagnoses.Count(d => d.PatientId == patient && d.IsActive) > 1)
                Violations.Add($"save {save}: two active diagnoses for patient {patient}");
            if (Plans.Count(p => p.PatientId == patient && p.IsActive) > 1)
                Violations.Add($"save {save}: two active versions for patient {patient}");
            if (Consultations.Count(c => c.PatientId == patient && c.IsInProgress) > 1)
                Violations.Add($"save {save}: two consultations in progress for patient {patient}");
        }

        foreach (var consultation in Consultations)
            if (Diagnoses.Count(d => d.IsPendingFor(consultation.Id.Value)) > 1)
                Violations.Add($"save {save}: two pending diagnoses for consultation {consultation.Id.Value}");
    }

    /// <summary>Same contract as <see cref="TransactionalUnitOfWork" />, checking the invariants at every save.</summary>
    public sealed class InvariantCheckingUnitOfWork(InMemoryNutritionalCare store) : IUnitOfWork
    {
        private bool _inTransaction;

        public int Saves { get; private set; }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            store.CheckInvariants(Saves);
            return Task.CompletedTask;
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            if (_inTransaction) return await work(cancellationToken);
            _inTransaction = true;
            try
            {
                return await work(cancellationToken);
            }
            finally
            {
                _inTransaction = false;
            }
        }
    }

    private abstract class Repository<T>(List<T> rows) : IBaseRepository<T> where T : class
    {
        protected List<T> Rows { get; } = rows;

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            AssignIdentity(entity, Rows.Count + 1);
            Rows.Add(entity);
            return Task.CompletedTask;
        }

        public abstract Task<T?> FindByIdAsync(int id, CancellationToken cancellationToken = default);

        public void Update(T entity)
        {
        }

        public void Remove(T entity)
        {
            throw new InvalidOperationException("Nothing is deleted in Nutritional Care.");
        }

        public Task<IEnumerable<T>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<T>>(Rows.ToList());
        }

        protected abstract void AssignIdentity(T entity, int id);
    }

    private sealed class ConsultationRepository(InMemoryNutritionalCare store)
        : Repository<Consultation>(store.Consultations), IConsultationRepository
    {
        public override Task<Consultation?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(c => c.Id.Value == id));
        }

        public Task<Consultation?> FindInProgressByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.LastOrDefault(c => c.PatientId == patientId && c.IsInProgress));
        }

        public Task<IReadOnlyList<Consultation>> ListInProgressByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Consultation>>(Rows
                .Where(c => patientIds.Contains(c.PatientId) && c.IsInProgress).ToList());
        }

        public Task<bool> ExistsCompletedForPatientAsync(int patientId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Any(c =>
                c.PatientId == patientId && c.State.Value == ConsultationState.Completed));
        }

        public Task<IEnumerable<Consultation>> ListByPatientIdAsync(int patientId, ConsultationState? state,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<Consultation>>(Rows
                .Where(c => c.PatientId == patientId && (state is null || c.State == state))
                .OrderByDescending(c => c.StartedAt).ThenByDescending(c => c.Id.Value).ToList());
        }

        protected override void AssignIdentity(Consultation entity, int id)
        {
            Identity.Assign(entity, new ConsultationId(id));
        }
    }

    private sealed class BaselineRepository(InMemoryNutritionalCare store)
        : Repository<PatientBaseline>(store.Baselines), IPatientBaselineRepository
    {
        public override Task<PatientBaseline?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(b => b.Id.Value == id));
        }

        public Task<PatientBaseline?> FindByPatientIdAsync(int patientId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(b => b.PatientId == patientId));
        }

        public Task<IReadOnlyList<PatientBaseline>> ListByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PatientBaseline>>(Rows
                .Where(b => patientIds.Contains(b.PatientId)).ToList());
        }

        protected override void AssignIdentity(PatientBaseline entity, int id)
        {
            Identity.Assign(entity, new PatientBaselineId(id));
        }
    }

    private sealed class AssessmentRepository(InMemoryNutritionalCare store)
        : Repository<NutritionalAssessment>(store.Assessments), INutritionalAssessmentRepository
    {
        public override Task<NutritionalAssessment?> FindByIdAsync(int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(a => a.Id.Value == id));
        }

        public Task<IEnumerable<NutritionalAssessment>> ListByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<NutritionalAssessment>>(Rows.Where(a => a.PatientId == patientId)
                .OrderByDescending(a => a.Id.Value).ToList());
        }

        public Task<bool> ExistsForPatientAsync(int patientId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Any(a => a.PatientId == patientId));
        }

        protected override void AssignIdentity(NutritionalAssessment entity, int id)
        {
            Identity.Assign(entity, new AssessmentId(id));
        }
    }

    private sealed class DiagnosisRepository(InMemoryNutritionalCare store)
        : Repository<NutritionalDiagnosis>(store.Diagnoses), INutritionalDiagnosisRepository
    {
        public override Task<NutritionalDiagnosis?> FindByIdAsync(int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(d => d.Id.Value == id));
        }

        public Task<NutritionalDiagnosis?> FindActiveByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Where(d => d.PatientId == patientId && d.IsActive)
                .OrderByDescending(d => d.IssuedAt).FirstOrDefault());
        }

        protected override void AssignIdentity(NutritionalDiagnosis entity, int id)
        {
            Identity.Assign(entity, new DiagnosisId(id));
        }
    }

    private sealed class PlanRepository(InMemoryNutritionalCare store)
        : Repository<NutritionPlan>(store.Plans), INutritionPlanRepository
    {
        public override Task<NutritionPlan?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(p => p.Id.Value == id));
        }

        public Task<NutritionPlan?> FindActiveByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Where(p => p.PatientId == patientId && p.IsActive && !p.IsSuperseded)
                .OrderByDescending(p => p.Version).FirstOrDefault());
        }

        public Task<IReadOnlyList<NutritionPlan>> ListActiveByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<NutritionPlan>>(Rows
                .Where(p => patientIds.Contains(p.PatientId) && p.IsActive && !p.IsSuperseded).ToList());
        }

        public Task<IEnumerable<NutritionPlan>> ListByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<NutritionPlan>>(Rows
                .Where(p => p.PatientId == patientId && !p.IsDiscarded).OrderByDescending(p => p.Version).ToList());
        }

        public Task<int> GetLatestVersionAsync(int patientId, CancellationToken cancellationToken = default)
        {
            var versions = Rows.Where(p => p.PatientId == patientId && !p.IsDiscarded).Select(p => p.Version).ToList();
            return Task.FromResult(versions.Count == 0 ? 0 : versions.Max());
        }

        protected override void AssignIdentity(NutritionPlan entity, int id)
        {
            Identity.Assign(entity, new PlanId(id));
        }
    }

    private sealed class ReviewItemRepository(InMemoryNutritionalCare store)
        : Repository<ReviewItem>(store.ReviewItemRows), IReviewItemRepository
    {
        public override Task<ReviewItem?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(r => r.Id.Value == id));
        }

        public Task<bool> ExistsOpenForPatientAndSignalTypeAsync(int patientId, SignalType signalType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Any(r => r.PatientId == patientId && r.SignalType == signalType && r.IsOpen));
        }

        public Task<IEnumerable<ReviewItem>> ListOpenByPractitionerIdAsync(int practitionerId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ReviewItem>>(Rows
                .Where(r => r.PractitionerId == practitionerId && r.IsOpen).OrderByDescending(r => r.Id.Value)
                .ToList());
        }

        public Task<IEnumerable<ReviewItem>> ListByPractitionerIdAndStateAsync(int practitionerId,
            ReviewItemState state, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ReviewItem>>(Rows
                .Where(r => r.PractitionerId == practitionerId && r.State == state)
                .OrderByDescending(r => r.Id.Value).ToList());
        }

        public Task<bool> ExistsOpenWithoutProposalAsync(int reviewItemId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Any(r => r.Id.Value == reviewItemId && r.IsOpen && r.Proposal is null));
        }

        public Task<IReadOnlyList<ReviewItem>> ListOpenWithoutProposalAsync(SignalType signalType,
            DateTimeOffset createdSince, int batchSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ReviewItem>>(Rows
                .Where(r => r.SignalType == signalType && r.IsOpen && r.Proposal is null && r.CreatedAt != null &&
                            r.CreatedAt >= createdSince)
                .OrderBy(r => r.Id.Value).Take(batchSize).ToList());
        }

        public Task<IReadOnlyList<ReviewItem>> ListRecheckDueAsync(DateTimeOffset now, int batchSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ReviewItem>>(Rows
                .Where(r => r.IsRecheckPending && r.RecheckDueAt <= now).OrderBy(r => r.RecheckDueAt)
                .Take(batchSize).ToList());
        }

        public Task<int> CountOpenByPractitionerIdAsync(int practitionerId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Count(r => r.PractitionerId == practitionerId && r.IsOpen));
        }

        public Task<IReadOnlyList<ReviewItem>> ListWithUnacceptedProposalByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ReviewItem>>(Rows
                .Where(r => r.PatientId == patientId && r.Proposal is { IsAccepted: false })
                .OrderBy(r => r.Id.Value).ToList());
        }

        public Task<bool> ExistsForPatientAndSignalTypeAsync(int patientId, SignalType signalType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.Any(r => r.PatientId == patientId && r.SignalType == signalType));
        }

        protected override void AssignIdentity(ReviewItem entity, int id)
        {
            Identity.Assign(entity, new ReviewItemId(id));
        }
    }

    private sealed class CacheRepository(InMemoryNutritionalCare store)
        : Repository<ActiveTargetsCache>(store.Caches), IActiveTargetsCacheRepository
    {
        public override Task<ActiveTargetsCache?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return FindByPatientIdAsync(id, cancellationToken);
        }

        public Task<ActiveTargetsCache?> FindByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Rows.FirstOrDefault(c => c.PatientId == patientId));
        }

        // The patient is the key of the cache.
        protected override void AssignIdentity(ActiveTargetsCache entity, int id)
        {
        }
    }
}
