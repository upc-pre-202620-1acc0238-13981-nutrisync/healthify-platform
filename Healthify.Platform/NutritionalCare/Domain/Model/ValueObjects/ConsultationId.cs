namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>Consultation</c>.</summary>
public sealed record ConsultationId
{
    public ConsultationId(int value)
    {
        if (value <= 0) throw new ArgumentException("ConsultationId must be a positive integer.", nameof(value));
        Value = value;
    }

    private ConsultationId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static ConsultationId FromRaw(int value)
    {
        return new ConsultationId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(ConsultationId id)
    {
        return id.Value;
    }

    public static explicit operator ConsultationId(int value)
    {
        return new ConsultationId(value);
    }
}
