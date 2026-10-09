namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Subflow 3.1 - Close Assessment. After this the assessment is immutable.</summary>
public record CloseAssessmentCommand(int AssessmentId, int PractitionerId);
