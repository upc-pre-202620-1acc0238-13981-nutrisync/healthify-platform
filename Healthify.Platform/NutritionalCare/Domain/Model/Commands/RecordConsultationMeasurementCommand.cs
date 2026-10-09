namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-3 - Step 1 of the guided consultation (EV-2). Creates a closed assessment with its clinical
///     measurement. Height, age, sex and medical history come from the baseline as a snapshot.
/// </summary>
public record RecordConsultationMeasurementCommand(
    int ConsultationId,
    int PractitionerId,
    decimal WeightKg,
    decimal? WaistCm,
    decimal? BodyFatPercentage,
    IReadOnlyList<string> ProtocolChecks,
    string ActivityLevel,
    EatingHabitsDto? Habits,
    BiochemistryDto? Biochemistry);
