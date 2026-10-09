using System.Text.Json;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;

public class NutritionPlanEntityTypeConfiguration : IEntityTypeConfiguration<NutritionPlan>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<Guideline>> GuidelineListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c.ToList());

    private static readonly ValueComparer<List<PlanChange>?> NullablePlanChangeListComparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        c => c == null ? 0 : c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c == null ? null : c.ToList());

    private static readonly ValueComparer<List<string>?> NullableStringListComparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        c => c == null ? 0 : c.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
        c => c == null ? null : c.ToList());

    public void Configure(EntityTypeBuilder<NutritionPlan> builder)
    {
        builder.ToTable("nutrition_plans");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => PlanId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context references: plain ints, no navigation, no foreign key constraint.
        builder.Property(p => p.PatientId).HasColumnName("patient_id").IsRequired();
        builder.Property(p => p.PractitionerId).HasColumnName("practitioner_id").IsRequired();
        builder.Property(p => p.DiagnosisId).HasColumnName("diagnosis_id").IsRequired();
        builder.Property(p => p.Version).HasColumnName("version").IsRequired();
        builder.HasIndex(p => p.PatientId).HasDatabaseName("ix_nutrition_plans_patient_id");

        // Persisted projection of the calculation basis. Always recorded: without it a target is a
        // number nobody can defend a year later. It never leaves this bounded context.
        builder.Property(p => p.BasisEquation)
            .HasConversion(e => e.Value, value => new Equation(value))
            .HasColumnName("basis_equation").HasMaxLength(30).IsRequired();
        builder.Property(p => p.BasisReferenceWeightKind)
            .HasColumnName("basis_reference_weight_kind").HasMaxLength(20).IsRequired();
        builder.Property(p => p.BasisReferenceWeightKg)
            .HasColumnName("basis_reference_weight_kg").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.BasisActivityFactor)
            .HasColumnName("basis_activity_factor").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.BasisDeficitKind)
            .HasColumnName("basis_deficit_kind").HasMaxLength(20).IsRequired();
        builder.Property(p => p.BasisDeficitValue)
            .HasColumnName("basis_deficit_value").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.BasisComputedBmr)
            .HasColumnName("basis_computed_bmr").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.BasisComputedTdee)
            .HasColumnName("basis_computed_tdee").HasColumnType("decimal(10,2)").IsRequired();

        // Persisted projection of the target proposal: what the arithmetic produced.
        builder.Property(p => p.ProposalEnergyKcal)
            .HasColumnName("proposal_energy_kcal").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.ProposalProteinG)
            .HasColumnName("proposal_protein_g").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.ProposalCarbG)
            .HasColumnName("proposal_carb_g").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.ProposalFatG)
            .HasColumnName("proposal_fat_g").HasColumnType("decimal(10,2)").IsRequired();

        // Persisted projection of the prescribed targets: what a person signed. Absent until then.
        builder.Property(p => p.PrescribedEnergyKcal)
            .HasColumnName("prescribed_energy_kcal").HasColumnType("decimal(10,2)");
        builder.Property(p => p.PrescribedProteinG)
            .HasColumnName("prescribed_protein_g").HasColumnType("decimal(10,2)");
        builder.Property(p => p.PrescribedCarbG)
            .HasColumnName("prescribed_carb_g").HasColumnType("decimal(10,2)");
        builder.Property(p => p.PrescribedFatG)
            .HasColumnName("prescribed_fat_g").HasColumnType("decimal(10,2)");
        builder.Property(p => p.PrescribedOutcome)
            .HasColumnName("prescribed_outcome").HasMaxLength(30);
        builder.Property(p => p.PrescribedOverrideReason)
            .HasColumnName("prescribed_override_reason").HasMaxLength(500);

        // X-2. The reason is three columns: the sentence (legacy fallback, or the practitioner's text when the code
        // is Custom), its code and the parameters of the code. ChangeReason is computed from them.
        builder.Ignore(p => p.ChangeReason);
        builder.Property(p => p.ChangeReasonText).HasColumnName("change_reason").HasMaxLength(500);
        builder.Property(p => p.ChangeReasonCode).HasColumnName("change_reason_code").HasMaxLength(30);
        builder.Property(p => p.ChangeReasonData)
            .HasConversion(
                v => v == null ? null : ChangeReasonDataJsonConverter.Serialize(v),
                v => v == null ? null : ChangeReasonDataJsonConverter.Deserialize(v))
            .HasColumnName("change_reason_data").HasColumnType("json");

        builder.Property(p => p.PublishedAt).HasColumnName("published_at");
        builder.Property(p => p.SupersededAt).HasColumnName("superseded_at");
        // NC-2. An unpublished draft left out by its consultation.
        builder.Property(p => p.DiscardedAt).HasColumnName("discarded_at");
        builder.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();

        // Guidelines and restrictions are lists stored as JSON, and mapped through their backing fields
        // because the aggregate exposes them as read-only. The value comparer is not optional: without it
        // EF never notices a change and updates are silently lost.
        // NC-6: guidelines are objects, a catalog code or a custom text (GuidelineJsonConverter), and
        // restrictions are DietaryRestriction codes.
        builder.Property<List<Guideline>>("_guidelines")
            .HasConversion(
                v => GuidelineJsonConverter.Serialize(v),
                v => GuidelineJsonConverter.Deserialize(v),
                GuidelineListComparer)
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

        // NC-6: free text restrictions from before the closed list that match no code, and the ones a new
        // version left out. Both are kept as written.
        builder.Property<List<string>>("_legacyRestrictions")
            .HasConversion(
                v => GuidelineJsonConverter.SerializeStrings(v),
                v => GuidelineJsonConverter.DeserializeStrings(v),
                StringListComparer)
            .HasColumnName("legacy_restrictions").HasColumnType("json").IsRequired();

        builder.Property<List<string>?>("_droppedLegacyRestrictions")
            .HasConversion(
                v => v == null ? null : GuidelineJsonConverter.SerializeStrings(v),
                v => v == null ? null : GuidelineJsonConverter.DeserializeStrings(v),
                NullableStringListComparer)
            .HasColumnName("dropped_legacy_restrictions").HasColumnType("json");

        // NC-8: "Qué cambió en esta versión", recorded once at publication. NULL for versions published before.
        builder.Property<List<PlanChange>?>("_changesFromPrevious")
            .HasConversion(
                v => v == null ? null : PlanChangeJsonConverter.Serialize(v),
                v => v == null ? null : PlanChangeJsonConverter.Deserialize(v),
                NullablePlanChangeListComparer)
            .HasColumnName("patient_change_summary").HasColumnType("json");

        // NC-9: the practitioner's message to the patient for this version.
        builder.Property(p => p.PatientMessage).HasColumnName("patient_message").HasMaxLength(500);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // Rebuilt in memory from the columns above; not database columns.
        builder.Ignore(p => p.Guidelines);
        builder.Ignore(p => p.Restrictions);
        builder.Ignore(p => p.LegacyRestrictions);
        builder.Ignore(p => p.DroppedLegacyRestrictions);
        builder.Ignore(p => p.ChangesFromPrevious);
        builder.Ignore(p => p.HasRecordedChanges);
        builder.Ignore(p => p.CalculationBasis);
        builder.Ignore(p => p.TargetProposal);
        builder.Ignore(p => p.PrescribedTargets);
        builder.Ignore(p => p.IsPrescribed);
        builder.Ignore(p => p.IsPublished);
        builder.Ignore(p => p.IsSuperseded);
        builder.Ignore(p => p.IsDiscarded);
    }
}
