using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class SignInResponseResourceAssembler
{
    public static SignInResponseResource ToResource(SignInOutcome result)
    {
        return new SignInResponseResource(
            result.User.Id.Value,
            result.User.Email.Value,
            result.Session.RoleClaim.Value,
            result.Session.Id.Value,
            result.Token,
            result.Session.StartedAt,
            result.User.GivenNames,
            result.User.FamilyNames,
            result.User.PreferredLanguage.Value,
            result.RefreshToken,
            result.ExpiresAt);
    }
}
