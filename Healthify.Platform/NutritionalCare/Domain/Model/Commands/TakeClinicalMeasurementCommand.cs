namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Subflow 3.1 - Take Clinical Measurement. Only a practitioner measures.</summary>
/// <remarks>
///     The free text protocol and the required height stay for measurements taken before the guided
///     consultation, and the optional <paramref name="ProtocolChecks" /> of the step 1 checklist are appended.
/// </remarks>
public record TakeClinicalMeasurementCommand(
    int AssessmentId,
    int PractitionerId,
    decimal WeightKg,
    decimal HeightCm,
    string Protocol,
    decimal? BodyFatPercentage,
    decimal? WaistCircumferenceCm,
    IReadOnlyList<string>? ProtocolChecks = null);
