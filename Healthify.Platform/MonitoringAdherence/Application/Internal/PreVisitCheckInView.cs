using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     Application DTO: a check in and whether it can still be edited, which depends on its visit (the
///     patient's "edit my answer" action). Not a domain type and not an HTTP resource.
/// </summary>
/// <param name="CheckIn">The check in.</param>
/// <param name="IsLocked">True once the visit is no longer scheduled or its hour has arrived.</param>
public record PreVisitCheckInView(PreVisitCheckIn CheckIn, bool IsLocked);
