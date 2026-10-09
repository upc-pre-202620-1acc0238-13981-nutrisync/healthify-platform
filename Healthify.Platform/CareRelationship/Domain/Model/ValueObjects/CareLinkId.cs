namespace Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>CareLink</c>.</summary>
public sealed record CareLinkId
{
    public CareLinkId(int value)
    {
        if (value <= 0) throw new ArgumentException("CareLinkId must be a positive integer.", nameof(value));
        Value = value;
    }

    private CareLinkId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static CareLinkId FromRaw(int value)
    {
        return new CareLinkId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(CareLinkId id)
    {
        return id.Value;
    }

    public static explicit operator CareLinkId(int value)
    {
        return new CareLinkId(value);
    }
}
