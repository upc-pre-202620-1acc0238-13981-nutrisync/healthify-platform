namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>Who an appointment reminder is written to.</summary>
public sealed record ReminderRecipient
{
    public const string Patient = "Patient";
    public const string Practitioner = "Practitioner";

    public ReminderRecipient(string? value)
    {
        Value = value switch
        {
            Patient or Practitioner => value,
            _ => throw new ArgumentException(
                $"'{value}' is not a reminder recipient. Allowed: {Patient}, {Practitioner}.", nameof(value))
        };
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
