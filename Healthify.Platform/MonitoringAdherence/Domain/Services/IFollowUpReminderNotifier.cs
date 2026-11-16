namespace Healthify.Platform.MonitoringAdherence.Domain.Services;

/// <summary>Everything the appointment reminder email says, as primitives.</summary>
/// <param name="Recipient"><c>Patient</c> or <c>Practitioner</c>: decides the wording.</param>
/// <param name="RecipientEmail">Where it is sent.</param>
/// <param name="RecipientName">Who it is addressed to.</param>
/// <param name="Language"><c>es</c> or <c>en</c>.</param>
/// <param name="PatientName">The patient of the visit.</param>
/// <param name="PractitionerName">The practitioner of the visit.</param>
/// <param name="LocalStart">The visit in the time zone of the practice, with its own offset.</param>
/// <param name="Modality"><c>InPerson</c> or <c>Remote</c>.</param>
/// <param name="Preparation">The preparation codes of the visit.</param>
/// <param name="LeadMinutes">How long before the visit this reminder is.</param>
/// <param name="Rescheduled">True when the visit moved after an earlier reminder.</param>
public sealed record FollowUpReminderMessage(
    string Recipient,
    string RecipientEmail,
    string RecipientName,
    string Language,
    string PatientName,
    string PractitionerName,
    DateTimeOffset LocalStart,
    string Modality,
    IReadOnlyList<string> Preparation,
    int LeadMinutes,
    bool Rescheduled);

/// <summary>Writes the reminder and delivers it. The adapter lives in the infrastructure layer.</summary>
public interface IFollowUpReminderNotifier
{
    /// <summary>False when email is not configured; the policy then skips its cycle.</summary>
    bool IsConfigured { get; }

    /// <summary>Delivers one reminder.</summary>
    /// <returns>True when it was sent; false when nothing was sent because email is not configured.</returns>
    /// <exception cref="Exception">When the mail server refuses or cannot be reached.</exception>
    Task<bool> SendAsync(FollowUpReminderMessage message, CancellationToken cancellationToken = default);
}

/// <summary>The visit moment as the practice's calendar reads it.</summary>
public interface IFollowUpLocalClock
{
    /// <summary>The same instant with the offset of the practice's time zone at that instant.</summary>
    DateTimeOffset ToLocal(DateTimeOffset moment);
}
