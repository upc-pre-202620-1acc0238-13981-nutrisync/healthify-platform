namespace Healthify.Platform.CareRelationship.Domain.Model.Queries;

/// <summary>
///     Read model: Care Link Status. Backs the Open Host Service the other five contexts query.
/// </summary>
public record GetActiveCareLinkByPatientIdQuery(int PatientId);
