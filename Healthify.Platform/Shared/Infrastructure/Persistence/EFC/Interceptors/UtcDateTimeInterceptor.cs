using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Interceptors;

/// <summary>
///     Normalises date and time values to UTC before they reach the database, so that every
///     server-side timestamp is comparable regardless of the machine that produced it.
/// </summary>
/// <remarks>
///     Deliberate exception: the properties named in <see cref="ClientDeclaredTimestampProperties"/>
///     are never touched. They carry the moment the patient declared on their own device, and
///     rewriting them would break the business rule
///     "Declared Local Timestamp Never Rewritten" (Intake and Body Response, Subflow 4.6).
///     The exclusion is expressed by property name rather than by type because Shared must not
///     depend on any bounded context.
/// </remarks>
public sealed class UtcDateTimeInterceptor : SaveChangesInterceptor
{
    /// <summary>Property names whose value is declared by the client and is never rewritten by the server.</summary>
    private static readonly HashSet<string> ClientDeclaredTimestampProperties =
        new(StringComparer.Ordinal) { "LocalTimestamp" };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ConvertToUtc(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ConvertToUtc(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ConvertToUtc(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        foreach (var property in entry.Properties)
        {
            if (ClientDeclaredTimestampProperties.Contains(property.Metadata.Name)) continue;

            if (property.CurrentValue is DateTimeOffset dto && dto.Offset != TimeSpan.Zero)
                property.CurrentValue = dto.ToUniversalTime();
            else if (property.CurrentValue is DateTime dt && dt.Kind != DateTimeKind.Utc)
                property.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
    }
}
