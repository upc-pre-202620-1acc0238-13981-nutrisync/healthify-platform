using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.Iam.Domain.Model.Aggregates;

public partial class UserSession : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
