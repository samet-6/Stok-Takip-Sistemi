using StokTakip.Application.Products;
using StokTakip.Domain.Entities;
using Xunit;

namespace StokTakip.UnitTests.Products;

/// <summary>
/// The rule has two forms — an EF expression for queries, and a plain boolean for the previous
/// state the edge detector judges (no entity holds "the product as it was before this movement").
/// This table is what keeps the two from drifting apart: every row is asserted against both.
/// </summary>
public class LowStockRuleTests
{
    public static TheoryData<bool, int, int, bool> Cases => new()
    {
        // isActive, quantity, minStockLevel, expected
        { true, 4, 5, true },     // below the minimum
        { true, 5, 5, true },     // exactly on it — the gap B27 closed
        { true, 6, 5, false },    // above it
        { true, 0, 5, true },     // empty is low too
        { true, 0, 0, true },     // a zero threshold still means "empty is low"
        { true, 1, 0, false },
        { false, 4, 5, false },   // passive is not low, it is closed
        { false, 0, 5, false }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Bellek_ici_form_beklenen_cevabi_veriyor(bool isActive, int quantity, int min, bool expected) =>
        Assert.Equal(expected, LowStockRule.IsLow(isActive, quantity, min));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ifade_formu_bellek_ici_formla_ayni_cevabi_veriyor(bool isActive, int quantity, int min, bool expected)
    {
        var product = new Product
        {
            Name = "Kural",
            SKU = "KURAL-01",
            IsActive = isActive,
            StockQuantity = quantity,
            MinStockLevel = min
        };

        Assert.Equal(expected, LowStockRule.Predicate.Compile()(product));
    }
}
