namespace Healthify.Platform.Shared.Domain.Model.Entities;

/// <summary>
///     Marks an entity as carrying audit timestamps managed by the persistence layer.
///     This is the single definition in the platform: the interceptor and the aggregates use it.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset? CreatedAt { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
}
