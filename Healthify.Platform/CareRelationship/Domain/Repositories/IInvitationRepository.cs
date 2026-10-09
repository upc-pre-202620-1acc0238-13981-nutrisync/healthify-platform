using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Domain.Repositories;

public interface IInvitationRepository : IBaseRepository<Invitation>
{
    Task<Invitation?> FindByTokenAsync(InvitationToken token, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Invitations past their expiration date that have neither been redeemed nor expired yet.
    ///     Feeds the time-driven policy "When Expiration Date Reached".
    /// </summary>
    Task<IEnumerable<Invitation>> ListExpirableAsync(DateTimeOffset asOf, int maxResults,
        CancellationToken cancellationToken = default);
}
