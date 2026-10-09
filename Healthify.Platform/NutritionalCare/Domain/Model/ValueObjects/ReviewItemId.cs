namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>ReviewItem</c>.</summary>
public sealed record ReviewItemId
{
    public ReviewItemId(int value)
    {
        if (value <= 0) throw new ArgumentException("ReviewItemId must be a positive integer.", nameof(value));
        Value = value;
    }

    private ReviewItemId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static ReviewItemId FromRaw(int value)
    {
        return new ReviewItemId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(ReviewItemId id)
    {
        return id.Value;
    }

    public static explicit operator ReviewItemId(int value)
    {
        return new ReviewItemId(value);
    }
}
