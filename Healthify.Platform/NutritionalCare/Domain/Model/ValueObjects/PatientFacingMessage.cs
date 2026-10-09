namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-9. The practitioner's message to the patient for one plan version (PR14.IA "Mensaje para Ana: Notamos que
///     tus cenas son más ligeras. Probemos con estas ideas."), shown in PT4 and PT3.M. Never empty, trimmed, at most
///     500 characters.
/// </summary>
/// <remarks>
///     Business rule: A Person Confirms The Message (NC-9). Even when the AI drafts it (IA-8), it reaches a version
///     only through a practitioner's action, so it is that person's message. It must never carry the diagnosis
///     (Diagnosis Never Leaves The Context): a soft rule for a human text, enforced on the AI draft by its validator.
/// </remarks>
public sealed record PatientFacingMessage
{
    public const int MaximumLength = 500;

    public PatientFacingMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A message for the patient cannot be empty.", nameof(value));
        var trimmed = value.Trim();
        if (trimmed.Length > MaximumLength)
            throw new ArgumentException($"A message for the patient has at most {MaximumLength} characters.",
                nameof(value));
        Value = trimmed;
    }

    public string Value { get; }

    /// <summary>The message of an optional field: none when the field is absent or blank.</summary>
    /// <exception cref="ArgumentException">Longer than <see cref="MaximumLength" />.</exception>
    public static PatientFacingMessage? FromOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : new PatientFacingMessage(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
