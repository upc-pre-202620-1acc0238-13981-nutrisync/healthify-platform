using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Configuration;

public class UserSessionEntityTypeConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => SessionId.FromRaw(value))
            .ValueGeneratedOnAdd();

        // Separate aggregate root inside the same bounded context: a plain int, no EF navigation.
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.HasIndex(s => s.UserId).HasDatabaseName("ix_user_sessions_user_id");

        builder.Property(s => s.RoleClaim)
            .HasConversion(role => role.Value, value => new Role(value))
            .HasColumnName("role_claim").HasMaxLength(20).IsRequired();

        // Nullable value object: explicit converter rather than a fragile nullable OwnsOne.
        builder.Property(s => s.NavigationShell)
            .HasConversion(new ValueConverter<NavigationShell?, string?>(
                shell => shell == null ? null : shell.Value,
                value => value == null ? null : new NavigationShell(value)))
            .HasColumnName("navigation_shell").HasMaxLength(30).IsRequired(false);

        builder.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(s => s.TerminatedAt).HasColumnName("terminated_at");

        // IAM-4. Only hashes are stored. The current one is unique: it is what a refresh looks the session up by.
        // It is also the concurrency token: every UPDATE of a session is conditional on the hash it read, so two
        // refreshes of the same token cannot both rotate; the one that loses replays the winner (grace).
        builder.Property(s => s.RefreshTokenHash)
            .HasColumnName("refresh_token_hash").HasColumnType("char(64)").IsRequired(false)
            .IsConcurrencyToken();
        builder.HasIndex(s => s.RefreshTokenHash).IsUnique()
            .HasDatabaseName("ix_user_sessions_refresh_token_hash");
        builder.Property(s => s.RefreshTokenExpiresAt).HasColumnName("refresh_token_expires_at");
        builder.Property(s => s.RefreshTokenRotatedAt).HasColumnName("refresh_token_rotated_at");
        builder.Property(s => s.PreviousRefreshTokenHash)
            .HasColumnName("previous_refresh_token_hash").HasColumnType("char(64)").IsRequired(false);
        builder.HasIndex(s => s.PreviousRefreshTokenHash)
            .HasDatabaseName("ix_user_sessions_previous_refresh_token_hash");

        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // Computed from mapped columns; not database columns.
        builder.Ignore(s => s.IsActive);
        builder.Ignore(s => s.ActiveRoleClaim);
    }
}
