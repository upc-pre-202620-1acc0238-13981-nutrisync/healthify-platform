using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

public partial class WeeklySummary : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
