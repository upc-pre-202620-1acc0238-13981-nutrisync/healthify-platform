using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Persistence;

/// <summary>
///     IA-0. <see cref="IAiGenerationLog" /> over <c>ai_generations</c>. Every call opens a scope, and so a
///     DbContext, of its own: auditing a generation in the middle of a command must not commit the command's
///     pending changes, nor be rolled back with them.
/// </summary>
public class EfAiGenerationLog(IServiceScopeFactory scopeFactory) : IAiGenerationLog
{
    private static readonly string[] CountedStatuses =
    [
        nameof(AiGenerationStatus.Succeeded), nameof(AiGenerationStatus.Rejected), nameof(AiGenerationStatus.Failed)
    ];

    public async Task<long> RecordAsync(AiGenerationRecord record, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = new AiGeneration(record);
        context.Set<AiGeneration>().Add(row);
        await context.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<int> CountSinceAsync(AiFeature feature, int subjectPatientId, int? requestedByUserId,
        DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var rows = scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AiGeneration>()
            .Where(g => g.Feature == feature.Name && g.CreatedAt >= since && CountedStatuses.Contains(g.Status));
        rows = requestedByUserId is { } requester
            ? rows.Where(g => g.RequestedByUserId == requester)
            : rows.Where(g => g.SubjectPatientId == subjectPatientId);
        return await rows.CountAsync(cancellationToken);
    }

    public async Task<int> PurgeForPatientAsync(int subjectPatientId, AiFeature? feature = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var rows = scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AiGeneration>()
            .Where(g => g.SubjectPatientId == subjectPatientId);
        if (feature is not null) rows = rows.Where(g => g.Feature == feature.Name);
        return await rows.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AiGeneration>()
            .Where(g => g.ExpiresAt != null && g.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
