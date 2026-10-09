using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

public partial class MealPhotoAnalysis : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
