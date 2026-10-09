namespace Healthify.Platform.ReadModels.Interfaces.REST.Resources;

/// <summary>Who the patient is. Read model Patient Record, identity section.</summary>
/// <param name="UserId">Identifier of the account.</param>
/// <param name="Email">The address the account signs in with.</param>
/// <param name="Role">Patient or Practitioner.</param>
/// <param name="GivenNames">IAM-1. Given names.</param>
/// <param name="FamilyNames">IAM-1. Family names.</param>
/// <param name="FullName">IAM-1. Both together, as the header shows them.</param>
public record RecordIdentityResource(
    int UserId,
    string Email,
    string Role,
    string GivenNames = "",
    string FamilyNames = "",
    string FullName = "")
{
    /// <summary>Identifier of the account.</summary>
    public int UserId { get; init; } = UserId;

    /// <summary>The address the account signs in with.</summary>
    public string Email { get; init; } = Email;

    /// <summary>Patient or Practitioner. Declared at registration and never inferred.</summary>
    public string Role { get; init; } = Role;

    /// <summary>IAM-1. Given names ("Ana").</summary>
    public string GivenNames { get; init; } = GivenNames;

    /// <summary>IAM-1. Family names ("Flores"). Empty on accounts created before IAM-1.</summary>
    public string FamilyNames { get; init; } = FamilyNames;

    /// <summary>IAM-1. "Ana Flores", as the header of the record shows it.</summary>
    public string FullName { get; init; } = FullName;
}

/// <summary>
///     The care link that currently grants access. Read model Patient Record, care section.
/// </summary>
/// <remarks>
///     Consent is on this resource beside the link itself, because a link without live consent grants
///     nothing and a record that showed only the link would be showing access that does not exist.
/// </remarks>
/// <param name="CareLinkId">Identifier of the link.</param>
/// <param name="PractitionerId">The practitioner at the other end.</param>
/// <param name="IsActive">Whether the link is live right now.</param>
/// <param name="HasConsent">Whether consent is live right now.</param>
/// <param name="LastAcknowledgedVersion">Last contract version the patient acknowledged, or null.</param>
public record RecordCareResource(
    int CareLinkId,
    int PractitionerId,
    bool IsActive,
    bool HasConsent,
    int? LastAcknowledgedVersion)
{
    /// <summary>Identifier of the link.</summary>
    public int CareLinkId { get; init; } = CareLinkId;

    /// <summary>The practitioner at the other end of the link.</summary>
    public int PractitionerId { get; init; } = PractitionerId;

    /// <summary>Whether the link is live right now.</summary>
    public bool IsActive { get; init; } = IsActive;

    /// <summary>Whether consent is live right now. Consent is always revocable.</summary>
    public bool HasConsent { get; init; } = HasConsent;

    /// <summary>Last published version the patient acknowledged, or null when none.</summary>
    public int? LastAcknowledgedVersion { get; init; } = LastAcknowledgedVersion;
}

/// <summary>One clinical weight reading. Read models Patient Record and Patient Monitoring Panel.</summary>
/// <param name="Date">The day the measurement was taken.</param>
/// <param name="ValueKg">The reading in kilograms.</param>
/// <param name="Source">Always ClinicalMeasurement.</param>
public record RecordAnthropometryPointResource(DateOnly Date, decimal ValueKg, string Source)
{
    /// <summary>The day the measurement was taken.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>The reading in kilograms, as the practitioner measured it.</summary>
    public decimal ValueKg { get; init; } = ValueKg;

    /// <summary>Always ClinicalMeasurement. The two weight series are never merged.</summary>
    public string Source { get; init; } = Source;
}

/// <summary>One point of the smoothed home series. Never a single day as a headline.</summary>
/// <param name="Date">The day the point belongs to.</param>
/// <param name="SmoothedValueKg">The smoothed value in kilograms.</param>
public record RecordWeightTrendPointResource(DateOnly Date, decimal SmoothedValueKg)
{
    /// <summary>The day the point belongs to.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>The smoothed value in kilograms. Only protocol-compliant readings shape it.</summary>
    public decimal SmoothedValueKg { get; init; } = SmoothedValueKg;
}

/// <summary>One evaluated day. Read models Patient Record and Patient Monitoring Panel.</summary>
/// <param name="Date">The calendar day the patient was living.</param>
/// <param name="Outcome">Met, Exceeded, Short or Unlogged.</param>
public record RecordDailyComplianceResource(DateOnly Date, string Outcome)
{
    /// <summary>The calendar day the patient was living, never the server day.</summary>
    public DateOnly Date { get; init; } = Date;

    /// <summary>
    ///     Met, Exceeded, Short or Unlogged. An unlogged day sits beside the other three rather than
    ///     below them: nobody wrote that day down, which is not evidence of anything.
    /// </summary>
    public string Outcome { get; init; } = Outcome;
}

/// <summary>
///     Where the consistency index stands. Read models Patient Record and Patient Monitoring Panel.
/// </summary>
/// <remarks>
///     Both dates are on the resource so that the order is readable: the patient is shown the index
///     first, always, and only after three weeks in alert does a practitioner hear about it.
/// </remarks>
/// <param name="State">Normal, Watch or Alert.</param>
/// <param name="ShownToPatientAt">When the patient was asked. Null when they have not been.</param>
/// <param name="EscalatedAt">When the practitioner was told. Null when they have not been.</param>
public record RecordConsistencyResource(
    string State,
    DateTimeOffset? ShownToPatientAt,
    DateTimeOffset? EscalatedAt)
{
    /// <summary>Normal, Watch or Alert. There is no grade and no penalty here.</summary>
    public string State { get; init; } = State;

    /// <summary>When the patient was asked about it. Null when they have not been.</summary>
    public DateTimeOffset? ShownToPatientAt { get; init; } = ShownToPatientAt;

    /// <summary>When the practitioner was told. Never before the patient was asked.</summary>
    public DateTimeOffset? EscalatedAt { get; init; } = EscalatedAt;
}

/// <summary>One referral. Read model Patient Record, referrals section.</summary>
/// <param name="ReferralId">Identifier of the referral.</param>
/// <param name="Specialty">Where the patient was sent.</param>
/// <param name="Reason">Why, in the words of the practitioner.</param>
/// <param name="IssuedBy">The practitioner who issued it.</param>
/// <param name="IssuedAt">When it was issued.</param>
/// <param name="Status">RM-4. Open ("en curso") or Closed.</param>
/// <param name="ClosedAt">RM-4. When it was closed, or null.</param>
public record RecordReferralResource(
    int ReferralId,
    string Specialty,
    string Reason,
    int IssuedBy,
    DateTimeOffset IssuedAt,
    string Status = "Open",
    DateTimeOffset? ClosedAt = null)
{
    /// <summary>Identifier of the referral.</summary>
    public int ReferralId { get; init; } = ReferralId;

    /// <summary>Where the patient was sent.</summary>
    public string Specialty { get; init; } = Specialty;

    /// <summary>Why, in the words of the practitioner.</summary>
    public string Reason { get; init; } = Reason;

    /// <summary>The practitioner who issued it.</summary>
    public int IssuedBy { get; init; } = IssuedBy;

    /// <summary>When it was issued. A referral is a record of a decision, not a workflow.</summary>
    public DateTimeOffset IssuedAt { get; init; } = IssuedAt;

    /// <summary>
    ///     RM-4 (DECISIÓN §12-#8). Open ("Derivación a Endocrinología · en curso") or Closed. PT20 "Sin
    ///     derivaciones activas" means none is Open.
    /// </summary>
    public string Status { get; init; } = Status;

    /// <summary>RM-4. When the practitioner closed it, or null while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; init; } = ClosedAt;
}

/// <summary>
///     The published contract in force. Read models Patient Record and Patient Monitoring Panel.
/// </summary>
/// <remarks>
///     Targets, guidelines and restrictions, and nothing else. There is no diagnosis on this resource
///     and no calculation basis, because neither leaves Nutritional Care and neither is on any
///     contract a composer can reach.
/// </remarks>
/// <param name="PlanVersion">Version of the published contract.</param>
/// <param name="ValidFrom">When this version came into force.</param>
/// <param name="EnergyKcal">Daily energy target in kilocalories.</param>
/// <param name="ProteinG">Daily protein target in grams.</param>
/// <param name="CarbG">Daily carbohydrate target in grams.</param>
/// <param name="FatG">Daily fat target in grams.</param>
/// <param name="Guidelines">What the practitioner wrote: a catalog code or a custom text each.</param>
/// <param name="Restrictions">What the practitioner ruled out, as codes since NC-6.</param>
/// <param name="GuidelineItems">NC-6. The guidelines, telling catalog codes from custom texts.</param>
/// <param name="LegacyRestrictions">NC-6. Free text restrictions from before the closed list.</param>
public record RecordActiveTargetsResource(
    int PlanVersion,
    DateTimeOffset ValidFrom,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> Guidelines,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<RecordGuidelineResource>? GuidelineItems = null,
    IReadOnlyList<string>? LegacyRestrictions = null)
{
    /// <summary>Version of the published contract.</summary>
    public int PlanVersion { get; init; } = PlanVersion;

    /// <summary>When this version came into force.</summary>
    public DateTimeOffset ValidFrom { get; init; } = ValidFrom;

    /// <summary>Daily energy target in kilocalories.</summary>
    public decimal EnergyKcal { get; init; } = EnergyKcal;

    /// <summary>Daily protein target in grams.</summary>
    public decimal ProteinG { get; init; } = ProteinG;

    /// <summary>Daily carbohydrate target in grams.</summary>
    public decimal CarbG { get; init; } = CarbG;

    /// <summary>Daily fat target in grams.</summary>
    public decimal FatG { get; init; } = FatG;

    /// <summary>
    ///     A catalog code or a custom text each (see <see cref="GuidelineItems" />). There is no menu generation
    ///     and no equivalence table on this platform.
    /// </summary>
    public IReadOnlyList<string> Guidelines { get; init; } = Guidelines;

    /// <summary>DietaryRestriction codes since NC-6.</summary>
    public IReadOnlyList<string> Restrictions { get; init; } = Restrictions;

    /// <summary>NC-6. The guidelines, each a catalog code (translate it) or a custom text (show it as written).</summary>
    public IReadOnlyList<RecordGuidelineResource> GuidelineItems { get; init; } = GuidelineItems ?? [];

    /// <summary>NC-6. Free text restrictions written before the closed list, which match no code (PT4, PAC-4).</summary>
    public IReadOnlyList<string> LegacyRestrictions { get; init; } = LegacyRestrictions ?? [];
}

/// <summary>NC-6. One guideline of the plan: a catalog code or a custom text, never both.</summary>
/// <param name="Code">Catalog code, or null for a custom guideline.</param>
/// <param name="Custom">Written by the practitioner. Clinical data: never translated.</param>
public record RecordGuidelineResource(string? Code, string? Custom)
{
    /// <summary>Catalog code, or null for a custom guideline.</summary>
    public string? Code { get; init; } = Code;

    /// <summary>Written by the practitioner. Clinical data: never translated.</summary>
    public string? Custom { get; init; } = Custom;
}

/// <summary>Phase one of the record: the clinical weight series.</summary>
/// <param name="ClinicalAnthropometrySeries">Weight taken by the practitioner, oldest first.</param>
public record RecordAssessmentSectionResource(
    IReadOnlyList<RecordAnthropometryPointResource> ClinicalAnthropometrySeries)
{
    /// <summary>
    ///     Weight taken by the practitioner, oldest first. The readings the patient takes at home are
    ///     a separate series and appear in the follow-up section as a trend.
    /// </summary>
    public IReadOnlyList<RecordAnthropometryPointResource> ClinicalAnthropometrySeries { get; init; } =
        ClinicalAnthropometrySeries;
}

/// <summary>Phase four of the record: how it has been going.</summary>
/// <param name="DailyCompliance">Day by day, oldest first.</param>
/// <param name="SelfWeighInTrend">The smoothed home series.</param>
/// <param name="Consistency">Where the consistency index stands, or null.</param>
/// <param name="Referrals">Where the patient was sent, most recent first.</param>
public record RecordFollowUpSectionResource(
    IReadOnlyList<RecordDailyComplianceResource> DailyCompliance,
    IReadOnlyList<RecordWeightTrendPointResource> SelfWeighInTrend,
    RecordConsistencyResource? Consistency,
    IReadOnlyList<RecordReferralResource> Referrals)
{
    /// <summary>Day by day, oldest first, unlogged days included as such.</summary>
    public IReadOnlyList<RecordDailyComplianceResource> DailyCompliance { get; init; } = DailyCompliance;

    /// <summary>The smoothed home series. Never a single day as a headline.</summary>
    public IReadOnlyList<RecordWeightTrendPointResource> SelfWeighInTrend { get; init; } = SelfWeighInTrend;

    /// <summary>Where the consistency index stands, or null when there is none.</summary>
    public RecordConsistencyResource? Consistency { get; init; } = Consistency;

    /// <summary>Where the patient was sent, most recent first.</summary>
    public IReadOnlyList<RecordReferralResource> Referrals { get; init; } = Referrals;
}

/// <summary>
///     The unified record of one patient. Read model Patient Record.
/// </summary>
/// <remarks>
///     A composite read model, not a bounded context. It is organised in the phases of the process
///     the expert dictated, and the diagnosis phase is deliberately absent: a nutritional diagnosis
///     and the calculation basis behind a target never leave Nutritional Care, so nothing that
///     composes across contexts can show them.
/// </remarks>
/// <param name="PatientId">Whose record.</param>
/// <param name="Identity">Who they are, or null when the account cannot be read.</param>
/// <param name="Care">The care link that currently grants access, or null.</param>
/// <param name="Assessment">Phase one.</param>
/// <param name="Intervention">Phase three, or null when nothing is published.</param>
/// <param name="FollowUp">Phase four.</param>
public record PatientRecordResource(
    int PatientId,
    RecordIdentityResource? Identity,
    RecordCareResource? Care,
    RecordAssessmentSectionResource Assessment,
    RecordActiveTargetsResource? Intervention,
    RecordFollowUpSectionResource FollowUp)
{
    /// <summary>Whose record.</summary>
    public int PatientId { get; init; } = PatientId;

    /// <summary>Who they are, or null when the account cannot be read.</summary>
    public RecordIdentityResource? Identity { get; init; } = Identity;

    /// <summary>The care link that currently grants access, or null when there is none.</summary>
    public RecordCareResource? Care { get; init; } = Care;

    /// <summary>Phase one: assessment. The clinical weight series.</summary>
    public RecordAssessmentSectionResource Assessment { get; init; } = Assessment;

    /// <summary>Phase three: intervention. The published contract, or null.</summary>
    public RecordActiveTargetsResource? Intervention { get; init; } = Intervention;

    /// <summary>Phase four: follow-up.</summary>
    public RecordFollowUpSectionResource FollowUp { get; init; } = FollowUp;
}
