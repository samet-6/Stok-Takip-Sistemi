namespace StokTakip.Application.Products;

/// <summary>
/// An SKU is a code, not a word (D8): letters, digits and <c>. _ / -</c> only.
/// <para>
/// Why a character rule rather than "stored upper case": .NET's <c>ToUpperInvariant</c> leaves
/// <c>ı</c> and <c>ß</c> as they are, so <c>"abı-1"</c> was stored as <c>"ABı-1"</c> — lower case in
/// an upper-case column and a second spelling of <c>"ABI-1"</c> the uniqueness rule could not see.
/// A database <c>upper()</c> check would not have closed it either: under the dev database's
/// ctype C it changes no Turkish letter, under the container's en_US.utf8 it changes them all.
/// With only ASCII letters admitted, upper-casing is the same everywhere.
/// </para>
/// <para>
/// Lower case and surrounding spaces are accepted here because the service trims and
/// upper-cases before saving; the database's <c>CK_Products_SKU</c> holds the stored form
/// (<c>^[A-Z0-9._/-]+$</c>). The web form's zod rule mirrors this one.
/// </para>
/// </summary>
public static class SkuRule
{
    public const string Pattern = @"^\s*[A-Za-z0-9._/-]+\s*$";

    public const string Message = "Yalnız harf (A–Z), rakam ve . _ / - kullanılabilir";
}
