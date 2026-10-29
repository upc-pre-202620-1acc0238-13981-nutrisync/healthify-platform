namespace Healthify.Platform.ReadModels.Interfaces.REST.Resources;

/// <summary>
///     One patient of the practitioner's roster. Only Active and PendingConsent links are listed.
/// </summary>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="CareLinkId">Identifier of the link.</param>
/// <param name="FullName">"Ana Flores"; empty when it could not be read.</param>
/// <param name="Initial">The letter of the avatar; '?' when the name could not be read.</param>
/// <param name="LinkedSince">When the link was established.</param>
/// <param name="LinkStatus">PendingConsent or Active.</param>
/// <param name="HasBaseline">False leads to the empty state.</param>
/// <param name="ActivePlanVersion">The published version in force, or null (no plan).</param>
/// <param name="IsNew">New: no baseline or no published plan yet.</param>
/// <param name="NextFollowUpAt">The next visit on the calendar, or null.</param>
/// <param name="HasConsultationInProgress">A consultation to resume.</param>
/// <param name="HasOpenReviewItem">A signal of this patient waits in the inbox.</param>
public record PatientRosterItemResource(
    int PatientId,
    int CareLinkId,
    string FullName,
    char Initial,
    DateTimeOffset LinkedSince,
    string LinkStatus,
    bool HasBaseline,
    int? ActivePlanVersion,
    bool IsNew,
    DateTimeOffset? NextFollowUpAt,
    bool HasConsultationInProgress,
    bool HasOpenReviewItem)
{
    /// <summary>Identifier of the patient.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Identifier of the care link.</summary>
    public int CareLinkId { get; init; } = CareLinkId;

    /// <summary>"Ana Flores". Empty when Iam could not answer.</summary>
    public string FullName { get; init; } = FullName;

    /// <summary>The letter of the avatar. '?' when the name could not be read.</summary>
    public char Initial { get; init; } = Initial;

    /// <summary>When the link was established.</summary>
    public DateTimeOffset LinkedSince { get; init; } = LinkedSince;

    /// <summary>PendingConsent or Active.</summary>
    public string LinkStatus { get; init; } = LinkStatus;

    /// <summary>Whether the patient has a baseline. False opens the empty state.</summary>
    public bool HasBaseline { get; init; } = HasBaseline;

    /// <summary>The plan version in force. Null means no plan.</summary>
    public int? ActivePlanVersion { get; init; } = ActivePlanVersion;

    /// <summary>New: no baseline or no published plan yet.</summary>
    public bool IsNew { get; init; } = IsNew;

    /// <summary>The next visit on the calendar, or null.</summary>
    public DateTimeOffset? NextFollowUpAt { get; init; } = NextFollowUpAt;

    /// <summary>There is a consultation to resume.</summary>
    public bool HasConsultationInProgress { get; init; } = HasConsultationInProgress;

    /// <summary>A signal of this patient waits in the practitioner inbox.</summary>
    public bool HasOpenReviewItem { get; init; } = HasOpenReviewItem;
}
