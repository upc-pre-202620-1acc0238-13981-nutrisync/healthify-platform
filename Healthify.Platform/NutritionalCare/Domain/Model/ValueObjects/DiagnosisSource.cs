namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     Who chose the diagnosis code in step 2: the practitioner accepted the AI suggestion
///     ("Use suggestion") or picked it from the list ("Choose another").
/// </summary>
/// <remarks>
///     Accepting the deterministic suggestion (source "Rule", no AI generation behind it) is recorded as
///     <see cref="PractitionerSelected" />: only a real AI generation can be traced as accepted.
/// </remarks>
public sealed record DiagnosisSource
{
    public const string AiSuggestionAccepted = "AiSuggestionAccepted";
    public const string PractitionerSelected = "PractitionerSelected";

    public DiagnosisSource(string value)
    {
        var match = All.FirstOrDefault(s => s.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a valid diagnosis source. Allowed: {string.Join(", ", All)}.", nameof(value));
    }

    public static IReadOnlyList<string> All { get; } = [AiSuggestionAccepted, PractitionerSelected];

    public string Value { get; }

    public bool IsAiSuggestionAccepted => Value == AiSuggestionAccepted;

    public override string ToString()
    {
        return Value;
    }
}
