namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Edit the patient baseline from the Summary tab or from step 1 of the consultation.</summary>
public record UpdatePatientBaselineCommand(
    int PatientId,
    int PractitionerId,
    DateOnly BirthDate,
    string BiologicalSex,
    decimal HeightCm,
    IReadOnlyList<string> Conditions);
