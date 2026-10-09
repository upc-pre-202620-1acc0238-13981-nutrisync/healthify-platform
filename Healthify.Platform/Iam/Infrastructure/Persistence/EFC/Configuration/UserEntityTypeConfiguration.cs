using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Configuration;

public class UserEntityTypeConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => UserId.FromRaw(value))
            .ValueGeneratedOnAdd();

        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => new Email(value))
            .HasColumnName("email").HasMaxLength(255).IsRequired();

        // Second line of defence for the rule Unique Email Required (Subflow 1.1).
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_users_email");

        // IAM-1. Two flat columns rather than an owned PersonName: see the note on User.GivenNames.
        builder.Property(u => u.GivenNames)
            .HasColumnName("given_names").HasMaxLength(80).IsRequired();
        builder.Property(u => u.FamilyNames)
            .HasColumnName("family_names").HasMaxLength(80).IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash").HasMaxLength(255).IsRequired();

        builder.Property(u => u.Role)
            .HasConversion(role => role.Value, value => new Role(value))
            .HasColumnName("role").HasMaxLength(20).IsRequired();

        // IAM-3. varchar(5) NOT NULL DEFAULT 'es': every account that existed before IAM-3 reads as Spanish.
        builder.Property(u => u.PreferredLanguage)
            .HasConversion(language => language.Value, value => new PreferredLanguage(value))
            .HasColumnName("preferred_language").HasMaxLength(5).IsRequired()
            .HasDefaultValue(PreferredLanguage.Default);

        builder.Property(u => u.FailedSignInAttempts)
            .HasColumnName("failed_sign_in_attempts").IsRequired();

        builder.Property(u => u.LockedOutAt).HasColumnName("locked_out_at");

        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");

        // Computed from mapped columns; not a database column.
        builder.Ignore(u => u.IsLockedOut);
        builder.Ignore(u => u.FullName);
    }
}
