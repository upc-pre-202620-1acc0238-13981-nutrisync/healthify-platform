namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>NutritionPlan</c>.</summary>
public sealed record PlanId
{
    public PlanId(int value)
    {
        if (value <= 0) throw new ArgumentException("PlanId must be a positive integer.", nameof(value));
        Value = value;
    }

    private PlanId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static PlanId FromRaw(int value)
    {
        return new PlanId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(PlanId id)
    {
        return id.Value;
    }

    public static explicit operator PlanId(int value)
    {
        return new PlanId(value);
    }
}
