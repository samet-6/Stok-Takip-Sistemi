using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StokTakip.Application.Common;
using StokTakip.Application.Products;
using StokTakip.Application.Services;
using StokTakip.Infrastructure.Data;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// The summary endpoint used to log an EF warning on every call —
/// <c>FirstWithoutOrderByAndFilterWarning</c>, a false alarm: EF looks for an OrderBy or filter
/// next to the First instead of asking whether more than one row was even possible, and grouping
/// on a constant collapses the scope to exactly one. The result was always right; the cost was
/// noise, and noise is where real warnings go to hide.
/// </summary>
/// <remarks>
/// The warning is raised while the query is <b>compiled</b>, not while it runs, and EF caches
/// compiled queries across every context sharing the same options — so watching the application's
/// log only catches this when no earlier test happened to compile the query first. That is a
/// false green waiting to happen. Escalating the warning to an exception on a context of its own
/// sidesteps both problems: different options mean a fresh compilation, every run.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SummaryLoggingTests
{
    private readonly TestDatabaseFixture _db;

    public SummaryLoggingTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ozet_sorgusu_EF_uyarisi_uretmiyor()
    {
        await using var context = StrictContext();
        var service = new ProductService(context, new UnusedUserLookup(), new UnusedNotifier());

        // Both shapes: ApplyScope adds a Where, which is exactly the kind of difference that
        // could make one of them warn and the other not.
        var unscoped = await service.GetSummaryAsync(new ProductScope(), Ct);
        var scoped = await service.GetSummaryAsync(new ProductScope(CategoryId: 1), Ct);

        // Reaching here at all is the assertion — the warning would have thrown. These keep the
        // test honest about having actually queried something.
        Assert.True(unscoped.TotalProducts > 0);
        Assert.True(scoped.TotalProducts >= 0);
    }

    /// <summary>
    /// Guards the test itself: with the row-limiting operator back in place the same setup must
    /// throw, otherwise "it did not throw" would prove nothing at all.
    /// </summary>
    [Fact]
    public async Task Kontrol_satir_sinirlayan_operator_ayni_kurulumda_istisna_firlatiyor()
    {
        await using var context = StrictContext();

        // The shape GetSummaryAsync deliberately avoids.
        var query = context.Products
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count() });

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.FirstOrDefaultAsync(Ct));
    }

    /// <summary>
    /// Same database, but warnings are errors — and the changed options give this context its own
    /// compiled-query cache, so the check cannot be skipped by an earlier test's compilation.
    /// </summary>
    private AppDbContext StrictContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_db.ConnectionString)
            .ConfigureWarnings(w => w.Throw(CoreEventId.FirstWithoutOrderByAndFilterWarning))
            .Options;

        return new AppDbContext(options);
    }

    // GetSummaryAsync touches neither of these; they exist because the constructor asks for them.
    private sealed class UnusedUserLookup : IUserLookupService
    {
        public Task<IReadOnlyDictionary<string, string>> GetFullNamesAsync(
            IEnumerable<string> userIds, CancellationToken ct)
            => throw new NotSupportedException("Özet sorgusu kullanıcı adı aramaz.");
    }

    private sealed class UnusedNotifier : IRealtimeNotifier
    {
        public void NotifyProductChanged(int productId)
            => throw new NotSupportedException("Özet sorgusu sinyal yaymaz.");

        public void NotifyNotificationsChanged()
            => throw new NotSupportedException("Özet sorgusu sinyal yaymaz.");
    }
}
