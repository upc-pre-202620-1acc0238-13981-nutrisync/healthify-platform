namespace Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

/// <summary>
///     The clinical justification a practitioner records when discharging a patient.
/// </summary>
/// <remarks>
///     Enforces the business rule "Clinical Reason Required" (Subflow 2.5). Note the asymmetry with
///     consent withdrawal, which requires no justification at all: the professional explains a
///     discharge, the patient never explains leaving.
/// </remarks>
public sealed record ClinicalReason
{
    private const int MaximumLength = 500;

    public ClinicalReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A clinical reason is required to discharge a patient.", nameof(value));
        if (value.Length > MaximumLength)
            throw new ArgumentException(
                $"The clinical reason exceeds the maximum length of {MaximumLength}.", nameof(value));
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
