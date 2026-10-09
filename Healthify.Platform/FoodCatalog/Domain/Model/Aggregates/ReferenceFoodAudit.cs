using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;

public partial class ReferenceFood : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
