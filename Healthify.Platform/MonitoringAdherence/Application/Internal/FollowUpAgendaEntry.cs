using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     Application DTO of the Practitioner Agenda: a visit and the name of its patient ("Ana
///     Flores"), read from Iam through its ACL in one batch for the whole agenda. Not a domain type and not an
///     HTTP resource.
/// </summary>
/// <param name="FollowUp">The visit.</param>
/// <param name="PatientFullName">The name, or null when Iam could not answer.</param>
public record FollowUpAgendaEntry(ScheduledFollowUp FollowUp, string? PatientFullName);
