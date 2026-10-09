using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.FoodCatalog.Infrastructure.Persistence.EFC.Configuration;

public class ReferenceFoodEntityTypeConfiguration : IEntityTypeConfiguration<ReferenceFood>
{
    public void Configure(EntityTypeBuilder<ReferenceFood> builder)
    {
        builder.ToTable("reference_foods");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ReferenceFoodId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(f => f.LocalNameText)
            .HasColumnName("local_name").HasMaxLength(LocalName.MaxLength).IsRequired();
        builder.HasIndex(f => f.LocalNameText).HasDatabaseName("ix_reference_foods_local_name");

        // Persisted projection of the nutrients value object, always per 100 grams.
        builder.Property(f => f.EnergyKcalPer100g)
            .HasColumnName("energy_kcal_per_100g").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(f => f.ProteinGPer100g)
            .HasColumnName("protein_g_per_100g").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(f => f.CarbGPer100g)
            .HasColumnName("carb_g_per_100g").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(f => f.FatGPer100g)
            .HasColumnName("fat_g_per_100g").HasColumnType("decimal(10,2)").IsRequired();

        // The upstream fingerprint. Unique, because it is the identity that makes a repeated import
        // a no-op instead of a duplicate row.
        builder.Property(f => f.SourceHash)
            .HasConversion(h => h.Value, value => new SourceHash(value))
            .HasColumnName("source_hash").HasMaxLength(SourceHash.Length).IsRequired();
        builder.HasIndex(f => f.SourceHash)
            .HasDatabaseName("ix_reference_foods_source_hash").IsUnique();

        builder.Property(f => f.IsLocalOverride).HasColumnName("is_local_override").IsRequired();

        // IN-7. Traceability, stored and never published. Rows from before IN-7 are Imported or LocalOverride
        // (backfilled from is_local_override) and verified.
        builder.Property(f => f.Source)
            .HasConversion(s => s.Value, value => new FoodSource(value))
            .HasColumnName("source").HasMaxLength(FoodSource.MaxLength).IsRequired();
        builder.HasIndex(f => f.Source).HasDatabaseName("ix_reference_foods_source");
        builder.Property(f => f.IsVerified).HasColumnName("is_verified").IsRequired();
        // Cross-context reference to the user who verified it: a plain int, no navigation, no constraint.
        builder.Property(f => f.VerifiedBy).HasColumnName("verified_by");
        // Technical reference to ai_generations (Shared), no constraint: the audit row has its own retention.
        builder.Property(f => f.AiGenerationId).HasColumnName("ai_generation_id");

        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        // Rebuilt in memory from the columns above; not database columns.
        builder.Ignore(f => f.LocalName);
        builder.Ignore(f => f.NutrientsPer100g);
        builder.Ignore(f => f.IsProtectedFromImport);
    }
}
