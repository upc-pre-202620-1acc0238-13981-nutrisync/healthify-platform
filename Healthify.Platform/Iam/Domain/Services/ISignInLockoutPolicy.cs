namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>
///     IAM-5. How long a lockout lasts: <c>Iam:LockoutMinutes</c> (15 by default). Business rule: Lockout Is
///     Temporary (IAM-5).
/// </summary>
public interface ISignInLockoutPolicy
{
    /// <summary>The time a locked account waits before its credentials are checked again.</summary>
    TimeSpan LockoutDuration();
}
