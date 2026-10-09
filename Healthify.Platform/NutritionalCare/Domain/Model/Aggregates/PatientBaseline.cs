using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

/// <summary>
///     NC-1. The facts about the patient that do not change from one consultation to the next:
///     birth date, biological sex, height and medical history from a closed list.
/// </summary>
/// <remarks>
///     Recorded once and edited afterwards. The age is never stored: it is derived from the birth
///     date on the day it is asked for, so it updates itself on the patient's birthday.
///     DECISIÓN §12-#13: editing the baseline never rewrites past assessments; each assessment keeps
///     its own snapshot of age, sex, height and conditions.
/// </remarks>
public partial class PatientBaseline
{
    private const int MinimumAgeYears = 1;
    private const int MaximumAgeYears = 120;

    private List<string> _conditions = [];

    /// <summary>Required by EF Core.</summary>
    protected PatientBaseline()
    {
    }

    public PatientBaseline(RecordPatientBaselineCommand command, DateOnly today)
    {
        PatientId = command.PatientId;
        RecordedBy = command.PractitionerId;
        Apply(command.BirthDate, command.BiologicalSex, command.HeightCm, command.Conditions, today);
    }

    public PatientBaselineId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account. Unique: one baseline per patient.</summary>
    public int PatientId { get; private set; }

    public DateOnly BirthDate { get; private set; }

    /// <summary>
    ///     NOTE: technical field. True only for baselines backfilled from an assessment that stored an
    ///     age instead of a birth date; the first edit by a practitioner confirms the date.
    /// </summary>
    public bool BirthDateEstimated { get; private set; }

    public BiologicalSex BiologicalSex { get; private set; } = null!;

    public HeightCm Height { get; private set; } = null!;

    /// <summary>Medical history from the closed list. Empty means none of them.</summary>
    public IReadOnlyList<MedicalCondition> Conditions =>
        _conditions.Select(c => new MedicalCondition(c)).ToList();

    /// <summary>Cross-context reference to the practitioner who first recorded the baseline.</summary>
    public int RecordedBy { get; private set; }

    /// <summary>Completed years on <paramref name="date" />. A 29 February birthday counts on 28 February.</summary>
    public int AgeAt(DateOnly date)
    {
        var age = date.Year - BirthDate.Year;
        if (BirthDate.AddYears(age) > date) age--;
        return age;
    }

    /// <summary>NC-1 - Edit from the Summary tab or from EV-2.</summary>
    public void Update(UpdatePatientBaselineCommand command, DateOnly today)
    {
        Apply(command.BirthDate, command.BiologicalSex, command.HeightCm, command.Conditions, today);

        // A practitioner has now seen and confirmed the date, estimated or not.
        BirthDateEstimated = false;
    }

    /// <summary>Business rule: Plausible Birth Date (NC-1). Between 1 and 120 years before today.</summary>
    public static void EnsurePlausibleBirthDate(DateOnly birthDate, DateOnly today)
    {
        if (birthDate > today.AddYears(-MinimumAgeYears) || birthDate < today.AddYears(-MaximumAgeYears))
            throw new ArgumentException(
                $"The birth date must be between {MinimumAgeYears} and {MaximumAgeYears} years before today.",
                nameof(birthDate));
    }

    private void Apply(DateOnly birthDate, string biologicalSex, decimal heightCm,
        IEnumerable<string>? conditions, DateOnly today)
    {
        EnsurePlausibleBirthDate(birthDate, today);

        BirthDate = birthDate;
        BiologicalSex = new BiologicalSex(biologicalSex);
        Height = new HeightCm(heightCm);
        _conditions = MedicalCondition.ListFrom(conditions).Select(c => c.Value).ToList();
    }
}
