using Healthify.Platform.Iam.Application.QueryServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Queries;
using Healthify.Platform.Iam.Interfaces.Acl;

namespace Healthify.Platform.Iam.Application.Acl;

/// <inheritdoc cref="IIamContextFacade" />
/// <remarks>
///     Delegates to the query service of its own context, never to a repository, so the application
///     layer is not bypassed.
/// </remarks>
public class IamContextFacade(IUserQueryService userQueryService) : IIamContextFacade
{
    public async Task<UserIdentityItem?> GetUserById(int userId, CancellationToken ct = default)
    {
        try
        {
            var user = await userQueryService.Handle(new GetUserByIdQuery(userId), ct);
            return user is null ? null : ToItem(user);
        }
        catch
        {
            return null; // graceful degradation: never propagates
        }
    }

    public async Task<IReadOnlyDictionary<int, UserIdentityItem>> GetUsersByIds(IEnumerable<int> userIds,
        CancellationToken ct = default)
    {
        try
        {
            var users = await userQueryService.Handle(new GetUsersByIdsQuery(userIds.ToList()), ct);
            return users.ToDictionary(u => u.Id.Value, ToItem);
        }
        catch
        {
            return new Dictionary<int, UserIdentityItem>();
        }
    }

    public async Task<string?> GetPreferredLanguage(int userId, CancellationToken ct = default)
    {
        try
        {
            var user = await userQueryService.Handle(new GetUserByIdQuery(userId), ct);
            return user?.PreferredLanguage.Value;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsPractitioner(int userId, CancellationToken ct = default)
    {
        try
        {
            var user = await userQueryService.Handle(new GetUserByIdQuery(userId), ct);
            return user is not null && user.Role.IsPractitioner;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsPatient(int userId, CancellationToken ct = default)
    {
        try
        {
            var user = await userQueryService.Handle(new GetUserByIdQuery(userId), ct);
            return user is not null && user.Role.IsPatient;
        }
        catch
        {
            return false;
        }
    }

    private static UserIdentityItem ToItem(User user)
    {
        return new UserIdentityItem(user.Id.Value, user.Email.Value, user.Role.Value, user.GivenNames,
            user.FamilyNames);
    }
}
