using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StokTakip.Infrastructure.Data.Seed;
using Xunit;

namespace StokTakip.IntegrationTests.Data;

/// <summary>
/// What the seeder does to a database that is <b>already running</b>. The seeder runs on every
/// startup, so "runs twice without changing anything" is not a nicety — it is the only reason
/// restarting the application is safe.
/// <para>
/// The assertions about what a <i>fresh</i> seed produces — row counts, Turkish spelling, stock
/// arithmetic — deliberately do not live here. They used to, and they were pinned against the
/// shared test database, which made them fail whenever some other class forgot to sweep its own
/// rows: a broken cleanup three files away was reported as "seed is wrong". They moved to
/// <see cref="SeedScenarioTests"/>, which seeds a database of its own (O28).
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeedTests
{
    private const string CalisanEmail = "user@stok.local";

    private readonly TestDatabaseFixture _db;

    public SeedTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Order-independent by construction: it compares the database against itself, so whatever
    /// other classes have left lying around is counted on both sides and cancels out.
    /// </summary>
    [Fact]
    public async Task Seed_ikinci_kez_kostugunda_satir_sayilari_degismiyor()
    {
        var before = await CountsAsync();

        await RunSeederAsync();

        Assert.Equal(before, await CountsAsync());
    }

    /// <summary>
    /// The seeder only fills in what is missing. If it ever "corrected" an existing user, every
    /// restart would quietly undo an admin's edits — and reset a password nobody asked it to.
    /// </summary>
    [Fact]
    public async Task Seed_mevcut_kullaniciya_dokunmuyor()
    {
        const string probe = "T1 seed testi";

        var original = await SetFullNameAsync(probe, Ct);

        try
        {
            await RunSeederAsync();

            await using var db = _db.CreateContext();
            var after = await db.Users.Where(u => u.Email == CalisanEmail).Select(u => u.FullName).SingleAsync(Ct);

            Assert.Equal(probe, after);
        }
        finally
        {
            // Later tests log in as this user; the row goes back exactly as it was.
            await SetFullNameAsync(original, CancellationToken.None);
        }
    }

    private async Task RunSeederAsync()
    {
        using var scope = _db.Factory.Services.CreateScope();

        await DbSeeder.SeedAsync(scope.ServiceProvider, includeDemoData: true);
    }

    private async Task<(int Categories, int Suppliers, int Products, int Movements, int Users)> CountsAsync()
    {
        await using var db = _db.CreateContext();

        return (
            await db.Categories.CountAsync(Ct),
            await db.Suppliers.CountAsync(Ct),
            await db.Products.CountAsync(Ct),
            await db.StockMovements.CountAsync(Ct),
            await db.Users.CountAsync(Ct));
    }

    private async Task<string> SetFullNameAsync(string fullName, CancellationToken ct)
    {
        await using var db = _db.CreateContext();

        var user = await db.Users.SingleAsync(u => u.Email == CalisanEmail, ct);
        var previous = user.FullName;
        user.FullName = fullName;
        await db.SaveChangesAsync(ct);

        return previous;
    }
}
