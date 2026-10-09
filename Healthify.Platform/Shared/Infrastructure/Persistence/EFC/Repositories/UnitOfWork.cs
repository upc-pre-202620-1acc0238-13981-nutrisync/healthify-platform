using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;

public class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        // Joins the ambient transaction: the caller that opened it decides commit or rollback.
        if (context.Database.CurrentTransaction is not null) return await work(cancellationToken);

        // A user-initiated transaction must run through the configured execution strategy as one
        // retriable unit; with the default (non-retrying) strategy this simply runs it once.
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await work(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                // CancellationToken.None: the rollback must happen even if the request was aborted.
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }, cancellationToken);
    }
}
