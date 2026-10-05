namespace Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>ReferenceFood</c>.</summary>
public sealed record ReferenceFoodId
{
    public ReferenceFoodId(int value)
    {
        if (value <= 0)
            throw new ArgumentException("ReferenceFoodId must be a positive integer.", nameof(value));
        Value = value;
    }

    private ReferenceFoodId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static ReferenceFoodId FromRaw(int value)
    {
        return new ReferenceFoodId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(ReferenceFoodId id)
    {
        return id.Value;
    }

    public static explicit operator ReferenceFoodId(int value)
    {
        return new ReferenceFoodId(value);
    }
}
