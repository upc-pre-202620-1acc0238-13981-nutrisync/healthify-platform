using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Repositories;

public class AiPreferencesRepository(AppDbContext context)
    : BaseRepository<AiPreferences>(context), IAiPreferencesRepository
{
    public async Task<AiPreferences?> FindByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId <= 0) return null;
        return await Context.Set<AiPreferences>().FirstOrDefaultAsync(p => p.PatientId == patientId,
            cancellationToken);
    }
}
