using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StokTakip.Infrastructure.Identity;

namespace StokTakip.Infrastructure.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(100).IsRequired();

        // Soft-delete + audit dates. DB-level defaults backfill existing rows on migration
        // and auto-populate new users created via UserManager (CreatedAt = insert time, UTC).
        builder.Property(u => u.IsActive).HasDefaultValue(true);
        builder.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        // DeactivatedAt: nullable timestamptz, no default (null while active).

        // D12: Identity builds EmailIndex non-unique whatever RequireUniqueEmail says, so the rule
        // is restated here to reach the database. Deactivated users keep their address on purpose:
        // the row stays for the audit trail, and a re-hire is a reactivation, not a new account.
        builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
    }
}
