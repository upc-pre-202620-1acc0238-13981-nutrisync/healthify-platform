using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class UserResourceAssembler
{
    public static UserResource ToResource(User user)
    {
        return new UserResource(user.Id.Value, user.Email.Value, user.Role.Value, user.CreatedAt, user.GivenNames,
            user.FamilyNames, user.FullName, user.PreferredLanguage.Value);
    }
}
