namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>NC-1 - Record the patient baseline (EV-1). Recorded once, edited afterwards.</summary>
public record RecordPatientBaselineCommand(
    int PatientId,
    int PractitionerId,
    DateOnly BirthDate,
    string BiologicalSex,
    decimal HeightCm,
    IReadOnlyList<string> Conditions);
