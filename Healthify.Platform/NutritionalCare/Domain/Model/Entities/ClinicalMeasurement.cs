using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Entities;

/// <summary>
///     One anthropometric reading taken by the practitioner, with the protocol that was followed.
///     Together these form the Anthropometry of an assessment.
/// </summary>
/// <remarks>
///     A Clinical Measurement and a Self Weigh In are deliberately different things. This one is
///     taken by a professional under a recorded protocol and carries clinical authority; the other
///     is the patient at home and only means something as a trend. The two series are never merged.
/// </remarks>
public class ClinicalMeasurement
{
    /// <summary>Plausibility ranges of the guided consultation (NC-3).</summary>
    private const decimal MinimumWeightKg = 20m, MaximumWeightKg = 350m;

    private const decimal MinimumWaistCm = 40m, MaximumWaistCm = 200m;
    private const decimal MinimumBodyFat = 3m, MaximumBodyFat = 70m;

    private List<string>? _protocolChecks;

    /// <summary>Required by EF Core.</summary>
    protected ClinicalMeasurement()
    {
    }

    /// <summary>Stand-alone endpoint (Subflow 3.1): height and free text protocol come with the request.</summary>
    internal ClinicalMeasurement(
        decimal weightKg,
        decimal heightCm,
        MeasurementProtocol protocol,
        decimal? bodyFatPercentage,
        decimal? waistCircumferenceCm,
        MeasurementProtocolChecklist? protocolChecks = null)
    {
        if (weightKg is < 20m or > 400m)
            throw new ArgumentException("The weight must be between 20 and 400 kg.", nameof(weightKg));
        if (heightCm is < 80m or > 250m)
            throw new ArgumentException("The height must be between 80 and 250 cm.", nameof(heightCm));
        if (bodyFatPercentage is < 1m or > 70m)
            throw new ArgumentException("The body fat percentage must be between 1 and 70.",
                nameof(bodyFatPercentage));
        if (waistCircumferenceCm is < 30m or > 250m)
            throw new ArgumentException("The waist circumference must be between 30 and 250 cm.",
                nameof(waistCircumferenceCm));

        Assign(weightKg, heightCm, protocol, bodyFatPercentage, waistCircumferenceCm, protocolChecks);
    }

    /// <summary>
    ///     Guided consultation (NC-3): the height is the baseline's, taken as a snapshot so the historic
    ///     body mass index never changes, and the protocol is the EV-2 checklist.
    /// </summary>
    internal ClinicalMeasurement(
        decimal weightKg,
        HeightCm height,
        MeasurementProtocolChecklist protocolChecks,
        decimal? bodyFatPercentage,
        decimal? waistCircumferenceCm)
    {
        EnsurePlausible(weightKg, waistCircumferenceCm, bodyFatPercentage);

        Assign(weightKg, height.Value, new MeasurementProtocol(protocolChecks.ToSummary()), bodyFatPercentage,
            waistCircumferenceCm, protocolChecks);
    }

    public int Id { get; private set; }

    /// <summary>
    ///     Foreign key to the owning assessment, inside the same aggregate. Typed like the principal
    ///     key it points at, because EF Core requires both ends of a relationship to share a CLR type.
    /// </summary>
    public AssessmentId AssessmentId { get; private set; } = null!;

    public decimal WeightKg { get; private set; }

    /// <summary>In the guided consultation, a snapshot of the baseline height on the day it was taken.</summary>
    public decimal HeightCm { get; private set; }

    /// <summary>
    ///     Business rule: Measurement Protocol Recorded (Subflow 3.1). Free text on the stand-alone
    ///     endpoint; in the guided consultation, the readable summary of <see cref="ProtocolChecks" />.
    /// </summary>
    public MeasurementProtocol Protocol { get; private set; } = null!;

    /// <summary>NC-3. The EV-2 checklist, or null for measurements that only have the free text protocol.</summary>
    public IReadOnlyList<string>? ProtocolChecks => _protocolChecks;

    public decimal? BodyFatPercentage { get; private set; }
    public decimal? WaistCircumferenceCm { get; private set; }

    /// <summary>NC-3. kg/m², one decimal. Stored so the historic value never changes.</summary>
    public decimal BmiKgM2 { get; private set; }

    /// <summary>NC-3. WHO category of <see cref="BmiKgM2" />. Never leaves this bounded context.</summary>
    public string BmiCategory { get; private set; } = null!;

    public DateTimeOffset TakenAt { get; private set; }

    /// <summary>NC-3 - Business rule: Plausible Measurement. Weight 20-350 kg, waist 40-200 cm, fat 3-70 %.</summary>
    public static void EnsurePlausible(decimal weightKg, decimal? waistCm, decimal? bodyFatPercentage)
    {
        if (weightKg is < MinimumWeightKg or > MaximumWeightKg)
            throw new ArgumentException($"The weight must be between {MinimumWeightKg} and {MaximumWeightKg} kg.",
                nameof(weightKg));
        if (waistCm is < MinimumWaistCm or > MaximumWaistCm)
            throw new ArgumentException($"The waist must be between {MinimumWaistCm} and {MaximumWaistCm} cm.",
                nameof(waistCm));
        if (bodyFatPercentage is < MinimumBodyFat or > MaximumBodyFat)
            throw new ArgumentException(
                $"The body fat percentage must be between {MinimumBodyFat} and {MaximumBodyFat}.",
                nameof(bodyFatPercentage));
    }

    private void Assign(decimal weightKg, decimal heightCm, MeasurementProtocol protocol,
        decimal? bodyFatPercentage, decimal? waistCircumferenceCm, MeasurementProtocolChecklist? protocolChecks)
    {
        WeightKg = decimal.Round(weightKg, 2);
        HeightCm = decimal.Round(heightCm, 2);
        Protocol = protocol;
        _protocolChecks = protocolChecks?.Checks.ToList();
        BodyFatPercentage = bodyFatPercentage is null ? null : decimal.Round(bodyFatPercentage.Value, 2);
        WaistCircumferenceCm = waistCircumferenceCm is null
            ? null
            : decimal.Round(waistCircumferenceCm.Value, 2);

        var bmi = new BodyMassIndex(WeightKg, HeightCm);
        BmiKgM2 = bmi.Value;
        BmiCategory = bmi.Category;
        TakenAt = DateTimeOffset.UtcNow;
    }
}
