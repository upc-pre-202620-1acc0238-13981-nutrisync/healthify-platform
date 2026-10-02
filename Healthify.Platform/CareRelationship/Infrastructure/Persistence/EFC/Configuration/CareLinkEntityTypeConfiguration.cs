using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Configuration;

public class CareLinkEntityTypeConfiguration : IEntityTypeConfiguration<CareLink>
{
    public void Configure(EntityTypeBuilder<CareLink> builder)
    {
        builder.ToTable("care_links");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => CareLinkId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context references: plain ints, no navigation, no foreign key constraint.
        builder.Property(c => c.PatientId).HasColumnName("patient_id").IsRequired();
        builder.Property(c => c.PractitionerId).HasColumnName("practitioner_id").IsRequired();
        builder.HasIndex(c => c.PatientId).HasDatabaseName("ix_care_links_patient_id");
        builder.HasIndex(c => c.PractitionerId).HasDatabaseName("ix_care_links_practitioner_id");

        builder.Property(c => c.EstablishedAt).HasColumnName("established_at").IsRequired();
        builder.Property(c => c.RevokedAt).HasColumnName("revoked_at");
        builder.Property(c => c.RevocationReason)
            .HasColumnName("revocation_reason")
            .HasMaxLength(30)
            .HasConversion(reason => reason!.Value, value => new RevocationReason(value));
        builder.Property(c => c.DischargedAt).HasColumnName("discharged_at");
        builder.Property(c => c.DischargeReason).HasColumnName("discharge_reason").HasMaxLength(500);

        builder.Property(c => c.PendingTargetsVersion).HasColumnName("pending_targets_version");
        builder.Property(c => c.LastAcknowledgedVersion).HasColumnName("last_acknowledged_version");
        // CR-3. When the acknowledgement was given.
        builder.Property(c => c.LastAcknowledgedAt).HasColumnName("last_acknowledged_at");

        // Persisted projection of the Consent value object. Mapped as columns rather than as a
        // nullable owned type, because consent is genuinely absent between establishing the link
        // and the patient granting it, and a nullable OwnsOne is fragile in that exact case.
        builder.Property(c => c.ConsentGranted).HasColumnName("consent_granted").IsRequired();
        builder.Property(c => c.ConsentScope).HasColumnName("consent_scope").HasMaxLength(200);
        builder.Property(c => c.ConsentGrantedAt).HasColumnName("consent_granted_at");
        builder.Property(c => c.ConsentWithdrawnAt).HasColumnName("consent_withdrawn_at");
        // CR-2. AI processing consent, inside the same consent. Existing rows: off, never decided.
        builder.Property(c => c.ConsentAiProcessingGranted).HasColumnName("consent_ai_processing")
            .IsRequired().HasDefaultValue(false);
        builder.Property(c => c.ConsentAiProcessingDecidedAt).HasColumnName("consent_ai_decided_at");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        // Rebuilt in memory from the columns above; not database columns.
        builder.Ignore(c => c.Consent);
        builder.Ignore(c => c.IsActive);
        builder.Ignore(c => c.IsRevoked);
        builder.Ignore(c => c.IsDischarged);
        builder.Ignore(c => c.IsPendingConsent);
        builder.Ignore(c => c.HasAiProcessingConsent);
    }
}
