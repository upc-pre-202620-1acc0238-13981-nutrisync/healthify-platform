namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Subflow 3.1 - Take Clinical Measurement. Only a practitioner measures.</summary>
/// <remarks>
///     NC-3 keeps the free text protocol and the height required on the stand-alone endpoint, and
///     appends the optional <paramref name="ProtocolChecks" /> of the EV-2 checklist.
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
