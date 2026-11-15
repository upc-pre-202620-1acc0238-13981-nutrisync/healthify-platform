using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Configuration;

public class FollowUpReminderLogEntityTypeConfiguration : IEntityTypeConfiguration<FollowUpReminderLog>
{
    public void Configure(EntityTypeBuilder<FollowUpReminderLog> builder)
    {
        builder.ToTable("follow_up_reminder_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(l => l.FollowUpId).HasColumnName("follow_up_id").IsRequired();
        builder.Property(l => l.Recipient).HasColumnName("recipient").HasMaxLength(15).IsRequired();
        builder.Property(l => l.LeadMinutes).HasColumnName("lead_minutes").IsRequired();
        builder.Property(l => l.VisitMoment).HasColumnName("visit_moment").IsRequired();
        builder.Property(l => l.SentAt).HasColumnName("sent_at").IsRequired();

        // The guarantee against duplicates: one reminder per visit, recipient, window and moment of the visit.
        builder.HasIndex(l => new { l.FollowUpId, l.Recipient, l.LeadMinutes, l.VisitMoment })
            .IsUnique()
            .HasDatabaseName("ux_follow_up_reminder_logs_reminder");
    }
}
