using StokTakip.Application.Common;
using Xunit;

namespace StokTakip.UnitTests.Common;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(100, 10, 10)] // tam bolunen
    [InlineData(95, 10, 10)]  // artan: son sayfa yarim dolu
    [InlineData(5, 10, 1)]    // tek sayfa
    [InlineData(0, 10, 0)]    // kayit yok
    public void TotalPages_sayfa_sayisini_dogru_hesapliyor(int totalCount, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: pageSize, TotalCount: totalCount);

        Assert.Equal(expected, result.TotalPages);
    }

    /// <summary>
    /// PageSize normally arrives clamped from the query object, but TotalPages divides by it —
    /// so the guard has to hold in the type itself, not only in its callers.
    /// </summary>
    [Fact]
    public void TotalPages_sifir_sayfa_boyutunda_sifirla_bolmuyor()
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: 0, TotalCount: 42);

        Assert.Equal(0, result.TotalPages);
    }
}
