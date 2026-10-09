namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>NutritionalAssessment</c>.</summary>
public sealed record AssessmentId
{
    public AssessmentId(int value)
    {
        if (value <= 0) throw new ArgumentException("AssessmentId must be a positive integer.", nameof(value));
        Value = value;
    }

    private AssessmentId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static AssessmentId FromRaw(int value)
    {
        return new AssessmentId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(AssessmentId id)
    {
        return id.Value;
    }

    public static explicit operator AssessmentId(int value)
    {
        return new AssessmentId(value);
    }
}
