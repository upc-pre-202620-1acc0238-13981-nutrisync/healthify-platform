using Healthify.Platform.Shared.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     An <see cref="IUnitOfWork" /> that models what the database keeps: a save outside a transaction is
///     durable at once; saves inside <see cref="ExecuteInTransactionAsync{T}" /> become durable only when
///     the work finishes, and are discarded if it throws. The real rollback is EF Core's; this fake lets
///     command services be tested against the same contract without a database.
/// </summary>
public sealed class TransactionalUnitOfWork : IUnitOfWork
{
    private int _pending;
    private bool _inTransaction;
    private int _saveAttempts;

    /// <summary>Saves the database kept.</summary>
    public int DurableSaves { get; private set; }

    /// <summary>Saves made inside a transaction that was then rolled back.</summary>
    public int DiscardedSaves { get; private set; }

    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }

    /// <summary>1-based number of the save that fails with a <see cref="DbUpdateException" />, if any.</summary>
    public int? FailOnSave { get; set; }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        _saveAttempts++;
        if (_saveAttempts == FailOnSave)
            throw new DbUpdateException($"Simulated failure of save {_saveAttempts}.");

        if (_inTransaction) _pending++;
        else DurableSaves++;
        return Task.CompletedTask;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        if (_inTransaction) return await work(cancellationToken);

        _inTransaction = true;
        try
        {
            var result = await work(cancellationToken);
            DurableSaves += _pending;
            Commits++;
            return result;
        }
        catch
        {
            DiscardedSaves += _pending;
            Rollbacks++;
            throw;
        }
        finally
        {
            _pending = 0;
            _inTransaction = false;
        }
    }
}
