using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.CareRelationship.Domain.Model.Aggregates;

public partial class Invitation : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
