using System.Net.Http.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// The search box, in Turkish. Every case below was measured as failing before the fix — the
/// list is not invented, it is the matrix from the refactor survey (§2.2.6 #51): ten terms typed
/// without a Turkish keyboard, none of which found anything.
/// <para>
/// The bug had three sources and they were independent, which is why the cases are grouped:
/// the term was folded by .NET's culture-sensitive <c>ToLower()</c>, the column by SQL
/// <c>lower()</c> under the database's ctype (so the outcome differed between the dev database
/// and the container), and neither of them can fold <c>I/ı/İ/i</c> onto one letter, because in
/// Turkish those are two separate case pairs.
/// </para>
/// <para>
/// Seeded rows on purpose: they carry the Turkish text this is about (<c>Çöp Poşeti 30L</c>,
/// <c>Zeytinyağı 1L</c>, <c>A4 Fotokopi Kağıdı</c>), T1 already pins them to the digit, and a
/// read-only test needs no sweeping.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductSearchTests
{
    private readonly TestDatabaseFixture _db;

    public ProductSearchTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The complaint that started this: a word beginning with a Turkish letter, typed the four
    /// ways a user types it. Under ctype C the column folded to <c>ÇÖp</c> and matched none of
    /// them; in the container it folded to <c>çöp</c> and matched two — the same search behaving
    /// differently per environment is the reason this test exists.
    /// </summary>
    [Theory]
    [InlineData("çöp")]
    [InlineData("ÇÖP")]
    [InlineData("Çöp")]
    [InlineData("cop")]
    [InlineData("COP")]
    public async Task Bastaki_Turkce_harf_dort_yazimda_da_buluyor(string term)
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var page = await SearchAsync(admin, term);

        Assert.Contains(page.Items, row => row.Name == "Çöp Poşeti 30L");
    }

    /// <summary>
    /// Terms holding a capital <c>I</c>. This is the half a "correct Turkish collation" would
    /// never fix: <c>lower('I')</c> is <c>'ı'</c> in Turkish, so an upper-cased term and a
    /// lower-cased column never meet. SKUs are stored upper case, so the SKU column is exactly
    /// where a user hits it.
    /// </summary>
    [Theory]
    [InlineData("OLIV", "Zeytinyağı 1L")]        // SKU OLIV-001
    [InlineData("oliv", "Zeytinyağı 1L")]
    [InlineData("SSD1", "Taşınabilir SSD 1TB")]  // SKU SSD1-001
    [InlineData("ELEKTRONIK", "Kablosuz Mouse")] // category Elektronik
    [InlineData("elektronik", "Kablosuz Mouse")]
    [InlineData("KIRTASIYE", "A4 Fotokopi Kağıdı")] // supplier Ege Kırtasiye
    public async Task Buyuk_I_iceren_terim_buluyor(string term, string expectedProduct)
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var page = await SearchAsync(admin, term);

        Assert.Contains(page.Items, row => row.Name == expectedProduct);
    }

    /// <summary>
    /// Ten terms as they come off a keyboard without Turkish letters. All ten were measured
    /// returning nothing. Terms are chosen to be genuine substrings of the seeded text — this is
    /// substring filtering, not linguistic search, so <c>kağıt</c> deliberately is not on the
    /// list (the row reads <c>Kağıdı</c>, and consonant mutation is out of scope by design).
    /// </summary>
    [Theory]
    [InlineData("cop", "Çöp Poşeti 30L")]
    [InlineData("poseti", "Çöp Poşeti 30L")]
    [InlineData("zeytinyagi", "Zeytinyağı 1L")]
    [InlineData("yesil", "Yeşil Çay 500g")]
    [InlineData("cay", "Yeşil Çay 500g")]
    [InlineData("tasinabilir", "Taşınabilir SSD 1TB")]
    [InlineData("kagidi", "A4 Fotokopi Kağıdı")]
    [InlineData("tukenmez", "Tükenmez Kalem 50'li")]
    [InlineData("yuzey", "Yüzey Temizleyici 750ml")]
    [InlineData("gida", "Yeşil Çay 500g")] // supplier Marmara Gıda Ltd. Şti.
    public async Task Turkce_klavyesiz_yazilan_terim_buluyor(string term, string expectedProduct)
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var page = await SearchAsync(admin, term);

        Assert.Contains(page.Items, row => row.Name == expectedProduct);
    }

    /// <summary>
    /// Sorting is the opposite problem from matching and needs the opposite tool: folding throws
    /// letter information away, ordering needs it. Under the database's byte order Ç and Ş sort
    /// after Z, so <c>Çöp Poşeti</c> landed at the end of the list instead of between the C and
    /// D words. The assertion is positional rather than a full sequence compare so it says which
    /// rule broke, not merely that something moved.
    /// </summary>
    [Fact]
    public async Task Siralama_Turkce_alfabeye_gore_C_harfini_D_den_once_koyuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var page = await SearchAsync(admin, term: null, extra: "pageSize=100");
        var names = page.Items.Select(row => row.Name).ToList();

        var cop = names.IndexOf("Çöp Poşeti 30L");
        var filtre = names.IndexOf("Filtre Kahve 1kg");
        var a4 = names.IndexOf("A4 Fotokopi Kağıdı");
        var zeytin = names.IndexOf("Zeytinyağı 1L");

        // Guard the test itself: if the rows were missing, every comparison below would compare
        // -1 with -1 and pass without measuring anything.
        Assert.True(cop >= 0 && filtre >= 0 && a4 >= 0 && zeytin >= 0, "Beklenen seed satirlari listede yok.");

        Assert.True(a4 < cop, $"A4 (index {a4}) Ç'den (index {cop}) once gelmeli.");
        Assert.True(cop < filtre, $"Ç (index {cop}) F'den (index {filtre}) once gelmeli — byte sirasinda sona dusuyordu.");
        Assert.True(filtre < zeytin, $"F (index {filtre}) Z'den (index {zeytin}) once gelmeli.");
    }

    /// <summary>
    /// The wildcards belong to LIKE, not to the user: someone typing % means the character.
    /// Without escaping, a single % would match the entire catalogue — a silent failure, since
    /// the screen fills with rows instead of showing an error.
    /// </summary>
    [Fact]
    public async Task Joker_karakter_yazan_kullanici_tum_katalogu_getirmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var page = await SearchAsync(admin, "%");

        Assert.Empty(page.Items);
    }

    private static async Task<ProductPage> SearchAsync(HttpClient admin, string? term, string? extra = null)
    {
        var query = term is null ? string.Empty : $"search={Uri.EscapeDataString(term)}";

        if (extra is not null)
            query = query.Length == 0 ? extra : $"{query}&{extra}";

        return (await admin.GetFromJsonAsync<ProductPage>($"/api/products?{query}", Ct))!;
    }

    private sealed record ProductPage(Row[] Items, int Page, int PageSize, int TotalCount, int TotalPages);

    private sealed record Row(int Id, string Name, string SKU);
}
