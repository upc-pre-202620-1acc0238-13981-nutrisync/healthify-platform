namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Subflow 3.1 - Record Assessment. Clinical phase 1.</summary>
/// <remarks>
///     Every original field and its rules stay for assessments recorded before the guided consultation, and
///     the structured values are appended as optional: <paramref name="ActivityLevelCode" /> (closed list),
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
