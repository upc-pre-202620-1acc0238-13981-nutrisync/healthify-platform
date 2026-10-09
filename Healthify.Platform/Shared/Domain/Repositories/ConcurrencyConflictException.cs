namespace Healthify.Platform.Shared.Domain.Repositories;

/// <summary>
///     A save found that the row it was updating had been changed by somebody else since it was read: one of
///     its concurrency tokens no longer matched. Raised by <see cref="IUnitOfWork.CompleteAsync" /> so that the
///     application layer can recover without depending on EF Core.
/// </summary>
public sealed class ConcurrencyConflictException(Exception innerException)
    : Exception("The row was changed by another request since it was read.", innerException);
