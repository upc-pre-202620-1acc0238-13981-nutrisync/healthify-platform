namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>DiaryEntry</c>.</summary>
public sealed record DiaryEntryId
{
    public DiaryEntryId(int value)
    {
        if (value <= 0) throw new ArgumentException("DiaryEntryId must be a positive integer.", nameof(value));
        Value = value;
    }

    private DiaryEntryId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static DiaryEntryId FromRaw(int value)
    {
        return new DiaryEntryId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(DiaryEntryId id)
    {
        return id.Value;
    }

    public static explicit operator DiaryEntryId(int value)
    {
        return new DiaryEntryId(value);
    }
}

/// <summary>Typed identity of a <c>SelfWeighIn</c>.</summary>
public sealed record SelfWeighInId
{
    public SelfWeighInId(int value)
    {
        if (value <= 0)
            throw new ArgumentException("SelfWeighInId must be a positive integer.", nameof(value));
        Value = value;
    }

    private SelfWeighInId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static SelfWeighInId FromRaw(int value)
    {
        return new SelfWeighInId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(SelfWeighInId id)
    {
        return id.Value;
    }

    public static explicit operator SelfWeighInId(int value)
    {
        return new SelfWeighInId(value);
    }
}
