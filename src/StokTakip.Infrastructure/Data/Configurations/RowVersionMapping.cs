using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StokTakip.Infrastructure.Data.Configurations;

public static class RowVersionMapping
{
    /// <summary>
    /// Optimistic concurrency via PostgreSQL's system column "xmin": a uint concurrency token that
    /// Postgres itself bumps on every UPDATE. Mapped onto the entity's own RowVersion property (the
    /// form the Npgsql documentation shows) rather than a shadow property, so projections read
    /// x.RowVersion instead of EF.Property&lt;uint&gt;(x, "xmin"). The column name is what keeps the
    /// migration from creating a real column — Npgsql recognises "xmin" as a system column.
    /// One definition for every entity that has the token, so they cannot drift apart.
    /// </summary>
    public static PropertyBuilder<uint> IsXminRowVersion(this PropertyBuilder<uint> property) =>
        property
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
}
