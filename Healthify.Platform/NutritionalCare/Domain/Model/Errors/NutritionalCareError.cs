namespace Healthify.Platform.NutritionalCare.Domain.Model.Errors;

/// <summary>Every failure this bounded context can report. One value per business rule it enforces.</summary>
public enum NutritionalCareError
{
    /// <summary>Rule: Active Care Link Required (Subflow 3.1).</summary>
    ActiveCareLinkRequired,

    /// <summary>Rule: Practitioner Only Measures (Subflow 3.1).</summary>
    PractitionerOnly,

    /// <summary>Rule: Habits History And Activity Required (Subflow 3.1).</summary>
    HabitsHistoryAndActivityRequired,

    AssessmentNotFound,

    /// <summary>Rules: Closed Assessment Is Immutable, No Measurement On Closed Assessment (3.1).</summary>
    AssessmentAlreadyClosed,

    /// <summary>Rule: Measurement Protocol Recorded (Subflow 3.1).</summary>
    MeasurementProtocolRequired,

    /// <summary>Rule: Closed Assessment Required (Subflow 3.2).</summary>
    ClosedAssessmentRequired,

    /// <summary>Rule: Clinical Rationale Required (Subflow 3.2).</summary>
    ClinicalRationaleRequired,

    /// <summary>Rule: One Active Diagnosis Per Patient (Subflow 3.2).</summary>
    PatientAlreadyHasActiveDiagnosis,

    DiagnosisNotFound,

    /// <summary>Rule: Active Diagnosis Required (Subflow 3.3).</summary>
    ActiveDiagnosisRequired,

    /// <summary>Rule: Complete Calculation Basis Required (Subflow 3.3).</summary>
    IncompleteCalculationBasis,

    UnsupportedEquation,

    InvalidActivityFactor,

    InvalidDeficitStrategy,

    InvalidReferenceWeight,

    /// <summary>Rule: Previous Proposal Required (Subflow 3.4).</summary>
    PreviousProposalRequired,

    /// <summary>Rule: Override Requires Reason (Subflow 3.4).</summary>
    OverrideReasonRequired,

    PlanNotFound,

    /// <summary>Rule: No Plan Without Diagnosis (Subflow 3.5).</summary>
    PlanRequiresDiagnosis,

    /// <summary>Rule: One Active Version Per Patient (Subflow 3.5).</summary>
    PatientAlreadyHasActivePlanVersion,

    /// <summary>Rule: Calculation Basis Always Recorded (Subflow 3.5).</summary>
    CalculationBasisRequired,

    /// <summary>Rule: Change Reason Required (Subflow 3.6).</summary>
    ChangeReasonRequired,

    /// <summary>Rule: Previous Version Superseded Never Deleted (Subflow 3.6).</summary>
    PlanVersionAlreadySuperseded,

    ReviewItemNotFound,

    /// <summary>Rule: One Open Item Per Patient And Signal Type (Subflow 3.7).</summary>
    ReviewItemAlreadyOpenForSignalType,

    /// <summary>Rule: Resolution States Whether The Plan Was Adjusted (Subflow 3.7).</summary>
    ResolutionOutcomeRequired,

    /// <summary>The plan has not been prescribed or published yet, so the step does not apply.</summary>
    PlanNotInExpectedState,

    /// <summary>Anthropometry is missing, so no equation can run.</summary>
    ClinicalMeasurementRequired,

    /// <summary>NC-1. The patient has no baseline yet; PAC-0 shows it as empty.</summary>
    BaselineNotFound,

    /// <summary>Rule: One Baseline Per Patient (NC-1).</summary>
    BaselineAlreadyRecorded,

    /// <summary>Rule: Plausible Birth Date (NC-1), between 1 and 120 years before today.</summary>
    InvalidBirthDate,

    /// <summary>NC-1. Height outside 50 to 250 cm.</summary>
    InvalidHeight,

    /// <summary>Rule: Closed Medical Condition List (NC-1).</summary>
    UnknownMedicalCondition,

    /// <summary>Only Female and Male are recorded (NC-1).</summary>
    InvalidBiologicalSex,

    /// <summary>Rule: Closed Activity Level List (NC-3).</summary>
    InvalidActivityLevel,

    /// <summary>Rule: Measurement Protocol Recorded (3.1), as the EV-2 checklist: at least one check (NC-3).</summary>
    ProtocolChecklistEmpty,

    /// <summary>Rule: Closed Protocol Checklist (NC-3). A check that is not on the EV-2 list.</summary>
    UnknownProtocolCheck,

    /// <summary>Rule: Plausible Measurement (NC-3). Weight 20-350 kg, waist 40-200 cm, body fat 3-70 %.</summary>
    ImplausibleMeasurement,

    /// <summary>Rule: Plausible Biochemistry (NC-3).</summary>
    ImplausibleBiochemistry,

    /// <summary>NC-3. Meals per day, water or meals out outside their ranges.</summary>
    InvalidEatingHabits,

    /// <summary>Rule: Baseline Required (NC-2/NC-3). A consultation reads age, sex and height from it.</summary>
    BaselineRequired,

    ConsultationNotFound,

    /// <summary>Rule: One Consultation In Progress Per Patient (NC-2).</summary>
    ConsultationAlreadyInProgress,

    /// <summary>The consultation was completed or abandoned; it no longer moves (NC-2).</summary>
    ConsultationNotInProgress,

    /// <summary>Rule: Steps In Order (NC-2). No diagnosis without the measurement, no targets without the diagnosis.</summary>
    ConsultationStepOutOfOrder,

    /// <summary>Rule: Closed Diagnosis Code List (NC-4).</summary>
    UnknownDiagnosisCode,

    /// <summary>Rule: Accepted Suggestion Is Traceable (NC-4). Unknown source, or an accepted AI suggestion without its generation or rationale.</summary>
    InvalidDiagnosisSource,

    /// <summary>Rule: Closed Restriction List (NC-6).</summary>
    UnknownRestriction,

    /// <summary>Rule: Closed Guideline Catalog (NC-6). A code that is not in the catalog.</summary>
    UnknownGuideline,

    /// <summary>NC-6. A custom guideline ("Otra indicación") outside 3 to 140 characters.</summary>
    InvalidCustomGuideline,

    /// <summary>Rule: At Most Five Custom Guidelines Per Version (NC-6).</summary>
    TooManyCustomGuidelines,

    /// <summary>NC-2. The Idempotency-Key header must hold 1 to 64 visible ASCII characters.</summary>
    InvalidIdempotencyKey,

    /// <summary>NC-2. The state filter is not InProgress, Completed or Abandoned.</summary>
    InvalidConsultationState,

    /// <summary>NC-11. The state filter of the review inbox is not Open or Resolved.</summary>
    InvalidReviewItemState,

    /// <summary>Rule: Patient Facing Message (NC-9). The message for the patient has more than 500 characters.</summary>
    InvalidPatientMessage,

    /// <summary>NC-10. The review item has no AI plan proposal (the AI is off, failed, or it was not generated).</summary>
    PlanProposalNotFound,

    /// <summary>NC-10. The proposal was already accepted or dismissed, or its item resolved.</summary>
    PlanProposalAlreadyDecided,

    /// <summary>Rule: Calorie Floor (NC-10). The edited energy is below the floor, or a target is not positive.</summary>
    PlanProposalOutOfSafetyBounds,

    /// <summary>Rule: Proposal Only For A Sustained Deviation (NC-10, DECISIÓN §12-#11).</summary>
    ReviewItemNotSustainedDeviation,

    /// <summary>NC-10. Accepting with edits (asIs false) needs the edited targets and guidelines.</summary>
    PlanProposalEditsRequired,

    UnexpectedError
}
