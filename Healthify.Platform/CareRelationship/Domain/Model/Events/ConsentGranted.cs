using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     Subflow 2.3. Deliberately crosses no boundary: Care Link Status is an Open Host Service that
///     the other contexts query synchronously, and asking whether a link is active is not reacting
///     to a past fact. No other context may declare a handler for this event.
/// </summary>
/// <remarks>
///     CR-2 appends <c>AiProcessingGranted</c>. When it is true, <see cref="AiProcessingConsentChanged" /> is
///     published as well: that is the event the AI functions react to.
/// </remarks>
public record ConsentGranted(int CareLinkId, int PatientId, int PractitionerId, string Scope,
    bool AiProcessingGranted = false) : DomainEventBase;
