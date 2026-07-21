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
    }
}
