using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Repositories;

public class NutritionalAssessmentRepository(AppDbContext context)
    : BaseRepository<NutritionalAssessment>(context), INutritionalAssessmentRepository
{
    /// <summary>Loads the anthropometry with the assessment: the calculation reads it.</summary>
    private IQueryable<NutritionalAssessment> WithRelations()
    {
        return Context.Set<NutritionalAssessment>().Include(a => a.Measurements);
    }

    public new async Task<NutritionalAssessment?> FindByIdAsync(int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var assessmentId = new AssessmentId(id);
        return await WithRelations().FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken);
    }

    public async Task<IEnumerable<NutritionalAssessment>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await WithRelations()
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsForPatientAsync(int patientId, CancellationToken cancellationToken = default)
    {
        return await Context.Set<NutritionalAssessment>().AnyAsync(a => a.PatientId == patientId, cancellationToken);
    }

    // Explicit re-implementation so that interface-typed calls load the measurements too.
    Task<NutritionalAssessment?> IBaseRepository<NutritionalAssessment>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class ConsultationRepository(AppDbContext context)
    : BaseRepository<Consultation>(context), IConsultationRepository
{
    /// <summary>Compared through the value converter; EF cannot translate a member of a converted type.</summary>
    private static readonly ConsultationState InProgressState = new(ConsultationState.InProgress);

    private static readonly ConsultationState CompletedState = new(ConsultationState.Completed);

    public new async Task<Consultation?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var consultationId = new ConsultationId(id);
        return await Context.Set<Consultation>()
            .FirstOrDefaultAsync(c => c.Id == consultationId, cancellationToken);
    }

    public async Task<Consultation?> FindInProgressByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<Consultation>()
            .Where(c => c.PatientId == patientId && c.State == InProgressState)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Consultation>> ListInProgressByPatientIdsAsync(
        IReadOnlyCollection<int> patientIds, CancellationToken cancellationToken = default)
    {
        var ids = patientIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await Context.Set<Consultation>()
            .Where(c => ids.Contains(c.PatientId) && c.State == InProgressState)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsCompletedForPatientAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<Consultation>()
            .AnyAsync(c => c.PatientId == patientId && c.State == CompletedState, cancellationToken);
    }

    public async Task<IEnumerable<Consultation>> ListByPatientIdAsync(int patientId, ConsultationState? state,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<Consultation>().Where(c => c.PatientId == patientId);
        if (state is not null) query = query.Where(c => c.State == state);
        return await query.OrderByDescending(c => c.StartedAt).ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    Task<Consultation?> IBaseRepository<Consultation>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class PatientBaselineRepository(AppDbContext context)
    : BaseRepository<PatientBaseline>(context), IPatientBaselineRepository
{
    public new async Task<PatientBaseline?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var baselineId = new PatientBaselineId(id);
        return await Context.Set<PatientBaseline>()
            .FirstOrDefaultAsync(b => b.Id == baselineId, cancellationToken);
    }

    public async Task<PatientBaseline?> FindByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<PatientBaseline>()
            .FirstOrDefaultAsync(b => b.PatientId == patientId, cancellationToken);
    }

    public async Task<IReadOnlyList<PatientBaseline>> ListByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
        CancellationToken cancellationToken = default)
    {
        var ids = patientIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await Context.Set<PatientBaseline>().Where(b => ids.Contains(b.PatientId))
            .ToListAsync(cancellationToken);
    }

    Task<PatientBaseline?> IBaseRepository<PatientBaseline>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class NutritionalDiagnosisRepository(AppDbContext context)
    : BaseRepository<NutritionalDiagnosis>(context), INutritionalDiagnosisRepository
{
    public new async Task<NutritionalDiagnosis?> FindByIdAsync(int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var diagnosisId = new DiagnosisId(id);
        return await Context.Set<NutritionalDiagnosis>()
            .FirstOrDefaultAsync(d => d.Id == diagnosisId, cancellationToken);
    }

    public async Task<NutritionalDiagnosis?> FindActiveByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<NutritionalDiagnosis>()
            // NC-7: the pending diagnosis of a consultation and a discarded one are not active; neither leaves
            // the consultation through this read.
            .Where(d => d.PatientId == patientId && d.SupersededAt == null && d.PendingConsultationId == null &&
                        d.DiscardedAt == null)
            .OrderByDescending(d => d.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    Task<NutritionalDiagnosis?> IBaseRepository<NutritionalDiagnosis>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class NutritionPlanRepository(AppDbContext context)
    : BaseRepository<NutritionPlan>(context), INutritionPlanRepository
{
    public new async Task<NutritionPlan?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var planId = new PlanId(id);
        return await Context.Set<NutritionPlan>().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
    }

    public async Task<NutritionPlan?> FindActiveByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<NutritionPlan>()
            .Where(p => p.PatientId == patientId && p.IsActive && p.SupersededAt == null)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NutritionPlan>> ListActiveByPatientIdsAsync(IReadOnlyCollection<int> patientIds,
        CancellationToken cancellationToken = default)
    {
        var ids = patientIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await Context.Set<NutritionPlan>()
            .Where(p => ids.Contains(p.PatientId) && p.IsActive && p.SupersededAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<NutritionPlan>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        // Superseded versions included: Plan Version History is only useful because nothing is
        // ever deleted. NC-2: a discarded draft never was a version, so it is not history.
        return await Context.Set<NutritionPlan>()
            .Where(p => p.PatientId == patientId && p.DiscardedAt == null)
            .OrderByDescending(p => p.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetLatestVersionAsync(int patientId, CancellationToken cancellationToken = default)
    {
        // NC-2: a discarded draft gives its number back, so the versions the patient receives have no gaps.
        var versions = await Context.Set<NutritionPlan>()
            .Where(p => p.PatientId == patientId && p.DiscardedAt == null)
            .Select(p => p.Version)
            .ToListAsync(cancellationToken);

        return versions.Count == 0 ? 0 : versions.Max();
    }

    Task<NutritionPlan?> IBaseRepository<NutritionPlan>.FindByIdAsync(int id,
        CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}

public class ReviewItemRepository(AppDbContext context)
    : BaseRepository<ReviewItem>(context), IReviewItemRepository
{
    /// <summary>Compared through the value converter; EF cannot translate a member of a converted type.</summary>
    private static readonly ReviewItemState OpenState = new(ReviewItemState.Open);

    public new async Task<ReviewItem?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var reviewItemId = new ReviewItemId(id);
        return await Context.Set<ReviewItem>()
            .Include(r => r.Proposal)
            .FirstOrDefaultAsync(r => r.Id == reviewItemId, cancellationToken);
    }

    public async Task<bool> ExistsOpenForPatientAndSignalTypeAsync(int patientId, SignalType signalType,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReviewItem>()
            .AnyAsync(r => r.PatientId == patientId && r.SignalType == signalType
                                                    && r.State == OpenState, cancellationToken);
    }

    public async Task<bool> ExistsForPatientAndSignalTypeAsync(int patientId, SignalType signalType,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReviewItem>()
            .AnyAsync(r => r.PatientId == patientId && r.SignalType == signalType, cancellationToken);
    }

    public async Task<IEnumerable<ReviewItem>> ListOpenByPractitionerIdAsync(int practitionerId,
        CancellationToken cancellationToken = default)
    {
        return await OpenFor(practitionerId).Include(r => r.Proposal).OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ReviewItem>> ListByPractitionerIdAndStateAsync(int practitionerId,
        ReviewItemState state, CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReviewItem>()
            .Include(r => r.Proposal)
            .Where(r => r.PractitionerId == practitionerId && r.State == state)
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsOpenWithoutProposalAsync(int reviewItemId,
        CancellationToken cancellationToken = default)
    {
        if (reviewItemId <= 0) return false;
        var id = new ReviewItemId(reviewItemId);
        return await Context.Set<ReviewItem>().AsNoTracking()
            .AnyAsync(r => r.Id == id && r.State == OpenState && r.Proposal == null, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewItem>> ListOpenWithoutProposalAsync(SignalType signalType,
        DateTimeOffset createdSince, int batchSize, CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReviewItem>().AsNoTracking()
            .Where(r => r.SignalType == signalType && r.State == OpenState && r.Proposal == null
                        && r.CreatedAt != null && r.CreatedAt >= createdSince)
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewItem>> ListRecheckDueAsync(DateTimeOffset now, int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReviewItem>()
            .Include(r => r.Proposal)
            .Where(r => r.RecheckDueAt != null && r.RecheckDueAt <= now && r.RecheckIssuedAt == null)
            .OrderBy(r => r.RecheckDueAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountOpenByPractitionerIdAsync(int practitionerId,
        CancellationToken cancellationToken = default)
    {
        return await OpenFor(practitionerId).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewItem>> ListWithUnacceptedProposalByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Set<ReviewItem>()
            .Include(r => r.Proposal)
            .Where(r => r.PatientId == patientId && r.Proposal != null && r.Proposal.AssignedPlanVersion == null)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<ReviewItem> OpenFor(int practitionerId)
    {
        return Context.Set<ReviewItem>()
            .Where(r => r.PractitionerId == practitionerId && r.State == OpenState);
    }

    Task<ReviewItem?> IBaseRepository<ReviewItem>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}
