namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;

/// <summary>
///     Every failure this bounded context can report. One value per business rule it enforces.
/// </summary>
/// <remarks>
///     Read the list for what is missing. There is no value here for a meal that was too large, a day
///     that fell short or a week that went badly, because this context does not have an opinion about
///     any of those. It reports that it could not record something, never that what was recorded was
///     wrong.
/// </remarks>
public enum IntakeError
{
    ActiveTargetsCacheNotFound,

    /// <summary>Rule: Published Contract Only (Subflow 4.1).</summary>
    PublishedContractOnly,

    /// <summary>Rule: Provenance Required (Subflows 4.2, 4.3 and 4.4).</summary>
    ProvenanceRequired,

    /// <summary>Rule: Local Timestamp Required (Subflows 4.2 and 4.3).</summary>
    LocalTimestampRequired,

    /// <summary>Hotspot: how far back a patient may log. Read from Intake:RetroactiveLoggingWindowHours.</summary>
    RetroactiveLoggingWindowExceeded,

    DiaryEntryNotFound,

    /// <summary>
    ///     Rule: Entry Never Deleted (Subflow 4.2). Structural rather than checked: the aggregate has
    ///     no method that removes an entry and no endpoint reaches one. The value exists so that the
    ///     rule has a name in the code rather than only in a comment.
    /// </summary>
    DiaryEntryCannotBeDeleted,

    /// <summary>Rule: Food Resolved From Local Catalog (Subflows 4.2 and 4.3).</summary>
    ReferenceFoodNotResolved,

    /// <summary>Rule: there is nothing to confirm or adjust yet (Subflow 4.2).</summary>
    EstimateNotProposed,

    /// <summary>Rule: Proposal Kept Alongside Confirmation (Subflow 4.2). A confirmation happens once.</summary>
    EstimateAlreadyConfirmed,

    /// <summary>Rule: Confidence Always Attached (Subflow 4.2).</summary>
    ConfidenceRequired,

    /// <summary>Rule: Plausible Weight Range (Subflow 4.5).</summary>
    ImplausibleWeightValue,

    /// <summary>Rule: Protocol Compliance Declared (Subflow 4.5).</summary>
    ProtocolComplianceRequired,

    SelfWeighInNotFound,

    WeightTrendNotFound,

    /// <summary>Rule: Idempotency By Aggregate Id (Subflow 4.6), seen from inside one batch.</summary>
    DuplicatedClientEntryId,

    /// <summary>Rule: Declared Local Timestamp Never Rewritten (Subflow 4.6).</summary>
    LocalTimestampCannotBeRewritten,

    /// <summary>
    ///     This context is written by the patient and by nobody else. Reported when the session does
    ///     not belong to the patient the command is about.
    /// </summary>
    PatientWriteOnly,

    /// <summary>Rule: targets only reach a patient whose care link is live (Subflow 4.1).</summary>
    ActiveCareLinkRequired,

    /// <summary>Rule: Plan Adherence Required On Confirmation (IN-1). «Responde Sí o No».</summary>
    PlanAdherenceRequired,

    /// <summary>IN-1. The answer is not InPlan or OffPlan.</summary>
    InvalidPlanAdherence,

    /// <summary>IN-2. The confirmation sent with a photo log is neither AsProposed nor a complete adjustment.</summary>
    InvalidEstimateConfirmation,

    /// <summary>Rule: Idempotency By Aggregate Id (Subflow 4.6, IN-4). A queued reading without its device identifier.</summary>
    ClientEntryIdRequired,

    /// <summary>
    ///     Rule: Ideas Need Room For A Meal (IA-3). 150 kcal or less are left today. Not a failure of the patient:
    ///     the text says «Ya cubriste tu energía de hoy», never anything that accuses.
    /// </summary>
    NotEnoughRemaining,

    /// <summary>IA-3. The local date is missing, or it is not today on the patient's device (one day either side of the server).</summary>
    InvalidLocalDate,

    /// <summary>IN-6. A meal logged in a group needs one to ten items, each with a food and a positive portion.</summary>
    InvalidMealGroupItems,

    /// <summary>IN-6. The origin of a grouped meal is not a known kind (MealIdea).</summary>
    InvalidEntryOrigin,

    /// <summary>IN-7. The photo of the dish is missing or empty.</summary>
    PhotoRequired,

    /// <summary>Rule: Meal Photo Is JPEG Or WebP (IN-7), read from the bytes, not from the declared type.</summary>
    UnsupportedPhotoFormat,

    /// <summary>IN-7. The photo exceeds <c>Intake:MealPhoto:MaxBytes</c> (2 MB by default). 413.</summary>
    PhotoTooLarge,

    /// <summary>
    ///     IN-7. The AI did not produce a valid proposal for the photo (no dish, an invalid answer, or nutrients that
    ///     could not describe a real food). PT7.3: the app offers to log it by hand.
    /// </summary>
    PhotoNotRecognized,

    /// <summary>IN-7. No photo analysis with this identifier belongs to the patient.</summary>
    MealPhotoAnalysisNotFound,

    /// <summary>IN-7. The photo analysis is older than its lifetime (24 hours by default) and can no longer be logged.</summary>
    MealPhotoAnalysisExpired,

    UnexpectedError
}
