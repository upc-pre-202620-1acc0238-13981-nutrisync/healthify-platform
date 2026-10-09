namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>NC-2/NC-3. The step of the guided consultation the practitioner is on (EV-2 to EV-5).</summary>
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

    /// <summary>1 to 4, as shown in "PASO n DE 4".</summary>
    public int Number => Array.IndexOf(Ordered, Value) + 1;

    public override string ToString()
    {
        return Value;
    }
}
