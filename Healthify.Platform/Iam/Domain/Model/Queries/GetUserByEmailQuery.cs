namespace Healthify.Platform.Iam.Domain.Model.Queries;

/// <summary>Used by sign-in to resolve the account before credentials are verified.</summary>
public record GetUserByEmailQuery(string Email);
