namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Subflow 3.1 - Record Assessment. Clinical phase 1.</summary>
/// <remarks>
///     NC-3 keeps every original field and its rules for the stand-alone endpoint, and appends the
///     structured values as optional: <paramref name="ActivityLevelCode" /> (closed list),
///     <paramref name="EatingHabitsData" /> and <paramref name="BiochemistryData" />. The guided
///     consultation uses <see cref="RecordConsultationMeasurementCommand" /> instead.
/// </remarks>
public record RecordAssessmentCommand(
    int PatientId,
    int PractitionerId,
    string Habits,
    string MedicalHistory,
    string PhysicalActivity,
    string? Biochemistry,
    int AgeYears,
    string BiologicalSex,
    int? SupersedesAssessmentId,
    string? ActivityLevelCode = null,
    EatingHabitsDto? EatingHabitsData = null,
    BiochemistryDto? BiochemistryData = null);
