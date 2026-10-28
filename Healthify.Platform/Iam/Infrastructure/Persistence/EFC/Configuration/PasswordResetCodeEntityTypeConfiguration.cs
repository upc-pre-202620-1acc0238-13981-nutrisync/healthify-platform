using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Configuration;

public class PasswordResetCodeEntityTypeConfiguration : IEntityTypeConfiguration<PasswordResetCode>
{
    public void Configure(EntityTypeBuilder<PasswordResetCode> builder)
    {
        builder.ToTable("password_reset_codes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedOnAdd();

        // Separate aggregate root inside the same bounded context: a plain int, no EF navigation.
        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        builder.HasIndex(c => c.UserId).HasDatabaseName("ix_password_reset_codes_user_id");

        // IAM-2. Only the keyed hash is stored, never the digits.
        builder.Property(c => c.CodeHash)
            .HasColumnName("code_hash").HasColumnType("char(64)").IsRequired();

        builder.Property(c => c.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(c => c.FailedAttempts).HasColumnName("failed_attempts").IsRequired();
        builder.Property(c => c.UsedAt).HasColumnName("used_at");

        // IAM-2. Verification. Only the hash of the reset token is stored; it is unique because a reset looks the
        // code up by it.
        builder.Property(c => c.VerifiedAt).HasColumnName("verified_at");
        builder.Property(c => c.ResetTokenHash)
            .HasColumnName("reset_token_hash").HasColumnType("char(64)").IsRequired(false);
        builder.HasIndex(c => c.ResetTokenHash).IsUnique()
            .HasDatabaseName("ix_password_reset_codes_reset_token_hash");
        builder.Property(c => c.ResetTokenExpiresAt).HasColumnName("reset_token_expires_at");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
    }
}
