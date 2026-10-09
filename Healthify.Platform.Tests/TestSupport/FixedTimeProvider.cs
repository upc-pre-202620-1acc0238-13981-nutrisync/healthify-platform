using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>A clock stopped at a given instant.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now.ToUniversalTime();
    }
}

/// <summary>A practice calendar stopped at a given date.</summary>
public sealed class FixedClinicalDate(DateOnly today) : IClinicalDateProvider
{
    public DateOnly Today()
    {
        return today;
    }
}

/// <summary>A clock a test can move forward, to cross the hour of a visit.</summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now.ToUniversalTime();
    }
}
