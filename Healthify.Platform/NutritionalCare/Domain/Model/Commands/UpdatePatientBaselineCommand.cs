namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>NC-1 - Edit the patient baseline from the Summary tab or from EV-2.</summary>
public record UpdatePatientBaselineCommand(
    int PatientId,
    int PractitionerId,
    DateOnly BirthDate,
    string BiologicalSex,
    decimal HeightCm,
    IReadOnlyList<string> Conditions);
