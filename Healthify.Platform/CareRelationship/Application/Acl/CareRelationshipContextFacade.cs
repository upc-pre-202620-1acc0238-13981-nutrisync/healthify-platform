using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Interfaces.Acl;

namespace Healthify.Platform.CareRelationship.Application.Acl;

/// <inheritdoc cref="ICareRelationshipContextFacade" />
/// <remarks>
///     Delegates to the query service of its own context, never to a repository, so the application
///     layer is not bypassed.
/// </remarks>
public class CareRelationshipContextFacade(ICareLinkQueryService queryService)
    : ICareRelationshipContextFacade
{
    public async Task<bool> IsCareLinkActive(int patientId, int practitionerId, CancellationToken ct = default)
    {
        try
        {
            var link = await queryService.Handle(new GetActiveCareLinkByPatientIdQuery(patientId), ct);
            return link is not null && link.PractitionerId == practitionerId && link.IsActive;
        }
        catch
        {
            return false; // graceful degradation: no answer means no access
        }
    }

    public async Task<bool> HasAiProcessingConsent(int patientId, CancellationToken ct = default)
    {
        try
        {
            var link = await queryService.Handle(new GetActiveCareLinkByPatientIdQuery(patientId), ct);
            return link is { HasAiProcessingConsent: true };
        }
        catch
        {
            return false; // graceful degradation: no answer means no processing
        }
    }

    public async Task<IReadOnlyList<RosterCareLinkItem>> GetRosterCareLinks(int practitionerId,
        CancellationToken ct = default)
    {
        try
        {
            var links = await queryService.Handle(new GetCareLinksByPractitionerIdQuery(practitionerId), ct);

            return links
                .Where(l => l.IsActive || l.IsPendingConsent)
                .OrderBy(l => l.EstablishedAt)
                .Select(l => new RosterCareLinkItem(l.Id.Value, l.PatientId,
                    l.IsActive ? "Active" : "PendingConsent", l.EstablishedAt))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<CareLinkStatusItem?> GetActiveCareLinkByPatientId(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var link = await queryService.Handle(new GetActiveCareLinkByPatientIdQuery(patientId), ct);
            return link is null
                ? null
                : new CareLinkStatusItem(
                    link.Id.Value,
                    link.PatientId,
                    link.PractitionerId,
                    link.IsActive,
                    link.Consent is { IsGranted: true },
                    link.LastAcknowledgedVersion,
                    link.EstablishedAt,
                    link.LastAcknowledgedAt);
        }
        catch
        {
            return null;
        }
    }
}
