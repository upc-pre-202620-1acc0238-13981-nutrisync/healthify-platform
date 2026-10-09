using Healthify.Platform.Iam.Domain.Services;

namespace Healthify.Platform.Iam.Infrastructure.Security;

/// <summary>IAM-5. Reads <c>Iam:LockoutMinutes</c>; 15 minutes by default, never less than one.</summary>
public class ConfiguredSignInLockoutPolicy(IConfiguration configuration) : ISignInLockoutPolicy
{
    private const int DefaultLockoutMinutes = 15;

    public TimeSpan LockoutDuration()
    {
        var minutes = configuration.GetValue<int?>("Iam:LockoutMinutes") ?? DefaultLockoutMinutes;
        return TimeSpan.FromMinutes(Math.Max(1, minutes));
    }
}
