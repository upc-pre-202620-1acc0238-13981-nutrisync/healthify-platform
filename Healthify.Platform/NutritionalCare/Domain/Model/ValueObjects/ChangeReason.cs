namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     Why a new plan version exists. Without it the version history is a list of numbers that
///     nobody can explain a year later.
/// </summary>
/// <remarks>
///     Enforces the business rule "Change Reason Required" (Subflow 3.6).
///     X-2: a reason the system writes is a <see cref="Code" /> with its <see cref="Data" />, which the client words in
///     the reader's language; <see cref="Value" /> keeps the Spanish sentence as the legacy fallback. A reason a
///     practitioner writes is <see cref="Custom" />: clinical text, kept as written and never translated.
/// </remarks>
public sealed record ChangeReason
{
    /// <summary>X-2. Written by a practitioner: <see cref="Value" /> is the reason.</summary>
    public const string Custom = "Custom";

    /// <summary>X-2 (NC-7). A version published from a consultation. Data: <c>date</c>.</summary>
    public const string NewConsultationCode = "NewConsultation";

    /// <summary>X-2 (NC-10). A version assigned from an AI proposal. Data: <c>date</c> and <c>signalType</c>.</summary>
    public const string SignalAdjustmentCode = "SignalAdjustment";

    private const int MaximumLength = 500;

    public ChangeReason(string value) : this(value, Custom, null)
    {
    }

    private ChangeReason(string value, string code, ChangeReasonData? data)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A change reason is required for every new plan version.",
                nameof(value));
        if (value.Length > MaximumLength)
            throw new ArgumentException($"The change reason exceeds the maximum length of {MaximumLength}.",
                nameof(value));
        Value = value.Trim();
        Code = code;
        Data = data;
    }

    /// <summary>Every code, for the client to know the closed list.</summary>
    public static IReadOnlyList<string> Codes { get; } = [Custom, NewConsultationCode, SignalAdjustmentCode];

    /// <summary>
    ///     The sentence. For <see cref="Custom" /> it is the practitioner's text; for the other codes it is the
    ///     Spanish fallback of clients that do not know the code (X-2).
    /// </summary>
    public string Value { get; }

    /// <summary>X-2. <see cref="Custom" />, <see cref="NewConsultationCode" /> or <see cref="SignalAdjustmentCode" />.</summary>
    public string Code { get; }

    /// <summary>X-2. The parameters of the code, or null for <see cref="Custom" />.</summary>
    public ChangeReasonData? Data { get; }

    public bool IsCustom => Code == Custom;

    /// <summary>
    ///     NC-7 - The reason written for a version published from a consultation: "Nueva consulta del 18 sept.
    ///     2026". X-2: code <see cref="NewConsultationCode" /> with the date; the sentence stays as the fallback, with
    ///     the months spelled here rather than taken from the culture data of the server.
    /// </summary>
    public static ChangeReason NewConsultation(DateOnly date)
    {
        return new ChangeReason($"Nueva consulta del {Spelled(date)}", NewConsultationCode,
            new ChangeReasonData(date, null));
    }

    /// <summary>
    ///     NC-10 - The reason of a version assigned from an AI proposal: "Ajuste por señal: desviación sostenida del
    ///     8 sept. 2026", the day the signal reached the inbox. X-2: code <see cref="SignalAdjustmentCode" /> with
    ///     that date and the signal type.
    /// </summary>
    public static ChangeReason SignalAdjustment(DateOnly signalDate, string signalType = SignalType.SustainedDeviation)
    {
        return new ChangeReason($"Ajuste por señal: desviación sostenida del {Spelled(signalDate)}",
            SignalAdjustmentCode, new ChangeReasonData(signalDate, signalType));
    }

    /// <summary>
    ///     Rebuilds a stored reason without validating it again. A row without a code (written before X-2 and not
    ///     recognised by its backfill) reads as <see cref="Custom" />, and an unknown code as well.
    /// </summary>
    public static ChangeReason? Rehydrate(string? value, string? code, ChangeReasonData? data)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var known = Codes.FirstOrDefault(c => c == code);
        return known is null or Custom
            ? new ChangeReason(value, Custom, null)
            : new ChangeReason(value, known, data);
    }

    private static string Spelled(DateOnly date)
    {
        string[] months = ["ene.", "feb.", "mar.", "abr.", "may.", "jun.", "jul.", "ago.", "sept.", "oct.", "nov.", "dic."];
        return $"{date.Day} {months[date.Month - 1]} {date.Year}";
    }

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
///     X-2. Parameters of a <see cref="ChangeReason" /> code, stored as JSON in
///     <c>nutrition_plans.change_reason_data</c>: <c>{ "date": "2026-09-18" }</c> or
///     <c>{ "date": "2026-09-08", "signalType": "SustainedDeviation" }</c>.
/// </summary>
/// <param name="Date">NewConsultation: the clinical day of the consultation. SignalAdjustment: the day of the signal.</param>
/// <param name="SignalType">SignalAdjustment: the signal that led to the proposal (SustainedDeviation).</param>
public sealed record ChangeReasonData(DateOnly? Date, string? SignalType);
