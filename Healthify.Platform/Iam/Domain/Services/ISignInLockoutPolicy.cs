namespace Healthify.Platform.Iam.Domain.Services;

/// <summary>
///     How long a lockout lasts: <c>Iam:LockoutMinutes</c> (15 by default). Business rule: Lockout Is
///     Temporary.
/// </summary>
public interface ISignInLockoutPolicy
{
    /// <summary>The time a locked account waits before its credentials are checked again.</summary>
    TimeSpan LockoutDuration();
}
