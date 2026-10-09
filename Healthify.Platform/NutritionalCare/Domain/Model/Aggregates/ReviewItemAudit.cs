using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

public partial class ReviewItem : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
