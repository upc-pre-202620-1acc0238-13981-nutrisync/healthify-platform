namespace Healthify.Platform.Iam.Domain.Model.Queries;

/// <summary>
///     IAM-1. Several accounts in one read, for the listings of other contexts (roster, inbox, agenda) so
///     that showing a name per row is not one query per row.
/// </summary>
public record GetUsersByIdsQuery(IReadOnlyCollection<int> UserIds);
