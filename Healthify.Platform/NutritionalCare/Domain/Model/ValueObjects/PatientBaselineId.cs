namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>PatientBaseline</c>.</summary>
public sealed record PatientBaselineId
{
    public PatientBaselineId(int value)
    {
        if (value <= 0) throw new ArgumentException("PatientBaselineId must be a positive integer.", nameof(value));
        Value = value;
    }

    private PatientBaselineId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static PatientBaselineId FromRaw(int value)
    {
        return new PatientBaselineId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(PatientBaselineId id)
    {
        return id.Value;
    }

    public static explicit operator PatientBaselineId(int value)
    {
        return new PatientBaselineId(value);
    }
}
