namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Subflow 3.5 and 3.6 - Publish Active Targets. Issued by the policies that react to a plan
///     being published or adjusted, never by a user. It has no REST endpoint.
/// </summary>
public record PublishActiveTargetsCommand(int PlanId);
