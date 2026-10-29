namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Record the patient baseline. Recorded once, edited afterwards.</summary>
public record RecordPatientBaselineCommand(
    int PatientId,
    int PractitionerId,
    DateOnly BirthDate,
    string BiologicalSex,
    decimal HeightCm,
    IReadOnlyList<string> Conditions);
