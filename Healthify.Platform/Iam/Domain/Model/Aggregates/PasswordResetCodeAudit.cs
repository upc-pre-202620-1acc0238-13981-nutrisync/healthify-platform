using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.Iam.Domain.Model.Aggregates;

public partial class PasswordResetCode : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
