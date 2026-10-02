using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

/// <summary>
///     Clinical phase 1: habits, history, physical activity, anthropometry and biochemistry, all
///     recorded inside the consultation by one actor.
/// </summary>
/// <remarks>
///     Once closed it is immutable. A correction does not edit it; it creates a new assessment that
///     references this one, so the record of what was believed at the time survives.
///     NC-3 adds the structured form of EV-2 next to the original free text, which stays for the rows
///     and clients that already use it.
/// </remarks>
public partial class NutritionalAssessment
{
    private readonly List<ClinicalMeasurement> _measurements = [];
    private List<string>? _conditionsSnapshot;

    /// <summary>Required by EF Core.</summary>
    protected NutritionalAssessment()
    {
    }

    public NutritionalAssessment(RecordAssessmentCommand command) : this(command, null)
    {
    }

    /// <summary>
    ///     Stand-alone endpoint (Subflow 3.1). <paramref name="conditionsSnapshot" /> is the baseline's
    ///     medical history when the patient has one, or null.
    /// </summary>
    public NutritionalAssessment(RecordAssessmentCommand command,
        IReadOnlyList<MedicalCondition>? conditionsSnapshot)
    {
        // Business rule: Habits History And Activity Required (Nutritional Care, Subflow 3.1)
        // An assessment missing any of the three is not an assessment, it is a note.
        if (string.IsNullOrWhiteSpace(command.Habits))
            throw new ArgumentException("Habits are required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.MedicalHistory))
            throw new ArgumentException("Medical history is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.PhysicalActivity))
            throw new ArgumentException("Physical activity is required.", nameof(command));
        if (command.AgeYears is < 1 or > 120)
            throw new ArgumentException("The age must be between 1 and 120 years.", nameof(command));

        PatientId = command.PatientId;
        PractitionerId = command.PractitionerId;
        Habits = command.Habits.Trim();
        MedicalHistory = command.MedicalHistory.Trim();
        PhysicalActivity = command.PhysicalActivity.Trim();
        Biochemistry = string.IsNullOrWhiteSpace(command.Biochemistry) ? null : command.Biochemistry.Trim();
        AgeYears = command.AgeYears;
        BiologicalSex = new BiologicalSex(command.BiologicalSex);

        // NC-3: the structured values are optional on this endpoint.
        ActivityLevel = command.ActivityLevelCode is null ? null : new ActivityLevel(command.ActivityLevelCode);
        AssignHabits(command.EatingHabitsData is null
            ? null
            : EatingHabits.From(command.EatingHabitsData.MealsPerDay, command.EatingHabitsData.WaterLitersPerDay,
                command.EatingHabitsData.MealsOutPerWeek));
        AssignBiochemistry(command.BiochemistryData is null
            ? null
            : BiochemistryPanel.From(command.BiochemistryData.FastingGlucoseMgDl,
                command.BiochemistryData.TotalCholesterolMgDl, command.BiochemistryData.TriglyceridesMgDl));
        _conditionsSnapshot = conditionsSnapshot?.Select(c => c.Value).ToList();

        // Business rule: Correction Creates A New Assessment (Nutritional Care, Subflow 3.1)
        // A corrected assessment points back at the one it replaces instead of overwriting it.
        SupersedesAssessmentId = command.SupersedesAssessmentId;
        ClosedAt = null;
    }

    public AssessmentId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>Cross-context reference to the practitioner who recorded it.</summary>
    public int PractitionerId { get; private set; }

    /// <summary>Free text habits. Null on assessments recorded through the guided consultation (NC-3).</summary>
    public string? Habits { get; private set; }

    /// <summary>
    ///     Free text medical history. Null on assessments recorded through the guided consultation, which
    ///     keep <see cref="ConditionsSnapshot" /> instead (NC-3).
    /// </summary>
    public string? MedicalHistory { get; private set; }

    /// <summary>Free text physical activity. Null when only <see cref="ActivityLevel" /> was recorded (NC-3).</summary>
    public string? PhysicalActivity { get; private set; }

    public string? Biochemistry { get; private set; }

    /// <summary>NC-3. Physical activity from the closed list.</summary>
    public ActivityLevel? ActivityLevel { get; private set; }

    /// <summary>NOTE: technical field. Persisted part of <see cref="EatingHabits" />.</summary>
    public int? MealsPerDay { get; private set; }

    /// <inheritdoc cref="MealsPerDay" />
    public decimal? WaterLitersPerDay { get; private set; }

    /// <inheritdoc cref="MealsPerDay" />
    public int? MealsOutPerWeek { get; private set; }

    /// <summary>NC-3. Optional eating habits, rebuilt from their columns.</summary>
    public EatingHabits? EatingHabits => EatingHabits.From(MealsPerDay, WaterLitersPerDay, MealsOutPerWeek);

    /// <summary>NOTE: technical field. Persisted part of <see cref="BiochemistryPanel" />.</summary>
    public decimal? FastingGlucoseMgDl { get; private set; }

    /// <inheritdoc cref="FastingGlucoseMgDl" />
    public decimal? TotalCholesterolMgDl { get; private set; }

    /// <inheritdoc cref="FastingGlucoseMgDl" />
    public decimal? TriglyceridesMgDl { get; private set; }

    /// <summary>NC-3. Optional biochemistry, rebuilt from its columns.</summary>
    public BiochemistryPanel? BiochemistryPanel =>
        BiochemistryPanel.From(FastingGlucoseMgDl, TotalCholesterolMgDl, TriglyceridesMgDl);

    /// <summary>
    ///     NC-3. The baseline medical history as it was when this assessment was recorded. Null when the
    ///     assessment predates the baseline; empty when the baseline listed none.
    /// </summary>
    public IReadOnlyList<string>? ConditionsSnapshot => _conditionsSnapshot;

    /// <summary>NC-3. The guided consultation this assessment belongs to, if any. Same context, plain int.</summary>
    public int? ConsultationId { get; private set; }

    /// <summary>
    ///     NOTE: age and biological sex are not listed in the aggregate contents of the inventory,
    ///     but three of the four mandated equations are undefined without them. They are facts
    ///     recorded during the assessment, not clinical choices, so they live here rather than
    ///     arriving with the calculation command. In the guided consultation they are a snapshot of the
    ///     baseline on the day of the consultation (NC-3).
    /// </summary>
    public int AgeYears { get; private set; }

    /// <inheritdoc cref="AgeYears" />
    public BiologicalSex BiologicalSex { get; private set; } = null!;

    /// <summary>The assessment this one corrects, if any. Business rule: Correction Creates A New Assessment.</summary>
    public int? SupersedesAssessmentId { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The Anthropometry of this assessment, in the order it was taken.</summary>
    public IReadOnlyCollection<ClinicalMeasurement> Measurements => _measurements;

    /// <summary>Business rule: Closed Assessment Is Immutable (Subflow 3.1).</summary>
    public bool IsClosed => ClosedAt is not null;

    /// <summary>
    ///     NC-3 - Step 1 of the guided consultation. Age, biological sex and medical history are taken from
    ///     the baseline as a snapshot. DECISIÓN §12-#13: editing the baseline afterwards never rewrites
    ///     this assessment.
    /// </summary>
    public static NutritionalAssessment ForConsultation(
        int consultationId,
        int practitionerId,
        PatientBaseline baseline,
        DateOnly consultationDate,
        ActivityLevel activityLevel,
        EatingHabits? eatingHabits,
        BiochemistryPanel? biochemistry,
        int? supersedesAssessmentId)
    {
        var ageYears = baseline.AgeAt(consultationDate);
        if (ageYears is < 1 or > 120)
            throw new ArgumentException("The age must be between 1 and 120 years.", nameof(baseline));

        var assessment = new NutritionalAssessment
        {
            PatientId = baseline.PatientId,
            PractitionerId = practitionerId,
            ConsultationId = consultationId,
            ActivityLevel = activityLevel,
            AgeYears = ageYears,
            BiologicalSex = baseline.BiologicalSex,
            _conditionsSnapshot = baseline.Conditions.Select(c => c.Value).ToList(),
            // Business rule: Correction Creates A New Assessment (Subflow 3.1). Repeating step 1 of a
            // consultation never edits the closed assessment; it replaces it with a new one.
            SupersedesAssessmentId = supersedesAssessmentId,
            ClosedAt = null
        };
        assessment.AssignHabits(eatingHabits);
        assessment.AssignBiochemistry(biochemistry);
        return assessment;
    }

    /// <summary>Subflow 3.1 - Take Clinical Measurement.</summary>
    public ClinicalMeasurement TakeClinicalMeasurement(TakeClinicalMeasurementCommand command)
    {
        // Business rule: No Measurement On Closed Assessment (Nutritional Care, Subflow 3.1)
        // Business rule: Closed Assessment Is Immutable (Nutritional Care, Subflow 3.1)
        if (IsClosed)
            throw new InvalidOperationException("A closed assessment cannot receive new measurements.");

        // Business rule: Measurement Protocol Recorded (Nutritional Care, Subflow 3.1)
        // The value object refuses an empty protocol, so an unprotocolled measurement cannot exist.
        var measurement = new ClinicalMeasurement(
            command.WeightKg,
            command.HeightCm,
            new MeasurementProtocol(command.Protocol),
            command.BodyFatPercentage,
            command.WaistCircumferenceCm,
            command.ProtocolChecks is null ? null : new MeasurementProtocolChecklist(command.ProtocolChecks));

        _measurements.Add(measurement);
        return measurement;
    }

    /// <summary>NC-3 - The measurement of step 1, with the baseline height and the EV-2 checklist.</summary>
    public ClinicalMeasurement TakeStructuredMeasurement(
        decimal weightKg,
        HeightCm height,
        MeasurementProtocolChecklist protocolChecks,
        decimal? bodyFatPercentage,
        decimal? waistCm)
    {
        // Business rule: Closed Assessment Is Immutable (Nutritional Care, Subflow 3.1)
        if (IsClosed)
            throw new InvalidOperationException("A closed assessment cannot receive new measurements.");

        var measurement = new ClinicalMeasurement(weightKg, height, protocolChecks, bodyFatPercentage, waistCm);

        _measurements.Add(measurement);
        return measurement;
    }

    /// <summary>Subflow 3.1 - Close Assessment.</summary>
    public void Close()
    {
        // Business rule: Closed Assessment Is Immutable (Nutritional Care, Subflow 3.1)
        if (IsClosed) throw new InvalidOperationException("This assessment has already been closed.");

        ClosedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The most recent anthropometry, which is what a calculation runs on.</summary>
    public ClinicalMeasurement? LatestMeasurement =>
        _measurements.OrderByDescending(m => m.TakenAt).FirstOrDefault();

    private void AssignHabits(EatingHabits? habits)
    {
        MealsPerDay = habits?.MealsPerDay;
        WaterLitersPerDay = habits?.WaterLitersPerDay;
        MealsOutPerWeek = habits?.MealsOutPerWeek;
    }

    private void AssignBiochemistry(BiochemistryPanel? panel)
    {
        FastingGlucoseMgDl = panel?.FastingGlucoseMgDl;
        TotalCholesterolMgDl = panel?.TotalCholesterolMgDl;
        TriglyceridesMgDl = panel?.TriglyceridesMgDl;
    }
}
