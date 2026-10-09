namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-2. What the practitioner chose in EV-5 before publishing: restriction codes, guideline codes and custom
///     guidelines, and (NC-9) the message for the patient. Kept with the consultation so "Lo que escribiste no se
///     perdió" (EV-5.E) holds even if the app closes.
/// </summary>
/// <remarks>
///     The values were already checked against the NC-6 catalogs, and the message against
///     <see cref="PatientFacingMessage" />, when the draft was saved.
/// </remarks>
public sealed record PublicationDraft
{
    public PublicationDraft(IEnumerable<string>? restrictions, IEnumerable<string>? guidelines,
        IEnumerable<string>? customGuidelines, string? patientMessage = null)
    {
        PatientMessage = PatientFacingMessage.FromOptional(patientMessage)?.Value;
        Restrictions = Clean(restrictions);
        Guidelines = Clean(guidelines);
        CustomGuidelines = Clean(customGuidelines);
        if (CustomGuidelines.Count > Guideline.MaximumCustomPerVersion)
            throw new ArgumentException(
                $"A plan version carries at most {Guideline.MaximumCustomPerVersion} custom guidelines.",
                nameof(customGuidelines));
    }

    public IReadOnlyList<string> Restrictions { get; }

    public IReadOnlyList<string> Guidelines { get; }

    public IReadOnlyList<string> CustomGuidelines { get; }

    /// <summary>NC-9. The message for the patient written in EV-5, or null.</summary>
    public string? PatientMessage { get; }

    public bool Equals(PublicationDraft? other)
    {
        return other is not null && Restrictions.SequenceEqual(other.Restrictions) &&
               Guidelines.SequenceEqual(other.Guidelines) && CustomGuidelines.SequenceEqual(other.CustomGuidelines) &&
               PatientMessage == other.PatientMessage;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(string.Join('\u001f', Restrictions), string.Join('\u001f', Guidelines),
            string.Join('\u001f', CustomGuidelines), PatientMessage);
    }

    private static List<string> Clean(IEnumerable<string>? values)
    {
        return (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().ToList();
    }
}
