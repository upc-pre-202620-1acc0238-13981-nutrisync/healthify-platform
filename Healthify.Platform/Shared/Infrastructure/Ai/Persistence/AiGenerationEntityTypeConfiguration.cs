using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Persistence;

public class AiGenerationEntityTypeConfiguration : IEntityTypeConfiguration<AiGeneration>
{
    public void Configure(EntityTypeBuilder<AiGeneration> builder)
    {
        builder.ToTable("ai_generations");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(g => g.Feature).HasColumnName("feature").HasMaxLength(40).IsRequired();
        // Cross-context references: plain ints, no navigation, no foreign key constraint.
        builder.Property(g => g.SubjectPatientId).HasColumnName("subject_patient_id").IsRequired();
        builder.Property(g => g.RequestedByUserId).HasColumnName("requested_by_user_id");
        // NOTE: technical field. The MD sizes it at 20, which "practitioner-monitoring-summary@1" already exceeds.
        builder.Property(g => g.PromptVersion).HasColumnName("prompt_version").HasMaxLength(60);
        builder.Property(g => g.Model).HasColumnName("model").HasMaxLength(60);
        builder.Property(g => g.InputHash).HasColumnName("input_hash").HasColumnType("char(64)");
        // IN-7: the digest of the photo, the only trace of it the platform keeps.
        builder.Property(g => g.InputImageHash).HasColumnName("input_image_hash").HasColumnType("char(64)");
        builder.Property(g => g.OutputJson).HasColumnName("output_json").HasColumnType("json");
        builder.Property(g => g.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(g => g.ErrorCode).HasColumnName("error_code").HasMaxLength(40);
        builder.Property(g => g.InputTokens).HasColumnName("input_tokens").IsRequired();
        builder.Property(g => g.OutputTokens).HasColumnName("output_tokens").IsRequired();
        builder.Property(g => g.LatencyMs).HasColumnName("latency_ms").IsRequired();
        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(g => g.ExpiresAt).HasColumnName("expires_at");

        // Purge on revocation (by patient) and the rate limit (feature + subject or requester, recent first).
        builder.HasIndex(g => new { g.SubjectPatientId, g.Feature, g.CreatedAt })
            .HasDatabaseName("ix_ai_generations_subject_patient_id_feature_created_at");
        builder.HasIndex(g => new { g.RequestedByUserId, g.Feature, g.CreatedAt })
            .HasDatabaseName("ix_ai_generations_requested_by_user_id_feature_created_at");
        // Retention purge.
        builder.HasIndex(g => g.ExpiresAt).HasDatabaseName("ix_ai_generations_expires_at");
    }
}
