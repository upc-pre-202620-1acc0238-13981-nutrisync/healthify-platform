namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>NutritionalDiagnosis</c>.</summary>
public sealed record DiagnosisId
{
    public DiagnosisId(int value)
    {
        if (value <= 0) throw new ArgumentException("DiagnosisId must be a positive integer.", nameof(value));
        Value = value;
    }

    private DiagnosisId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static DiagnosisId FromRaw(int value)
    {
        return new DiagnosisId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(DiagnosisId id)
    {
        return id.Value;
    }

    public static explicit operator DiagnosisId(int value)
    {
        return new DiagnosisId(value);
    }
}
