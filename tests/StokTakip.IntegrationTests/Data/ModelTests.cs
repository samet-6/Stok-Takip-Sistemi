using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using StokTakip.Domain.Entities;
using Xunit;

namespace StokTakip.IntegrationTests.Data;

/// <summary>
/// The EF model and the schema it produced have to agree with the design doc. Asserted against
/// the migrated database, not against the configuration code that would only repeat itself.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ModelTests
{
    private readonly TestDatabaseFixture _db;

    public ModelTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Product_xmin_concurrency_token_olarak_tanimli()
    {
        using var db = _db.CreateContext();

        var xmin = db.Model.FindEntityType(typeof(Product))!.FindProperty(nameof(Product.RowVersion));

        Assert.NotNull(xmin);
        Assert.True(xmin.IsConcurrencyToken);
        // The database bumps it on every UPDATE; EF must never try to write it.
        Assert.Equal(ValueGenerated.OnAddOrUpdate, xmin.ValueGenerated);
        // The property is the entity's own now, but the column it reads is still PostgreSQL's
        // system column — that mapping is what keeps the migration from creating a real one.
        Assert.Equal("xmin", xmin.GetColumnName());
    }

    [Fact]
    public async Task Xmin_tabloda_gercek_kolon_uretmiyor()
    {
        await using var db = _db.CreateContext();

        var columns = await db.Database.SqlQueryRaw<string>(
            """
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Products'
            """).ToListAsync(Ct);

        // Guard: proves the query is really looking at the Products table.
        Assert.Contains("SKU", columns);

        // xmin is PostgreSQL's own system column — a real column of that name would mean the
        // shadow property was mapped as ordinary data and concurrency checks compare nothing.
        Assert.DoesNotContain("xmin", columns);
    }

    [Fact]
    public async Task Katalog_unique_indexleri_veritabaninda_mevcut()
    {
        await using var db = _db.CreateContext();

        var uniqueIndexes = await db.Database.SqlQueryRaw<string>(
            """
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """).ToListAsync(Ct);

        // D9c: the category key is the Turkish lower-case name, not the name as typed.
        Assert.Contains("UQ_Categories_NameKey", uniqueIndexes);
        Assert.DoesNotContain("UQ_Categories_Name", uniqueIndexes);
        Assert.Contains("UQ_Products_SKU", uniqueIndexes);

        // A supplier's name is a label, not its identity — two firms may share it (D9c).
        Assert.DoesNotContain("UQ_Suppliers_Name", uniqueIndexes);

        // Guard: a plain index must not show up here. Without it, a filter that quietly stopped
        // selecting on uniqueness would return every index and the checks above would pass even
        // if none of those indexes were unique.
        Assert.DoesNotContain("IX_StockMovements_ProductId_CreatedAt_Id", uniqueIndexes);
    }

    /// <summary>
    /// D17/D20: every date-ordered list reads "newest first, Id breaking ties", so each gets an
    /// index in exactly that order — otherwise PostgreSQL still sorts the page after the index
    /// hands it over (measured: an Incremental Sort on every list). D16: the bell counts unread
    /// rows only, so that index holds only those. Compared as the database spells them, so a
    /// dropped DESC or a lost predicate fails here rather than in a slow page later.
    /// </summary>
    [Fact]
    public async Task Liste_indexleri_okuma_sirasiyla_tanimli()
    {
        await using var db = _db.CreateContext();

        var definitions = await db.Database.SqlQueryRaw<string>(
            """
            SELECT indexname || ' ' || substring(indexdef FROM 'USING btree (.*)$') AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename IN ('StockMovements', 'Notifications')
            """).ToListAsync(Ct);

        Assert.Contains("""IX_StockMovements_CreatedAt_Id ("CreatedAt" DESC, "Id" DESC)""", definitions);
        Assert.Contains("""IX_StockMovements_ProductId_CreatedAt_Id ("ProductId", "CreatedAt" DESC, "Id" DESC)""", definitions);
        Assert.Contains("""IX_StockMovements_CreatedByUserId_CreatedAt_Id ("CreatedByUserId", "CreatedAt" DESC, "Id" DESC)""", definitions);
        Assert.Contains("""IX_Notifications_CreatedAt_Id ("CreatedAt" DESC, "Id" DESC)""", definitions);
        Assert.Contains("""IX_Notifications_Unread_ProductId ("ProductId") WHERE ("ReadAt" IS NULL)""", definitions);

        // The foreign key keeps an index covering every row: the partial one above only sees
        // unread notifications, and a product delete has to find the read ones too.
        Assert.Contains("""IX_Notifications_ProductId ("ProductId")""", definitions);

        // Superseded: each is the leading column of a composite above, which serves the same
        // lookups. Left in place they would only cost writes.
        var names = definitions.Select(d => d.Split(' ')[0]).ToList();
        Assert.DoesNotContain("IX_StockMovements_CreatedAt", names);
        Assert.DoesNotContain("IX_StockMovements_ProductId", names);
        Assert.DoesNotContain("IX_StockMovements_CreatedByUserId", names);
        Assert.DoesNotContain("IX_Notifications_CreatedAt", names);
    }
}
