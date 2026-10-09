using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.Iam.Domain.Model.Events;

/// <summary>Subflow 1.2. Stays inside Iam.</summary>
public record NavigationShellSelected(int SessionId, int UserId, string NavigationShell) : DomainEventBase;
