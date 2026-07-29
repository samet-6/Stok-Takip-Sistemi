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

        var xmin = db.Model.FindEntityType(typeof(Product))!.FindProperty("xmin");

        Assert.NotNull(xmin);
        Assert.True(xmin.IsConcurrencyToken);
        // The database bumps it on every UPDATE; EF must never try to write it.
        Assert.Equal(ValueGenerated.OnAddOrUpdate, xmin.ValueGenerated);
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
    public async Task Uc_unique_index_veritabaninda_mevcut()
    {
        await using var db = _db.CreateContext();

        var uniqueIndexes = await db.Database.SqlQueryRaw<string>(
            """
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """).ToListAsync(Ct);

        Assert.Contains("UQ_Categories_Name", uniqueIndexes);
        Assert.Contains("UQ_Products_SKU", uniqueIndexes);
        Assert.Contains("UQ_Suppliers_Name", uniqueIndexes);

        // Guard: a plain index must not show up here. Without it, a filter that quietly stopped
        // selecting on uniqueness would return every index and the three checks above would pass
        // even if none of those indexes were unique.
        Assert.DoesNotContain("IX_StockMovements_ProductId", uniqueIndexes);
    }
}
