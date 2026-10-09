namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>Typed identity of an <c>EvaluationWindow</c>.</summary>
public sealed record WindowId
{
    public WindowId(int value)
    {
        if (value <= 0) throw new ArgumentException("WindowId must be a positive integer.", nameof(value));
        Value = value;
    }

    private WindowId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static WindowId FromRaw(int value)
    {
        return new WindowId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(WindowId id)
    {
        return id.Value;
    }

    public static explicit operator WindowId(int value)
    {
        return new WindowId(value);
    }
}

/// <summary>Typed identity of a <c>Deviation</c>.</summary>
public sealed record DeviationId
{
    public DeviationId(int value)
    {
        if (value <= 0)
            throw new ArgumentException("DeviationId must be a positive integer.", nameof(value));
        Value = value;
    }

    private DeviationId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static DeviationId FromRaw(int value)
    {
        return new DeviationId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(DeviationId id)
    {
        return id.Value;
    }

    public static explicit operator DeviationId(int value)
    {
        return new DeviationId(value);
    }
}

/// <summary>Typed identity of a <c>Referral</c>.</summary>
public sealed record ReferralId
{
    public ReferralId(int value)
    {
        if (value <= 0)
            throw new ArgumentException("ReferralId must be a positive integer.", nameof(value));
        Value = value;
    }

    private ReferralId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static ReferralId FromRaw(int value)
    {
        return new ReferralId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(ReferralId id)
    {
        return id.Value;
    }

    public static explicit operator ReferralId(int value)
    {
        return new ReferralId(value);
    }
}

/// <summary>Typed identity of a <c>ScheduledFollowUp</c>.</summary>
public sealed record FollowUpId
{
    public FollowUpId(int value)
    {
        if (value <= 0)
            throw new ArgumentException("FollowUpId must be a positive integer.", nameof(value));
        Value = value;
    }

    private FollowUpId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static FollowUpId FromRaw(int value)
    {
        return new FollowUpId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(FollowUpId id)
    {
        return id.Value;
    }

    public static explicit operator FollowUpId(int value)
    {
        return new FollowUpId(value);
    }
}

/// <summary>Typed identity of a <c>PreVisitCheckIn</c> (MA-4).</summary>
public sealed record PreVisitCheckInId
{
    public PreVisitCheckInId(int value)
    {
        if (value <= 0)
            throw new ArgumentException("PreVisitCheckInId must be a positive integer.", nameof(value));
        Value = value;
    }

    private PreVisitCheckInId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static PreVisitCheckInId FromRaw(int value)
    {
        return new PreVisitCheckInId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(PreVisitCheckInId id)
    {
        return id.Value;
    }

    public static explicit operator PreVisitCheckInId(int value)
    {
        return new PreVisitCheckInId(value);
    }
}

/// <summary>Typed identity of a <c>WeeklySummary</c> (IA-2).</summary>
public sealed record WeeklySummaryId
{
    public WeeklySummaryId(int value)
    {
        if (value <= 0)
            throw new ArgumentException("WeeklySummaryId must be a positive integer.", nameof(value));
        Value = value;
    }

    private WeeklySummaryId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static WeeklySummaryId FromRaw(int value)
    {
        return new WeeklySummaryId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(WeeklySummaryId id)
    {
        return id.Value;
    }

    public static explicit operator WeeklySummaryId(int value)
    {
        return new WeeklySummaryId(value);
    }
}
