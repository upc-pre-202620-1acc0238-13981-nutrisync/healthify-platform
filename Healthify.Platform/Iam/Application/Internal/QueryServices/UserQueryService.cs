using Healthify.Platform.Iam.Application.QueryServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Queries;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;

namespace Healthify.Platform.Iam.Application.Internal.QueryServices;

public class UserQueryService(IUserRepository userRepository) : IUserQueryService
{
    public async Task<User?> Handle(GetUserByIdQuery query, CancellationToken cancellationToken = default)
    {
        return await userRepository.FindByIdAsync(query.UserId, cancellationToken);
    }

    public async Task<User?> Handle(GetUserByEmailQuery query, CancellationToken cancellationToken = default)
    {
        Email email;
        try
        {
            email = new Email(query.Email);
        }
        catch (ArgumentException)
        {
            // A malformed address matches no account. Queries report absence, not failure.
            return null;
        }

        return await userRepository.FindByEmailAsync(email, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> Handle(GetUsersByIdsQuery query,
        CancellationToken cancellationToken = default)
    {
        var ids = query.UserIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return [];

        return await userRepository.ListByIdsAsync(ids, cancellationToken);
    }
}
