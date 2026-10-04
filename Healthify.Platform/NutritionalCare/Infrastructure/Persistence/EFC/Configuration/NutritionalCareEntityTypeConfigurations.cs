using System.Text.Json;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;

public class NutritionalAssessmentEntityTypeConfiguration : IEntityTypeConfiguration<NutritionalAssessment>
{
    /// <summary>Same JSON pattern as the plan guidelines, for a list that may be absent (null).</summary>
    private static readonly ValueComparer<List<string>?> NullableStringListComparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        c => c == null ? 0 : c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c == null ? null : c.ToList());

    private static readonly ValueConverter<List<string>?, string?> NullableStringListConverter = new(
        v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v)
            ? null
            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<NutritionalAssessment> builder)
    {
        builder.ToTable("nutritional_assessments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => AssessmentId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context references: plain ints, no navigation, no foreign key constraint.
        builder.Property(a => a.PatientId).HasColumnName("patient_id").IsRequired();
        builder.Property(a => a.PractitionerId).HasColumnName("practitioner_id").IsRequired();
        builder.HasIndex(a => a.PatientId).HasDatabaseName("ix_nutritional_assessments_patient_id");

        // Free text of the original assessment. Nullable since NC-3: the guided consultation records the
        // structured columns below instead, and the historic text of older rows is kept as it was.
        builder.Property(a => a.Habits).HasColumnName("habits").HasMaxLength(4000);
        builder.Property(a => a.MedicalHistory).HasColumnName("medical_history").HasMaxLength(4000);
        builder.Property(a => a.PhysicalActivity).HasColumnName("physical_activity").HasMaxLength(2000);
        builder.Property(a => a.Biochemistry).HasColumnName("biochemistry").HasMaxLength(4000);

        // NC-3: structured assessment of EV-2.
        builder.Property(a => a.ActivityLevel)
            .HasConversion(new ValueConverter<ActivityLevel?, string?>(
                l => l == null ? null : l.Value,
                v => v == null ? null : new ActivityLevel(v)))
            .HasColumnName("activity_level").HasMaxLength(15);
        builder.Property(a => a.MealsPerDay).HasColumnName("meals_per_day");
        builder.Property(a => a.WaterLitersPerDay).HasColumnName("water_liters_per_day")
            .HasColumnType("decimal(4,2)");
        builder.Property(a => a.MealsOutPerWeek).HasColumnName("meals_out_per_week");
        builder.Property(a => a.FastingGlucoseMgDl).HasColumnName("glucose_mg_dl").HasColumnType("decimal(6,1)");
        builder.Property(a => a.TotalCholesterolMgDl).HasColumnName("total_cholesterol_mg_dl")
            .HasColumnType("decimal(6,1)");
        builder.Property(a => a.TriglyceridesMgDl).HasColumnName("triglycerides_mg_dl")
            .HasColumnType("decimal(6,1)");
        builder.Property<List<string>?>("_conditionsSnapshot")
            .HasConversion(NullableStringListConverter, NullableStringListComparer)
            .HasColumnName("conditions_snapshot").HasColumnType("json");

        // Logical reference inside this context: plain int, no navigation, no constraint.
        builder.Property(a => a.ConsultationId).HasColumnName("consultation_id");
        builder.HasIndex(a => a.ConsultationId).HasDatabaseName("ix_nutritional_assessments_consultation_id");

        builder.Property(a => a.AgeYears).HasColumnName("age_years").IsRequired();
        builder.Property(a => a.BiologicalSex)
            .HasConversion(sex => sex.Value, value => new BiologicalSex(value))
            .HasColumnName("biological_sex").HasMaxLength(10).IsRequired();

        builder.Property(a => a.SupersedesAssessmentId).HasColumnName("supersedes_assessment_id");
        builder.Property(a => a.ClosedAt).HasColumnName("closed_at");

        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        // Inside the aggregate: EF navigation with cascade delete.
        builder.HasMany(a => a.Measurements)
            .WithOne()
            .HasForeignKey(m => m.AssessmentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Measurements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(a => a.IsClosed);
        builder.Ignore(a => a.LatestMeasurement);
        builder.Ignore(a => a.EatingHabits);
        builder.Ignore(a => a.BiochemistryPanel);
        builder.Ignore(a => a.ConditionsSnapshot);
    }
}

public class ClinicalMeasurementEntityTypeConfiguration : IEntityTypeConfiguration<ClinicalMeasurement>
{
    /// <summary>Same JSON pattern as the plan guidelines, for a list that may be absent (null).</summary>
    private static readonly ValueComparer<List<string>?> NullableStringListComparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        c => c == null ? 0 : c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c == null ? null : c.ToList());

    private static readonly ValueConverter<List<string>?, string?> NullableStringListConverter = new(
        v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v)
            ? null
            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<ClinicalMeasurement> builder)
    {
        builder.ToTable("clinical_measurements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(m => m.AssessmentId)
            .HasColumnName("assessment_id")
            .HasConversion(id => id.Value, value => AssessmentId.FromRaw(value))
            .IsRequired();
        builder.HasIndex(m => m.AssessmentId)
            .HasDatabaseName("ix_clinical_measurements_assessment_id");

        builder.Property(m => m.WeightKg).HasColumnName("weight_kg").HasColumnType("decimal(10,2)")
            .IsRequired();
        builder.Property(m => m.HeightCm).HasColumnName("height_cm").HasColumnType("decimal(10,2)")
            .IsRequired();
        builder.Property(m => m.BodyFatPercentage).HasColumnName("body_fat_percentage")
            .HasColumnType("decimal(10,2)");
        builder.Property(m => m.WaistCircumferenceCm).HasColumnName("waist_circumference_cm")
            .HasColumnType("decimal(10,2)");

        builder.Property(m => m.Protocol)
            .HasConversion(protocol => protocol.Value, value => new MeasurementProtocol(value))
            .HasColumnName("protocol").HasMaxLength(300).IsRequired();

        // NC-3: the EV-2 checklist (null on measurements that only have the free text protocol) and the
        // body mass index, stored so the historic value never changes. decimal(5,1) rather than (4,1):
        // the accepted weight and height ranges allow values above 999.9 in theory.
        builder.Property<List<string>?>("_protocolChecks")
            .HasConversion(NullableStringListConverter, NullableStringListComparer)
            .HasColumnName("protocol_checks").HasColumnType("json");
        builder.Property(m => m.BmiKgM2).HasColumnName("bmi").HasColumnType("decimal(5,1)").IsRequired();
        builder.Property(m => m.BmiCategory).HasColumnName("bmi_category").HasMaxLength(20).IsRequired();

        builder.Property(m => m.TakenAt).HasColumnName("taken_at").IsRequired();

        builder.Ignore(m => m.ProtocolChecks);
    }
}

public class ConsultationEntityTypeConfiguration : IEntityTypeConfiguration<Consultation>
{
    /// <summary>NC-2. The EV-5 draft as one JSON object, or null while none was saved.</summary>
    private static readonly ValueConverter<PublicationDraft?, string?> PublicationDraftConverter = new(
        d => d == null
            ? null
            : JsonSerializer.Serialize(
                new StoredPublicationDraft(d.Restrictions, d.Guidelines, d.CustomGuidelines, d.PatientMessage),
                (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v) ? null : ReadPublicationDraft(v));

    private static readonly ValueComparer<PublicationDraft?> PublicationDraftComparer = new(
        (a, b) => a == null ? b == null : a.Equals(b),
        d => d == null ? 0 : d.GetHashCode(),
        d => d == null
            ? null
            : new PublicationDraft(d.Restrictions, d.Guidelines, d.CustomGuidelines, d.PatientMessage));

    public void Configure(EntityTypeBuilder<Consultation> builder)
    {
        builder.ToTable("consultations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ConsultationId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context references: plain ints, no navigation, no foreign key constraint.
        builder.Property(c => c.PatientId).HasColumnName("patient_id").IsRequired();
        builder.Property(c => c.PractitionerId).HasColumnName("practitioner_id").IsRequired();
        builder.Property(c => c.ScheduledFollowUpId).HasColumnName("scheduled_follow_up_id");

        builder.Property(c => c.CurrentStep)
            .HasConversion(s => s.Value, value => new ConsultationStep(value))
            .HasColumnName("current_step").HasMaxLength(20).IsRequired();
        builder.Property(c => c.State)
            .HasConversion(s => s.Value, value => new ConsultationState(value))
            .HasColumnName("state").HasMaxLength(20).IsRequired();
        builder.HasIndex(c => new { c.PatientId, c.State }).HasDatabaseName("ix_consultations_patient_id_state");

        // Business rule: One Consultation In Progress Per Patient (NC-2). MySQL has no partial indexes,
        // so a stored generated column holds the patient only while the consultation is in progress;
        // a unique index on it lets any number of NULLs through and rejects a second in-progress row.
        builder.Property<int?>("InProgressPatientId")
            .HasColumnName("in_progress_patient_id")
            .HasComputedColumnSql("IF(`state` = 'InProgress', `patient_id`, NULL)", true);
        builder.HasIndex("InProgressPatientId").IsUnique()
            .HasDatabaseName("ix_consultations_in_progress_patient_id");

        // Logical references to this context's aggregates, saved step by step.
        builder.Property(c => c.AssessmentId).HasColumnName("assessment_id");
        builder.Property(c => c.DiagnosisId).HasColumnName("diagnosis_id");
        builder.Property(c => c.PlanId).HasColumnName("plan_id");

        builder.Property(c => c.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(c => c.LastSavedAt).HasColumnName("last_saved_at").IsRequired();
        builder.Property(c => c.CompletedAt).HasColumnName("completed_at");
        builder.Property(c => c.PublishedPlanVersion).HasColumnName("published_plan_version");
        builder.Property(c => c.IsFirstConsultation).HasColumnName("is_first_consultation").IsRequired();

        // NC-2. Idempotency of the publication, the EV-5 draft and discarding.
        builder.Property(c => c.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64);
        builder.Property(c => c.PublicationDraft)
            .HasConversion(PublicationDraftConverter, PublicationDraftComparer)
            .HasColumnName("publication_draft").HasColumnType("json");
        builder.Property(c => c.AbandonedAt).HasColumnName("abandoned_at");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(c => c.IsInProgress);
    }

    private static PublicationDraft? ReadPublicationDraft(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredPublicationDraft>(json, (JsonSerializerOptions?)null);
        return stored is null
            ? null
            : new PublicationDraft(stored.Restrictions, stored.Guidelines, stored.CustomGuidelines,
                stored.PatientMessage);
    }

    /// <summary>
    ///     The shape of <c>publication_draft</c>; the value object rebuilds itself from it. NC-9 adds
    ///     <c>PatientMessage</c> to the JSON (no column change): drafts saved before it read as null.
    /// </summary>
    private sealed record StoredPublicationDraft(
        IReadOnlyList<string>? Restrictions,
        IReadOnlyList<string>? Guidelines,
        IReadOnlyList<string>? CustomGuidelines,
        string? PatientMessage = null);
}

public class PatientBaselineEntityTypeConfiguration : IEntityTypeConfiguration<PatientBaseline>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<PatientBaseline> builder)
    {
        builder.ToTable("patient_baselines");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => PatientBaselineId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context reference: plain int, no navigation. Unique: Business rule One Baseline Per
        // Patient (NC-1), enforced by the database as well as by the command service.
        builder.Property(b => b.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(b => b.PatientId).IsUnique().HasDatabaseName("ix_patient_baselines_patient_id");

        // MySql.EntityFrameworkCore materializes a DATE column as DateTime and cannot cast it to DateOnly,
        // so the conversion is explicit. The column stays a plain DATE.
        builder.Property(b => b.BirthDate)
            .HasConversion(d => d.ToDateTime(TimeOnly.MinValue), v => DateOnly.FromDateTime(v))
            .HasColumnName("birth_date").HasColumnType("date").IsRequired();
        builder.Property(b => b.BirthDateEstimated).HasColumnName("birth_date_estimated").IsRequired();
        builder.Property(b => b.BiologicalSex)
            .HasConversion(sex => sex.Value, value => new BiologicalSex(value))
            .HasColumnName("biological_sex").HasMaxLength(10).IsRequired();
        builder.Property(b => b.Height)
            .HasConversion(h => h.Value, value => new HeightCm(value))
            .HasColumnName("height_cm").HasColumnType("decimal(5,1)").IsRequired();

        // Condition codes from the closed list, stored as JSON through the backing field with the same
        // pattern as the plan guidelines. The value comparer is what lets EF notice an edit.
        builder.Property<List<string>>("_conditions")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
                      ?? new List<string>(),
                StringListComparer)
            .HasColumnName("conditions").HasColumnType("json").IsRequired();

        builder.Property(b => b.RecordedBy).HasColumnName("recorded_by").IsRequired();

        builder.Property(b => b.CreatedAt).HasColumnName("created_at");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(b => b.Conditions);
    }
}

public class NutritionalDiagnosisEntityTypeConfiguration : IEntityTypeConfiguration<NutritionalDiagnosis>
{
    public void Configure(EntityTypeBuilder<NutritionalDiagnosis> builder)
    {
        builder.ToTable("nutritional_diagnoses");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => DiagnosisId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(d => d.PatientId).HasColumnName("patient_id").IsRequired();
        builder.Property(d => d.PractitionerId).HasColumnName("practitioner_id").IsRequired();
        builder.Property(d => d.AssessmentId).HasColumnName("assessment_id").IsRequired();
        builder.HasIndex(d => d.PatientId).HasDatabaseName("ix_nutritional_diagnoses_patient_id");

        builder.Property(d => d.Statement).HasColumnName("statement").HasMaxLength(1000).IsRequired();
        builder.Property(d => d.Rationale)
            .HasConversion(r => r.Value, value => new ClinicalRationale(value))
            .HasColumnName("rationale").HasMaxLength(2000).IsRequired(false);

        // NC-4. Null on historic free text diagnoses.
        builder.Property(d => d.Code)
            .HasConversion(new ValueConverter<DiagnosisCode?, string?>(
                c => c == null ? null : c.Value,
                v => v == null ? null : new DiagnosisCode(v)))
            .HasColumnName("code").HasMaxLength(30);
        builder.Property(d => d.Source)
            .HasConversion(new ValueConverter<DiagnosisSource?, string?>(
                s => s == null ? null : s.Value,
                v => v == null ? null : new DiagnosisSource(v)))
            .HasColumnName("source").HasMaxLength(30);
        builder.Property(d => d.AiGenerationId).HasColumnName("ai_generation_id");
        builder.Property(d => d.BmiAtIssue).HasColumnName("bmi_at_issue").HasColumnType("decimal(4,1)");

        builder.Property(d => d.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(d => d.SupersededAt).HasColumnName("superseded_at");

        // NC-7. A diagnosis of a consultation waits here until the consultation publishes its plan. Logical
        // reference to this context's Consultation, no navigation.
        builder.Property(d => d.PendingConsultationId).HasColumnName("pending_consultation_id");
        builder.HasIndex(d => d.PendingConsultationId)
            .HasDatabaseName("ix_nutritional_diagnoses_pending_consultation_id");
        builder.Property(d => d.DiscardedAt).HasColumnName("discarded_at");

        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(d => d.IsActive);
        builder.Ignore(d => d.IsPending);
        builder.Ignore(d => d.IsDiscarded);
    }
}

public class ReviewItemEntityTypeConfiguration : IEntityTypeConfiguration<ReviewItem>
{
    public void Configure(EntityTypeBuilder<ReviewItem> builder)
    {
        builder.ToTable("review_items");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ReviewItemId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(r => r.PatientId).HasColumnName("patient_id").IsRequired();
        builder.Property(r => r.PractitionerId).HasColumnName("practitioner_id").IsRequired();
        builder.HasIndex(r => r.PractitionerId).HasDatabaseName("ix_review_items_practitioner_id");
        builder.HasIndex(r => r.PatientId).HasDatabaseName("ix_review_items_patient_id");

        builder.Property(r => r.SignalType)
            .HasConversion(s => s.Value, value => new SignalType(value))
            .HasColumnName("signal_type").HasMaxLength(40).IsRequired();

        builder.Property(r => r.Evidence).HasColumnName("evidence").HasMaxLength(2000).IsRequired();

        // NC-11: the same evidence as numbers.
        builder.Property(r => r.EvidenceData)
            .HasConversion(
                v => v == null ? null : ReviewItemEvidenceJsonConverter.Serialize(v),
                v => v == null ? null : ReviewItemEvidenceJsonConverter.Deserialize(v))
            .HasColumnName("evidence_data").HasColumnType("json");

        builder.Property(r => r.State)
            .HasConversion(s => s.Value, value => new ReviewItemState(value))
            .HasColumnName("state").HasMaxLength(20).IsRequired();

        builder.Property(r => r.ResolvedWithAdjustment).HasColumnName("resolved_with_adjustment");
        builder.Property(r => r.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(1000);
        // X-2. Who wrote the note (Custom) or which sentence the system meant, with its parameters.
        builder.Property(r => r.ResolutionNoteCode).HasColumnName("resolution_note_code").HasMaxLength(30);
        builder.Property(r => r.ResolutionNoteData)
            .HasConversion(
                v => v == null ? null : ResolutionNoteDataJsonConverter.Serialize(v),
                v => v == null ? null : ResolutionNoteDataJsonConverter.Deserialize(v))
            .HasColumnName("resolution_note_data").HasColumnType("json");
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at");

        // NC-10: the scheduled recheck ("Revisar de nuevo en N días").
        builder.Property(r => r.RecheckDueAt).HasColumnName("recheck_due_at");
        builder.Property(r => r.RecheckIssuedAt).HasColumnName("recheck_issued_at");
        builder.HasIndex(r => r.RecheckDueAt).HasDatabaseName("ix_review_items_recheck_due_at");

        // NC-10. Inside the aggregate: EF navigation, one proposal per item at most, cascade delete.
        builder.HasOne(r => r.Proposal)
            .WithOne()
            .HasForeignKey<PlanAdjustmentProposal>(p => p.ReviewItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(r => r.IsOpen);
        builder.Ignore(r => r.HasPlanProposal);
        builder.Ignore(r => r.IsRecheckPending);
    }
}

/// <summary>NC-10. <c>review_item_plan_proposals</c>: the AI plan proposal of a review item.</summary>
public class PlanAdjustmentProposalEntityTypeConfiguration : IEntityTypeConfiguration<PlanAdjustmentProposal>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    private static readonly ValueConverter<List<string>, string> StringListConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

    public void Configure(EntityTypeBuilder<PlanAdjustmentProposal> builder)
    {
        builder.ToTable("review_item_plan_proposals");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(p => p.ReviewItemId)
            .HasColumnName("review_item_id")
            .HasConversion(id => id.Value, value => ReviewItemId.FromRaw(value))
            .IsRequired();
        builder.HasIndex(p => p.ReviewItemId).IsUnique()
            .HasDatabaseName("ix_review_item_plan_proposals_review_item_id");

        builder.Property(p => p.AiGenerationId).HasColumnName("ai_generation_id").IsRequired();
        builder.Property(p => p.Title).HasColumnName("title").HasMaxLength(PlanAdjustmentProposal.MaximumTitleLength)
            .IsRequired();
        builder.Property(p => p.ProposedEnergyKcal).HasColumnName("proposed_energy_kcal")
            .HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.ProposedProteinG).HasColumnName("proposed_protein_g")
            .HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.ProposedCarbG).HasColumnName("proposed_carb_g")
            .HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.ProposedFatG).HasColumnName("proposed_fat_g")
            .HasColumnType("decimal(10,2)").IsRequired();

        builder.Property<List<string>>("_addedGuidelines")
            .HasConversion(StringListConverter, StringListComparer)
            .HasColumnName("added_guidelines").HasColumnType("json").IsRequired();
        builder.Property<List<string>>("_removedGuidelines")
            .HasConversion(StringListConverter, StringListComparer)
            .HasColumnName("removed_guidelines").HasColumnType("json").IsRequired();

        builder.Property(p => p.PatientMessage).HasColumnName("patient_message")
            .HasMaxLength(PlanAdjustmentProposal.MaximumPatientMessageLength).IsRequired();
        builder.Property(p => p.RecheckAfterDays).HasColumnName("recheck_after_days").IsRequired();
        builder.Property(p => p.Rationale).HasColumnName("rationale")
            .HasMaxLength(PlanAdjustmentProposal.MaximumRationaleLength).IsRequired();
        builder.Property(p => p.GeneratedAt).HasColumnName("generated_at").IsRequired();
        builder.Property(p => p.Status)
            .HasConversion(s => s.Value, value => new PlanProposalStatus(value))
            .HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(p => p.AssignedPlanVersion).HasColumnName("assigned_plan_version");
        // X-2. The language of each part; null on proposals generated before X-2.
        builder.Property(p => p.PractitionerLanguage).HasColumnName("practitioner_language").HasMaxLength(5);
        builder.Property(p => p.PatientLanguage).HasColumnName("patient_language").HasMaxLength(5);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(p => p.AddedGuidelines);
        builder.Ignore(p => p.RemovedGuidelines);
        builder.Ignore(p => p.IsProposed);
        builder.Ignore(p => p.IsAccepted);
    }
}
