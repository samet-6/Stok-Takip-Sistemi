using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StokTakip.Application.Common;
using StokTakip.Domain.Entities;

namespace StokTakip.Infrastructure.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Id).UseIdentityAlwaysColumn();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired()
            .UseCollation(PostgresText.TurkishCollation);
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasIndex(c => c.Name).IsUnique().HasDatabaseName("UQ_Categories_Name");

        // Product search also matches on the category name, so it needs the same search key.
        // See ProductConfiguration for why the database generates it.
        builder.Property<string>(SearchText.NameFolded)
            .HasMaxLength(100)
            .HasComputedColumnSql(PostgresText.Folded("Name"), stored: true);

        builder.HasIndex(SearchText.NameFolded)
            .HasDatabaseName("IX_Categories_NameFolded")
            .HasMethod("gin")
            .HasOperators(PostgresText.TrigramOperators);
    }
}
