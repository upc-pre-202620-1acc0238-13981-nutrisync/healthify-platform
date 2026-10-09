using System.Text.Json;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Configuration;

public class ActiveTargetsCacheEntityTypeConfiguration : IEntityTypeConfiguration<ActiveTargetsCache>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<CachedPlanChange>?> CachedPlanChangeListComparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        c => c == null ? 0 : c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c == null ? null : c.ToList());

    private static readonly ValueComparer<List<CachedGuideline>> CachedGuidelineListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<ActiveTargetsCache> builder)
    {
        builder.ToTable("active_targets_caches");

        // The patient is the root: one cache each, so the patient identifier is the key.
        builder.HasKey(c => c.PatientId);
        builder.Property(c => c.PatientId).HasColumnName("patient_id").ValueGeneratedNever();

        builder.Property(c => c.PlanVersion).HasColumnName("plan_version").IsRequired();
        builder.Property(c => c.ValidFrom).HasColumnName("valid_from").IsRequired();

        builder.Property(c => c.EnergyKcal)
            .HasColumnName("energy_kcal").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(c => c.ProteinG)
            .HasColumnName("protein_g").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(c => c.CarbG)
            .HasColumnName("carb_g").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(c => c.FatG)
            .HasColumnName("fat_g").HasColumnType("decimal(10,2)").IsRequired();

        builder.Property(c => c.RefreshedAt).HasColumnName("refreshed_at").IsRequired();

        // Free text lists, stored as JSON through their backing fields. The value comparer is not
        // optional: without it EF never notices a change and updates are silently lost.
        builder.Property<List<string>>("_guidelines")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
                      ?? new List<string>(),
                StringListComparer)
            .HasColumnName("guidelines").HasColumnType("json").IsRequired();

        builder.Property<List<string>>("_restrictions")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
                      ?? new List<string>(),
                StringListComparer)
            .HasColumnName("restrictions").HasColumnType("json").IsRequired();

        // NC-6: the same guidelines as objects, [{ "code": ... } | { "custom": ... }], and the free text
        // restrictions from before the closed list.
        builder.Property<List<CachedGuideline>>("_guidelineItems")
            .HasConversion(
                v => CachedGuidelineJson.Serialize(v),
                v => CachedGuidelineJson.Deserialize(v),
                CachedGuidelineListComparer)
            .HasColumnName("guideline_items").HasColumnType("json").IsRequired();

        builder.Property<List<string>>("_legacyRestrictions")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
                      ?? new List<string>(),
                StringListComparer)
            .HasColumnName("legacy_restrictions").HasColumnType("json").IsRequired();

        // NC-8 and NC-9: "Qué cambió" and the practitioner's message, as the contract carried them.
        builder.Property<List<CachedPlanChange>?>("_changesFromPrevious")
            .HasConversion(
                v => v == null ? null : CachedGuidelineJson.SerializeChanges(v),
                v => v == null ? null : CachedGuidelineJson.DeserializeChanges(v),
                CachedPlanChangeListComparer)
            .HasColumnName("changes_from_previous").HasColumnType("json");
        builder.Property(c => c.PatientMessage).HasColumnName("patient_message").HasMaxLength(500);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(c => c.Guidelines);
        builder.Ignore(c => c.Restrictions);
        builder.Ignore(c => c.GuidelineItems);
        builder.Ignore(c => c.LegacyRestrictions);
        builder.Ignore(c => c.ChangesFromPrevious);
    }
}

public class DiaryEntryEntityTypeConfiguration : IEntityTypeConfiguration<DiaryEntry>
{
    public void Configure(EntityTypeBuilder<DiaryEntry> builder)
    {
        builder.ToTable("diary_entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => DiaryEntryId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context reference: a plain int, no navigation, no foreign key constraint.
        builder.Property(e => e.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(e => e.PatientId).HasDatabaseName("ix_diary_entries_patient_id");

        // The declared wall clock, under this exact name so that the shared UTC interceptor leaves
        // it alone, and as a plain type so that the diary can be read by date range.
        builder.Property(e => e.LocalTimestamp).HasColumnName("local_timestamp").IsRequired();
        builder.Property(e => e.LocalUtcOffsetMinutes)
            .HasColumnName("local_utc_offset_minutes").IsRequired();
        builder.HasIndex(e => e.LocalTimestamp).HasDatabaseName("ix_diary_entries_local_timestamp");

        builder.Property(e => e.Provenance)
            .HasConversion(p => p.Value, value => new Provenance(value))
            .HasColumnName("provenance").HasMaxLength(20).IsRequired();

        // IN-1. The answer to «¿Esta comida estaba en tu plan?». The database default covers rows
        // written before the column existed; the migration backfills the legacy off-plan ones.
        builder.Property(e => e.PlanAdherence)
            .HasConversion(a => a.Value, value => new PlanAdherence(value))
            .HasColumnName("plan_adherence").HasMaxLength(15).IsRequired()
            .HasDefaultValueSql("'NotAnswered'");

        builder.Property(e => e.PhotoRef).HasColumnName("photo_ref").HasMaxLength(500);

        // Persisted projection of the proposed estimate: what the on-device estimator suggested.
        builder.Property(e => e.ProposedReferenceFoodId).HasColumnName("proposed_reference_food_id");
        builder.Property(e => e.ProposedPortionGrams)
            .HasColumnName("proposed_portion_grams").HasColumnType("decimal(10,2)");
        builder.Property(e => e.ProposedConfidence)
            .HasColumnName("proposed_confidence").HasColumnType("decimal(6,4)");
        builder.Property(e => e.ProposedEstimatedAt).HasColumnName("proposed_estimated_at");

        // Persisted projection of the confirmed estimate: what the patient said. Both sets of
        // columns coexist, which is the whole of Proposal Kept Alongside Confirmation.
        builder.Property(e => e.ConfirmedReferenceFoodId).HasColumnName("confirmed_reference_food_id");
        builder.Property(e => e.ConfirmedPortionGrams)
            .HasColumnName("confirmed_portion_grams").HasColumnType("decimal(10,2)");
        builder.Property(e => e.ConfirmedAt).HasColumnName("confirmed_at");

        builder.Property(e => e.SyncState)
            .HasConversion(s => s.Value, value => new SyncState(value))
            .HasColumnName("sync_state").HasMaxLength(20).IsRequired();

        // Rule: Idempotency By Aggregate Id (Subflow 4.6). Unique, and nullable because an entry
        // created directly against the server never had a client identifier. MySQL allows any
        // number of NULLs in a unique index, which is exactly the behaviour wanted here.
        builder.Property(e => e.ClientEntryId).HasColumnName("client_entry_id");
        builder.HasIndex(e => e.ClientEntryId)
            .HasDatabaseName("ix_diary_entries_client_entry_id").IsUnique();

        // IN-6. The entries of one meal logged together (an idea of IA-3), and where they came from. Both null for
        // an entry logged alone, which is every entry written before IN-6.
        builder.Property(e => e.MealGroupId).HasColumnName("meal_group_id");
        builder.HasIndex(e => e.MealGroupId).HasDatabaseName("ix_diary_entries_meal_group_id");
        builder.Property(e => e.Origin)
            .HasConversion(o => o!.Value, value => new EntryOrigin(value))
            .HasColumnName("origin").HasMaxLength(EntryOrigin.MaximumLength);

        // IN-7. The photo analysis the proposal came from (unique: an analysis is logged once; MySQL allows any number
        // of NULLs, which every entry without one is) and the AI generation behind it. No foreign key: the analysis
        // is purged after its lifetime, the trace stays.
        builder.Property(e => e.MealPhotoAnalysisId).HasColumnName("meal_photo_analysis_id");
        builder.HasIndex(e => e.MealPhotoAnalysisId)
            .HasDatabaseName("ix_diary_entries_meal_photo_analysis_id").IsUnique();
        builder.Property(e => e.ProposedAiGenerationId).HasColumnName("proposed_ai_generation_id");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        // Rebuilt in memory from the columns above; not database columns.
        builder.Ignore(e => e.DeclaredLocalTimestamp);
        builder.Ignore(e => e.ProposedEstimate);
        builder.Ignore(e => e.ConfirmedEstimate);
        builder.Ignore(e => e.HasProposedEstimate);
        builder.Ignore(e => e.HasConfirmedEstimate);
        builder.Ignore(e => e.LocalDate);
    }
}

/// <summary>IN-7. The temporary photo analyses. There is no column for the image: it is never stored.</summary>
public class MealPhotoAnalysisEntityTypeConfiguration : IEntityTypeConfiguration<MealPhotoAnalysis>
{
    private static readonly ValueComparer<List<MealPhotoAlternative>> AlternativeListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<MealPhotoAnalysis> builder)
    {
        builder.ToTable("meal_photo_analyses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        // Cross-context references: plain ints, no navigation, no foreign key constraint.
        builder.Property(a => a.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(a => a.PatientId).HasDatabaseName("ix_meal_photo_analyses_patient_id");
        builder.Property(a => a.ReferenceFoodId).HasColumnName("reference_food_id").IsRequired();

        builder.Property(a => a.EstimatedGrams)
            .HasColumnName("estimated_grams").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(a => a.Confidence)
            .HasColumnName("confidence").HasColumnType("decimal(6,4)").IsRequired();

        builder.Property<List<MealPhotoAlternative>>("_alternatives")
            .HasConversion(
                v => JsonSerializer.Serialize(v.Select(AlternativeJson.Of).ToList(), (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<MealPhotoAlternative>()
                    : (JsonSerializer.Deserialize<List<AlternativeJson>>(v, (JsonSerializerOptions?)null) ?? new List<AlternativeJson>())
                    .Select(a => a.ToValueObject()).ToList(),
                AlternativeListComparer)
            .HasColumnName("alternatives").HasColumnType("json").IsRequired();

        // Technical reference to ai_generations (Shared), no constraint.
        builder.Property(a => a.AiGenerationId).HasColumnName("ai_generation_id").IsRequired();

        builder.Property(a => a.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.HasIndex(a => a.ExpiresAt).HasDatabaseName("ix_meal_photo_analyses_expires_at");

        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(a => a.Alternatives);
    }

    /// <summary>The JSON shape of one alternative: plain members, so the value object stays free of serializer concerns.</summary>
    private sealed record AlternativeJson(string Name, decimal Grams, int? ReferenceFoodId)
    {
        public static AlternativeJson Of(MealPhotoAlternative alternative)
        {
            return new AlternativeJson(alternative.Name, alternative.Grams, alternative.ReferenceFoodId);
        }

        public MealPhotoAlternative ToValueObject()
        {
            return new MealPhotoAlternative(Name, Grams, ReferenceFoodId);
        }
    }
}

public class SelfWeighInEntityTypeConfiguration : IEntityTypeConfiguration<SelfWeighIn>
{
    public void Configure(EntityTypeBuilder<SelfWeighIn> builder)
    {
        builder.ToTable("self_weigh_ins");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => SelfWeighInId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(w => w.PatientId).HasColumnName("patient_id").IsRequired();
        builder.HasIndex(w => w.PatientId).HasDatabaseName("ix_self_weigh_ins_patient_id");

        builder.Property(w => w.ValueKg)
            .HasColumnName("value_kg").HasColumnType("decimal(10,2)").IsRequired();

        builder.Property(w => w.LocalTimestamp).HasColumnName("local_timestamp").IsRequired();
        builder.Property(w => w.LocalUtcOffsetMinutes)
            .HasColumnName("local_utc_offset_minutes").IsRequired();

        // Persisted projection of the protocol declaration: fasted, same time of day, same scale.
        // IN-3: only fasted is asked now; the other two are null on new readings.
        builder.Property(w => w.ProtocolFastedState).HasColumnName("protocol_fasted_state").IsRequired();
        builder.Property(w => w.ProtocolSameTimeOfDay)
            .HasColumnName("protocol_same_time_of_day").IsRequired(false);
        builder.Property(w => w.ProtocolSameScale).HasColumnName("protocol_same_scale").IsRequired(false);

        // IN-4. Idempotency By Aggregate Id (Subflow 4.6), per patient. Online readings leave it NULL, and
        // MySQL lets a unique index hold any number of NULLs.
        builder.Property(w => w.ClientEntryId).HasColumnName("client_entry_id");
        builder.HasIndex(w => new { w.PatientId, w.ClientEntryId })
            .IsUnique()
            .HasDatabaseName("ix_self_weigh_ins_patient_id_client_entry_id");

        builder.Property(w => w.CreatedAt).HasColumnName("created_at");
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(w => w.DeclaredLocalTimestamp);
        builder.Ignore(w => w.ProtocolCompliance);
        builder.Ignore(w => w.FollowsProtocol);
        builder.Ignore(w => w.LocalDate);
    }
}

public class WeightTrendEntityTypeConfiguration : IEntityTypeConfiguration<WeightTrend>
{
    private static readonly ValueComparer<List<WeightTrendPoint>> PointListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    public void Configure(EntityTypeBuilder<WeightTrend> builder)
    {
        builder.ToTable("weight_trends");

        // The patient is the root: one trend each.
        builder.HasKey(t => t.PatientId);
        builder.Property(t => t.PatientId).HasColumnName("patient_id").ValueGeneratedNever();

        builder.Property(t => t.WindowSize).HasColumnName("window_size").IsRequired();
        builder.Property(t => t.LastRecalculatedAt).HasColumnName("last_recalculated_at").IsRequired();

        // The smoothed series, stored as JSON through its backing field.
        builder.Property<List<WeightTrendPoint>>("_points")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<WeightTrendPoint>()
                    : JsonSerializer.Deserialize<List<WeightTrendPoint>>(v, (JsonSerializerOptions?)null)
                      ?? new List<WeightTrendPoint>(),
                PointListComparer)
            .HasColumnName("points").HasColumnType("json").IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(t => t.Points);
        builder.Ignore(t => t.HasPoints);
    }
}
