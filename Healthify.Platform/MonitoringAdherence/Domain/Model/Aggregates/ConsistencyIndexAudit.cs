using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

public partial class ConsistencyIndex : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
