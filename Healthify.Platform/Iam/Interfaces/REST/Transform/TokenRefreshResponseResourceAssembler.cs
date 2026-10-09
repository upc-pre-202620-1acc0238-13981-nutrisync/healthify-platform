using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class TokenRefreshResponseResourceAssembler
{
    public static TokenRefreshResponseResource ToResource(SignInOutcome outcome)
    {
        return new TokenRefreshResponseResource(outcome.Token, outcome.RefreshToken!, outcome.ExpiresAt!.Value);
    }
}
