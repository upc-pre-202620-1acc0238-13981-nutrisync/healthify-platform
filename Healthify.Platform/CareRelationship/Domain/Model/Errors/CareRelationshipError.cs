namespace Healthify.Platform.CareRelationship.Domain.Model.Errors;

/// <summary>Every failure this bounded context can report. One value per business rule it enforces.</summary>
public enum CareRelationshipError
{
    /// <summary>Rule: Practitioner Only (Subflow 2.1).</summary>
    PractitionerOnly,

    /// <summary>Rule: Expiration Date Required (Subflow 2.1).</summary>
    ExpirationDateRequired,

    InvitationNotFound,

    /// <summary>Rule: Invitation Must Be Valid (Subflow 2.2).</summary>
    InvitationExpired,

    /// <summary>Rule: Invitation Must Be Unused, and Single Use Token (Subflows 2.1 and 2.2).</summary>
    InvitationAlreadyRedeemed,

    /// <summary>Rule: Invitation Must Be Valid (Subflow 2.2).</summary>
    InvitationNotValid,

    /// <summary>Rule: Redeemed Invitation Cannot Expire (Subflow 2.1).</summary>
    RedeemedInvitationCannotExpire,

    /// <summary>Rule: Patient Cannot Self Link (Subflow 2.2).</summary>
    PatientCannotSelfLink,

    /// <summary>Rule: One Active Link Per Patient (Subflow 2.2).</summary>
    PatientAlreadyHasActiveLink,

    CareLinkNotFound,

    /// <summary>Rule: Consent Scope Recorded (Subflow 2.3).</summary>
    ConsentScopeRequired,

    ConsentAlreadyGranted,

    /// <summary>Rules: No Access Without Consent, and Consent Always Revocable (Subflow 2.3).</summary>
    NoActiveConsent,

    /// <summary>Rule: One Pending Version At A Time (Subflow 2.4).</summary>
    PendingVersionAlreadyExists,

    NoPendingTargetsVersion,

    /// <summary>Rule: Acknowledged Version Not Newer Than Active (Subflow 2.4).</summary>
    AcknowledgedVersionNewerThanActive,

    /// <summary>Rule: Clinical Reason Required (Subflow 2.5).</summary>
    ClinicalReasonRequired,

    /// <summary>Rule: Revoked Link Kept With Revocation Date (Subflow 2.5).</summary>
    CareLinkAlreadyRevoked,

    /// <summary>Rule: Discharged Link Never Reactivated (Subflow 2.5).</summary>
    DischargedLinkCannotBeReactivated,

    /// <summary>Rule: Link Starts Inactive Until Consent (Subflow 2.2). The link is not active yet.</summary>
    CareLinkNotActive,

    /// <summary>
    ///     Rule: Switching Requires Another Practitioner (CR-1). A redemption that replaces the active
    ///     link was asked for, but the active link is already with the practitioner who issued the
    ///     invitation.
    /// </summary>
    AlreadyLinkedToThisPractitioner,

    /// <summary>
    ///     Rule: AI Preferences Require Consent (IA-1). An AI function was turned on while the patient does not
    ///     consent to AI processing (CR-2).
    /// </summary>
    AiConsentRequiredToEnableFeature,

    UnexpectedError
}
