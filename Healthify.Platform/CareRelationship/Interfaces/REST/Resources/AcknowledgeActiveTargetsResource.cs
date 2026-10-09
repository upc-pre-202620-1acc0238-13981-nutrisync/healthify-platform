namespace Healthify.Platform.CareRelationship.Interfaces.REST.Resources;

/// <summary>Payload of Subflow 2.4 - Active Targets Acknowledgement.</summary>
public record AcknowledgeActiveTargetsResource(int PlanVersion)
{
    /// <summary>
    ///     The version of the published targets the patient is acknowledging. Acknowledging does not
    ///     change the plan in any way.
    /// </summary>
    public int PlanVersion { get; init; } = PlanVersion;
}
