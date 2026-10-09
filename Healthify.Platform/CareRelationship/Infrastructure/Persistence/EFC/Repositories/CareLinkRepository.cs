using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Repositories;

public class CareLinkRepository(AppDbContext context) : BaseRepository<CareLink>(context), ICareLinkRepository
{
    public new async Task<CareLink?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var careLinkId = new CareLinkId(id);
        return await Context.Set<CareLink>().FirstOrDefaultAsync(c => c.Id == careLinkId, cancellationToken);
    }

    public async Task<CareLink?> FindActiveByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Unclosed()
            .Where(c => c.PatientId == patientId && c.ConsentGranted)
            .OrderByDescending(c => c.EstablishedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CareLink?> FindUnclosedByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Unclosed()
            .Where(c => c.PatientId == patientId)
            .OrderByDescending(c => c.EstablishedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsUnclosedByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return await Unclosed().AnyAsync(c => c.PatientId == patientId, cancellationToken);
    }

    public async Task<IEnumerable<CareLink>> ListByPractitionerIdAsync(int practitionerId,
        CancellationToken cancellationToken = default)
    {
        // The whole roster, including revoked and discharged links: the read model is a history.
        return await Context.Set<CareLink>()
            .Where(c => c.PractitionerId == practitionerId)
            .OrderByDescending(c => c.EstablishedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Links that still occupy the one slot a patient has, consent granted or not yet.</summary>
    private IQueryable<CareLink> Unclosed()
    {
        return Context.Set<CareLink>().Where(c => c.RevokedAt == null && c.DischargedAt == null);
    }

    Task<CareLink?> IBaseRepository<CareLink>.FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        return FindByIdAsync(id, cancellationToken);
    }
}
