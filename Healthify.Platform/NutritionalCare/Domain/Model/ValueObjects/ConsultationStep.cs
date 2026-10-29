namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>The step of the guided consultation the practitioner is on.</summary>
public sealed record ConsultationStep
{
    public const string Measurement = "Measurement";
    public const string Diagnosis = "Diagnosis";
    public const string Targets = "Targets";
    public const string Publication = "Publication";

    private static readonly string[] Ordered = [Measurement, Diagnosis, Targets, Publication];

    public ConsultationStep(string value)
    {
        var match = Ordered.FirstOrDefault(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a valid consultation step. Allowed: {string.Join(", ", Ordered)}.",
            nameof(value));
    }

    public string Value { get; }

    /// <summary>1 to 4, as shown in "STEP n OF 4".</summary>
    public int Number => Array.IndexOf(Ordered, Value) + 1;

    public override string ToString()
    {
        return Value;
    }
}
