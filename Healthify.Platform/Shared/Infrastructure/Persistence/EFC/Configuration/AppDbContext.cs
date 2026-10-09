using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     The single database context of the platform.
/// </summary>
/// <remarks>
///     No explicit <c>DbSet</c> is declared on purpose: every bounded context ships its own
///     <c>IEntityTypeConfiguration</c> implementations, they are discovered by assembly scanning,
///     and repositories reach their tables through <c>Context.Set&lt;T&gt;()</c>. That keeps this
///     file free of any reference to the domain model of a bounded context.
/// </remarks>
public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        builder.AddInterceptors(new AuditableEntityInterceptor(), new UtcDateTimeInterceptor());
        base.OnConfiguring(builder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        builder.UseSnakeCaseNamingConvention();
    }
}
