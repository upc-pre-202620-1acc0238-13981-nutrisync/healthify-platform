namespace Healthify.Platform.ReadModels.Interfaces.REST.Resources;

/// <summary>
///     RM-1. One patient of the practitioner's roster (PR1). Only Active and PendingConsent links are listed.
/// </summary>
/// <param name="PatientId">Identifier of the patient.</param>
/// <param name="CareLinkId">Identifier of the link.</param>
/// <param name="FullName">"Ana Flores"; empty when it could not be read.</param>
/// <param name="Initial">The letter of the avatar; '?' when the name could not be read.</param>
/// <param name="LinkedSince">"Vinculada desde 12 mar. 2026".</param>
/// <param name="LinkStatus">PendingConsent or Active.</param>
/// <param name="HasBaseline">False leads to PAC-0.</param>
/// <param name="ActivePlanVersion">The published version in force, or null ("sin plan").</param>
/// <param name="IsNew">"Nueva": no baseline or no published plan yet.</param>
/// <param name="NextFollowUpAt">The next visit on the calendar, or null.</param>
/// <param name="HasConsultationInProgress">PAC-1.C: a consultation to resume.</param>
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

    /// <summary>"Vinculada desde 12 mar. 2026": when the link was established.</summary>
    public DateTimeOffset LinkedSince { get; init; } = LinkedSince;

    /// <summary>PendingConsent or Active.</summary>
    public string LinkStatus { get; init; } = LinkStatus;

    /// <summary>Whether the patient has a baseline. False opens PAC-0.</summary>
    public bool HasBaseline { get; init; } = HasBaseline;

    /// <summary>"Plan versión 3". Null means "sin plan".</summary>
    public int? ActivePlanVersion { get; init; } = ActivePlanVersion;

    /// <summary>"Nueva": no baseline or no published plan yet.</summary>
    public bool IsNew { get; init; } = IsNew;

    /// <summary>The next visit on the calendar, or null.</summary>
    public DateTimeOffset? NextFollowUpAt { get; init; } = NextFollowUpAt;

    /// <summary>PAC-1.C: there is a consultation to resume.</summary>
    public bool HasConsultationInProgress { get; init; } = HasConsultationInProgress;

    /// <summary>A signal of this patient waits in the practitioner inbox.</summary>
    public bool HasOpenReviewItem { get; init; } = HasOpenReviewItem;
}
