using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Configuration;

public class AiPreferencesEntityTypeConfiguration : IEntityTypeConfiguration<AiPreferences>
{
    public void Configure(EntityTypeBuilder<AiPreferences> builder)
    {
        builder.ToTable("ai_preferences");

        // One row per patient: the patient is the identity (IA-1). Cross-context reference, no foreign key.
        builder.HasKey(p => p.PatientId);
        builder.Property(p => p.PatientId).HasColumnName("patient_id").ValueGeneratedNever();

        builder.Property(p => p.WeeklySummaryEnabled).HasColumnName("weekly_summary_enabled").IsRequired();
        builder.Property(p => p.MealIdeasEnabled).HasColumnName("meal_ideas_enabled").IsRequired();
        builder.Property(p => p.SuggestedQuestionsEnabled).HasColumnName("suggested_questions_enabled")
            .IsRequired();
        // IN-7. Rows from before IN-7 stay off (the migration adds it with DEFAULT 0): nobody consented to sending
        // photos yet.
        builder.Property(p => p.MealPhotoRecognitionEnabled).HasColumnName("meal_photo_recognition_enabled")
            .IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(p => p.AnyEnabled);
    }
}
