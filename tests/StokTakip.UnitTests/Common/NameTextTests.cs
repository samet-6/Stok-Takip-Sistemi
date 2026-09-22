using StokTakip.Application.Common;
using Xunit;

namespace StokTakip.UnitTests.Common;

/// <summary>
/// Two names that look the same on screen must be stored the same way. Every case below was
/// measured (D9c probe): each pair looked identical in a list and still compared as different
/// under every uniqueness rule, including the database's.
/// </summary>
public class NameTextTests
{
    public static TheoryData<string, string> Cases => new()
    {
        // input, expected
        { "Gıda", "Gıda" },                               // already clean: untouched
        { "  Gıda  ", "Gıda" },                           // leading/trailing space
        { "Gıda ", "Gıda" },                         // non-breaking space (Excel/web paste)
        { "Gıda​", "Gıda" },                         // zero-width space — .NET Trim() keeps it
        { "﻿Gıda", "Gıda" },                         // byte-order mark at the front
        { "Ege  Kırtasiye", "Ege Kırtasiye" },            // double space inside
        { "Ege Kırtasiye", "Ege Kırtasiye" },        // non-breaking space inside
        { "Ege \t Kırtasiye", "Ege Kırtasiye" },          // tab inside
        { "Şile", "Şile" },                         // NFD (Word/macOS paste) → one character
        { "İnci", "İnci" },                         // NFD dotted capital I
        { "Kıl", "Kıl" },                                 // real letters are never touched…
        { "GIDA", "GIDA" },                               // …and neither is case
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Gorunusu_ayni_adlari_tek_bicime_indiriyor(string input, string expected) =>
        Assert.Equal(expected, NameText.Clean(input));

    // A name made only of invisible characters must come out empty, so the caller can refuse it
    // instead of storing a row whose name nobody can see.
    [Fact]
    public void Yalniz_gorunmez_karakterden_olusan_ad_bos_kaliyor() =>
        Assert.Equal("", NameText.Clean("​  ﻿"));
}
