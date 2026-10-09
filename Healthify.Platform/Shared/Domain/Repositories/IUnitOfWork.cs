namespace Healthify.Platform.Shared.Domain.Repositories;

/// <summary>Commits the changes tracked for the current request.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">A concurrency token of an updated row no longer matched.</exception>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs <paramref name="work" /> inside one database transaction: every <see cref="CompleteAsync" />
    ///     it makes is committed together or rolled back together. Used when one command needs several
    ///     saves, for instance because a later save needs an identifier the database assigns earlier.
    /// </summary>
    /// <remarks>
    ///     If the call already runs inside a transaction, the work joins it and the outer caller commits.
    ///     Under a retrying execution strategy the whole work may run again, so it must be safe to repeat.
    ///     Domain events are published by the caller after this method returns, never inside the work.
    /// </remarks>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
