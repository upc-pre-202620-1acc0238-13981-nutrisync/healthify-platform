namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>Payload of Subflow 2.5 - Discharge.</summary>
public record DischargePatientResource(string ClinicalReason)
{
    /// <summary>
    ///     Why the treatment is being closed. Required. Note the asymmetry with withdrawing consent,
    ///     which asks the patient for no justification at all.
    /// </summary>
    public string ClinicalReason { get; init; } = ClinicalReason;
}
