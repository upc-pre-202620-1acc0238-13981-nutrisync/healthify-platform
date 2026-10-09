using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.Iam.Domain.Model.Events;

/// <summary>
///     Subflow 1.1. Stays inside Iam: session and account infrastructure carries no domain meaning
///     outside this context. No other bounded context may declare a handler for it.
/// </summary>
/// <remarks>IAM-1 appends <c>FullName</c>.</remarks>
public record AccountCreated(int UserId, string Email, string Role, string FullName = "") : DomainEventBase;
