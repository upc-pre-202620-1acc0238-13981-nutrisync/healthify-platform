using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.Iam.Domain.Model.Events;

/// <summary>
///     Subflow 1.2. Consumed by the only policy of this context, which selects the navigation shell.
///     The subscriber lives in Iam itself, so the event never crosses a boundary.
/// </summary>
public record RoleClaimIssued(int SessionId, int UserId, string Role) : DomainEventBase;
