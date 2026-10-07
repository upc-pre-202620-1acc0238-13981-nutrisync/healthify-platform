using System.Text.Json;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Configuration;

public class EvaluationWindowEntityTypeConfiguration : IEntityTypeConfiguration<EvaluationWindow>
{
    private static readonly ValueComparer<List<TargetsSnapshot>> SnapshotListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<DailyCompliance>> DayListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<AnthropometryPoint>> PointListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<EvaluationWindow> builder)
    {
        builder.ToTable("evaluation_windows");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => WindowId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context references: plain ints, no navigation, no foreign key constraint.
        builder.Property(w => w.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(w => w.PatientId).HasDatabaseName("ix_evaluation_windows_patient_id");

        builder.Property(w => w.CareLinkId).HasColumnName("care_link_id").IsRequired();
        builder.Property(w => w.WindowDays).HasColumnName("window_days").IsRequired();

        // The window boundaries are calendar days, stored at midnight because the database has no
        // date type that survives the round trip untouched, and rebuilt as calendar values in the
        // aggregate. Same reasoning as the declared local timestamp of the diary.
        builder.Property(w => w.FromDate).HasColumnName("from_date").IsRequired();
        builder.Property(w => w.ToDate).HasColumnName("to_date").IsRequired();

        builder.Property(w => w.State)
            .HasConversion(s => s.Value, value => new WindowState(value))
            .HasColumnName("state").HasMaxLength(20).IsRequired();
        builder.HasIndex(w => w.State).HasDatabaseName("ix_evaluation_windows_state");

        builder.Property(w => w.ClosedAt).HasColumnName("closed_at");
        builder.Property(w => w.LastLoggingGapFlaggedOn).HasColumnName("last_logging_gap_flagged_on");
        builder.Property(w => w.LastPatientRemindedAt).HasColumnName("last_patient_reminded_at");

        // The three series, stored as JSON through their backing fields. The value comparers are not
        // optional: without them EF never notices a change and updates are silently lost.
        builder.Property<List<TargetsSnapshot>>("_targetsSnapshots")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<TargetsSnapshot>()
                    : JsonSerializer.Deserialize<List<TargetsSnapshot>>(v, (JsonSerializerOptions?)null)
                      ?? new List<TargetsSnapshot>(),
                SnapshotListComparer)
            .HasColumnName("targets_snapshots").HasColumnType("json").IsRequired();

        builder.Property<List<DailyCompliance>>("_dailyComplianceSeries")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<DailyCompliance>()
                    : JsonSerializer.Deserialize<List<DailyCompliance>>(v, (JsonSerializerOptions?)null)
                      ?? new List<DailyCompliance>(),
                DayListComparer)
            .HasColumnName("daily_compliance_series").HasColumnType("json").IsRequired();

        builder.Property<List<AnthropometryPoint>>("_anthropometrySeries")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<AnthropometryPoint>()
                    : JsonSerializer.Deserialize<List<AnthropometryPoint>>(v, (JsonSerializerOptions?)null)
                      ?? new List<AnthropometryPoint>(),
                PointListComparer)
            .HasColumnName("anthropometry_series").HasColumnType("json").IsRequired();

        builder.Property(w => w.CreatedAt).HasColumnName("created_at");
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at");

        // Rebuilt in memory from the columns above; not database columns.
        builder.Ignore(w => w.From);
        builder.Ignore(w => w.To);
        builder.Ignore(w => w.IsOpen);
        builder.Ignore(w => w.TargetsSnapshots);
        builder.Ignore(w => w.TargetsSnapshot);
        builder.Ignore(w => w.DailyComplianceSeries);
        builder.Ignore(w => w.AnthropometrySeries);
        builder.Ignore(w => w.LoggedDaysCount);
        builder.Ignore(w => w.IntakeSummary);
    }
}

public class DeviationEntityTypeConfiguration : IEntityTypeConfiguration<Deviation>
{
    public void Configure(EntityTypeBuilder<Deviation> builder)
    {
        builder.ToTable("deviations");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => DeviationId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Same bounded context, but a separate aggregate: the reference is stored as its typed
        // identity and carries no EF navigation.
        builder.Property(d => d.WindowRef)
            .HasColumnName("window_id")
            .HasConversion(id => id.Value, value => WindowId.FromRaw(value))
            .IsRequired();
        builder.HasIndex(d => d.WindowRef).HasDatabaseName("ix_deviations_window_id");

        builder.Property(d => d.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(d => d.PatientId).HasDatabaseName("ix_deviations_patient_id");

        // Persisted projection of the DeviationMagnitude value object.
        builder.Property(d => d.MagnitudeRelativeValue)
            .HasColumnName("magnitude_relative_value").HasColumnType("decimal(10,4)").IsRequired();
        builder.Property(d => d.MagnitudeEnergyKcal)
            .HasColumnName("magnitude_energy_kcal").HasColumnType("decimal(10,2)").IsRequired();

        builder.Property(d => d.Direction)
            .HasConversion(dir => dir.Value, value => new DeviationDirection(value))
            .HasColumnName("direction").HasMaxLength(10).IsRequired();

        builder.Property(d => d.DetectedAt).HasColumnName("detected_at").IsRequired();
        builder.Property(d => d.IsSustained).HasColumnName("is_sustained").IsRequired();
        builder.Property(d => d.SustainedAt).HasColumnName("sustained_at");

        builder.Property(d => d.LoggedDaysConsidered)
            .HasColumnName("logged_days_considered").IsRequired();
        builder.Property(d => d.DeviatingDaysConsidered)
            .HasColumnName("deviating_days_considered").IsRequired();

        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(d => d.Magnitude);
    }
}

public class ConsistencyIndexEntityTypeConfiguration : IEntityTypeConfiguration<ConsistencyIndex>
{
    public void Configure(EntityTypeBuilder<ConsistencyIndex> builder)
    {
        builder.ToTable("consistency_indices");

        // The patient is the root: one index each, so the patient identifier is the key.
        builder.HasKey(i => i.PatientId);
        builder.Property(i => i.PatientId).HasColumnName("patient_id").ValueGeneratedNever();

        builder.Property(i => i.Value)
            .HasColumnName("value").HasColumnType("decimal(12,4)").IsRequired();

        builder.Property(i => i.State)
            .HasConversion(s => s.Value, value => new ConsistencyState(value))
            .HasColumnName("state").HasMaxLength(10).IsRequired();
        builder.HasIndex(i => i.State).HasDatabaseName("ix_consistency_indices_state");

        builder.Property(i => i.FirstFlaggedAt).HasColumnName("first_flagged_at");
        builder.Property(i => i.ShownToPatientAt).HasColumnName("shown_to_patient_at");
        // MA-7. The prompt issued, apart from the patient seeing it.
        builder.Property(i => i.PromptIssuedAt).HasColumnName("prompt_issued_at");
        builder.Ignore(i => i.IsPatientPromptPending);
        builder.Property(i => i.EscalatedAt).HasColumnName("escalated_at");
        builder.Property(i => i.AlertSinceAt).HasColumnName("alert_since_at");
        builder.Property(i => i.LastRecomputedAt).HasColumnName("last_recomputed_at").IsRequired();

        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(i => i.IsInAlert);
    }
}

public class ReferralEntityTypeConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> builder)
    {
        builder.ToTable("referrals");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ReferralId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(r => r.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(r => r.PatientId).HasDatabaseName("ix_referrals_patient_id");

        builder.Property(r => r.Specialty)
            .HasConversion(s => s.Value, value => new Specialty(value))
            .HasColumnName("specialty").HasMaxLength(Specialty.MaximumLength).IsRequired();

        builder.Property(r => r.Reason)
            .HasConversion(reason => reason.Value, value => new ReferralReason(value))
            .HasColumnName("reason").HasMaxLength(ReferralReason.MaximumLength).IsRequired();

        builder.Property(r => r.IssuedBy).HasColumnName("issued_by").IsRequired();
        builder.Property(r => r.IssuedAt).HasColumnName("issued_at").IsRequired();

        // RM-4. Open or Closed; existing referrals are Open.
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(10).IsRequired()
            .HasDefaultValue(Referral.Open);
        builder.Property(r => r.ClosedAt).HasColumnName("closed_at");
        builder.Ignore(r => r.IsOpen);

        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
    }
}

public class ScheduledFollowUpEntityTypeConfiguration : IEntityTypeConfiguration<ScheduledFollowUp>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<ScheduledFollowUp> builder)
    {
        builder.ToTable("scheduled_follow_ups");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => FollowUpId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(f => f.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(f => f.PatientId).HasDatabaseName("ix_scheduled_follow_ups_patient_id");

        builder.Property(f => f.PractitionerId).HasColumnName("practitioner_id").IsRequired();
        builder.HasIndex(f => f.PractitionerId)
            .HasDatabaseName("ix_scheduled_follow_ups_practitioner_id");

        builder.Property(f => f.ScheduledFor).HasColumnName("scheduled_for").IsRequired();

        builder.Property(f => f.State)
            .HasConversion(s => s.Value, value => new FollowUpState(value))
            .HasColumnName("state").HasMaxLength(20).IsRequired();

        builder.Property(f => f.MissedAt).HasColumnName("missed_at");

        // MA-2. The preparation codes as JSON through their backing field. The value comparer is not
        // optional: without it EF never notices a change and updates are silently lost.
        builder.Property<List<string>>("_preparation")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
                      ?? new List<string>(),
                StringListComparer)
            .HasColumnName("preparation").HasColumnType("json").IsRequired();

        builder.Property(f => f.Modality)
            .HasConversion(m => m.Value, value => new ConsultationModality(value))
            .HasColumnName("modality").HasMaxLength(15).IsRequired();

        builder.Property(f => f.CompletedAt).HasColumnName("completed_at");
        builder.Property(f => f.CompletedByConsultationId).HasColumnName("completed_by_consultation_id");
        builder.Property(f => f.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(f => f.CancellationReason)
            .HasColumnName("cancellation_reason").HasMaxLength(ScheduledFollowUp.CancellationReasonMaxLength);

        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(f => f.IsScheduled);
        builder.Ignore(f => f.Preparation);
        builder.Ignore(f => f.ScheduledAt);
    }
}

/// <summary>MA-4. Table pre_visit_check_ins: one row per visit.</summary>
public class PreVisitCheckInEntityTypeConfiguration : IEntityTypeConfiguration<PreVisitCheckIn>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<PatientQuestion>> QuestionListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<PreVisitCheckIn> builder)
    {
        builder.ToTable("pre_visit_check_ins");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => PreVisitCheckInId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Business rule: One Check In Per Visit (MA-4). A unique index, so two concurrent first answers cannot
        // both be stored.
        builder.Property(c => c.FollowUpId).HasColumnName("follow_up_id").IsRequired();
        builder.HasIndex(c => c.FollowUpId).IsUnique().HasDatabaseName("ux_pre_visit_check_ins_follow_up_id");

        builder.Property(c => c.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(c => c.PatientId).HasDatabaseName("ix_pre_visit_check_ins_patient_id");

        builder.Property(c => c.PractitionerId).HasColumnName("practitioner_id").IsRequired();

        builder.Property(c => c.Feeling)
            .HasConversion(f => f.Value, value => new PlanFeeling(value))
            .HasColumnName("feeling").HasMaxLength(PlanFeeling.MaximumLength).IsRequired();

        // The value comparers are not optional: without them EF never notices an edit.
        builder.Property<List<string>>("_difficulties")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
                      ?? new List<string>(),
                StringListComparer)
            .HasColumnName("difficulties").HasColumnType("json").IsRequired();

        builder.Property<List<PatientQuestion>>("_questions")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<PatientQuestion>()
                    : JsonSerializer.Deserialize<List<PatientQuestion>>(v, (JsonSerializerOptions?)null)
                      ?? new List<PatientQuestion>(),
                QuestionListComparer)
            .HasColumnName("questions").HasColumnType("json").IsRequired();

        builder.Property(c => c.SubmittedAt).HasColumnName("submitted_at").IsRequired();
        builder.Property(c => c.EditedAt).HasColumnName("edited_at");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(c => c.Difficulties);
        builder.Ignore(c => c.Questions);
    }
}

/// <summary>IA-2. Table <c>weekly_summaries</c>: one row per patient and week.</summary>
public class WeeklySummaryEntityTypeConfiguration : IEntityTypeConfiguration<WeeklySummary>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<WeeklySummary> builder)
    {
        builder.ToTable("weekly_summaries");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => WeeklySummaryId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(s => s.PatientId).HasColumnName("patient_id").IsRequired();

        // MySql.EntityFrameworkCore materializes a DATE column as DateTime and cannot cast it to DateOnly.
        builder.Property(s => s.WeekStart)
            .HasConversion(d => d.ToDateTime(TimeOnly.MinValue), v => DateOnly.FromDateTime(v))
            .HasColumnName("week_start").HasColumnType("date").IsRequired();
        builder.Property(s => s.WeekEnd)
            .HasConversion(d => d.ToDateTime(TimeOnly.MinValue), v => DateOnly.FromDateTime(v))
            .HasColumnName("week_end").HasColumnType("date").IsRequired();

        // Business rule: One Summary Per Patient And Week (IA-2). Two concurrent runs cannot both store a week.
        builder.HasIndex(s => new { s.PatientId, s.WeekStart }).IsUnique()
            .HasDatabaseName("ux_weekly_summaries_patient_id_week_start");

        builder.Property<string>("_complianceFactsJson")
            .HasColumnName("compliance_facts").HasColumnType("json").IsRequired();

        builder.Property(s => s.Headline).HasColumnName("headline")
            .HasMaxLength(WeeklySummary.HeadlineMaximumLength).IsRequired();

        builder.Property<List<string>>("_wentWell")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                StringListComparer)
            .HasColumnName("went_well").HasColumnType("json").IsRequired();

        builder.Property<List<string>>("_watchOut")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                StringListComparer)
            .HasColumnName("watch_out").HasColumnType("json").IsRequired();

        // NOTE: technical field. Traceability to ai_generations; a plain bigint, no constraint (Shared module).
        builder.Property(s => s.AiGenerationId).HasColumnName("ai_generation_id").IsRequired();
        builder.Property(s => s.Language).HasColumnName("language").HasMaxLength(5).IsRequired();
        builder.Property(s => s.GeneratedAt).HasColumnName("generated_at").IsRequired();
        builder.HasIndex(s => s.GeneratedAt).HasDatabaseName("ix_weekly_summaries_generated_at");

        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(s => s.ComplianceFacts);
        builder.Ignore(s => s.WentWell);
        builder.Ignore(s => s.WatchOut);
    }
}
