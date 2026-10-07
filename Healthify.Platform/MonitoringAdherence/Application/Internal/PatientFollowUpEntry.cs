using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     MA-3. Application DTO of the visits a patient reads (PT3, PT20, PT25): a visit and the name of the
///     practitioner who scheduled it, read from Iam through its ACL in one batch. Not a domain type and not an HTTP
///     resource.
/// </summary>
/// <param name="FollowUp">The visit.</param>
/// <param name="PractitionerFullName">The name, or null when Iam could not answer.</param>
public record PatientFollowUpEntry(ScheduledFollowUp FollowUp, string? PractitionerFullName);
