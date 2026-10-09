namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-4. The nutritional diagnosis, chosen from the closed list of EV-3. It replaces the free text
///     statement of the original diagnosis.
/// </summary>
/// <remarks>
///     The codes and the WHO cut-offs are shared with <see cref="BodyMassIndex" />: the deterministic
///     suggestion is the category of the index. The text shown is localized by the client; the code is
///     what the record keeps.
/// </remarks>
public sealed record DiagnosisCode
{
    public const string Underweight = BodyMassIndex.Underweight;
    public const string NormalWeight = BodyMassIndex.NormalWeight;
    public const string OverweightGradeI = BodyMassIndex.OverweightGradeI;
    public const string ObesityGradeI = BodyMassIndex.ObesityGradeI;
    public const string ObesityGradeII = BodyMassIndex.ObesityGradeII;
    public const string ObesityGradeIII = BodyMassIndex.ObesityGradeIII;

    public DiagnosisCode(string value)
    {
        var match = All.FirstOrDefault(c => c.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
        Value = match ?? throw new ArgumentException(
            $"'{value}' is not a valid diagnosis code. Allowed: {string.Join(", ", All)}.", nameof(value));
    }

    /// <summary>Every code, from the lowest to the highest index (EV-3 order).</summary>
    public static IReadOnlyList<string> All { get; } =
        [Underweight, NormalWeight, OverweightGradeI, ObesityGradeI, ObesityGradeII, ObesityGradeIII];

    public string Value { get; }

    /// <summary>Position in <see cref="All" />, so two codes can be compared by grade (IA-6).</summary>
    public int Grade => All.ToList().IndexOf(Value);

    /// <summary>Business rule: no weight loss is proposed to someone who is not above a normal weight (NC-5).</summary>
    public bool IsAtOrBelowNormalWeight => Value is Underweight or NormalWeight;

    /// <summary>
    ///     Business rule: AI Suggestion Within One Grade Of The Index (IA-6). A suggested code may move one grade away
    ///     from the category of the body mass index (the waist or the body fat can justify it), never more: IMC 26.3
    ///     may become ObesityGradeI, never ObesityGradeII.
    /// </summary>
    public bool IsWithinOneGradeOf(DiagnosisCode other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Math.Abs(Grade - other.Grade) <= 1;
    }

    /// <summary>The deterministic suggestion: the WHO category of the body mass index (NC-4 fallback).</summary>
    public static DiagnosisCode FromBodyMassIndex(decimal bmi)
    {
        return new DiagnosisCode(BodyMassIndex.CategoryFor(bmi));
    }

    public override string ToString()
    {
        return Value;
    }
}
