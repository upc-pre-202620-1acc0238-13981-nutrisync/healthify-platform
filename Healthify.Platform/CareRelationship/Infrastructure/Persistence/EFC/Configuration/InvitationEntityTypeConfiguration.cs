using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Configuration;

public class InvitationEntityTypeConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => InvitationId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Cross-context reference to the practitioner account: a plain int, no navigation, no
        // foreign key constraint. Consistency across contexts is kept with events.
        builder.Property(i => i.IssuedBy).HasColumnName("issued_by").IsRequired();
        builder.HasIndex(i => i.IssuedBy).HasDatabaseName("ix_invitations_issued_by");

        builder.Property(i => i.Token)
            .HasConversion(token => token.Value, value => new InvitationToken(value))
            .HasColumnName("token").HasMaxLength(64).IsRequired();

        // Second line of defence for the rule Single Use Token (Subflow 2.1).
        builder.HasIndex(i => i.Token).IsUnique().HasDatabaseName("ix_invitations_token");

        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(i => i.RedeemedAt).HasColumnName("redeemed_at");
        builder.Property(i => i.ExpiredAt).HasColumnName("expired_at");

        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");

        // Computed from mapped columns; not database columns.
        builder.Ignore(i => i.IsRedeemed);
        builder.Ignore(i => i.IsExpired);
    }
}
