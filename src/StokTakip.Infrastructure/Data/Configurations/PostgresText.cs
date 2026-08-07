namespace StokTakip.Infrastructure.Data.Configurations;

/// <summary>
/// PostgreSQL-specific text handling used by more than one configuration. Kept in one place so a
/// searched column and the index that serves it can never drift apart.
/// </summary>
internal static class PostgresText
{
    /// <summary>
    /// Real Turkish alphabetical order (Ç after C, Ğ after G, İ after I, Ş after S, Ü after U).
    /// Applied at column level, so an ordinary <c>OrderBy(x =&gt; x.Name)</c> sorts correctly
    /// without every query having to remember. Byte order — the default under ctype C — puts
    /// Ç and Ş after Z.
    /// </summary>
    public const string TurkishCollation = "tr-TR-x-icu";

    /// <summary>
    /// The database-side folding function created by the AddTurkishSearch migration:
    /// <c>lower(unaccent('unaccent', $1))</c>, wrapped because <c>unaccent</c> itself is STABLE
    /// and a generated column requires IMMUTABLE.
    /// </summary>
    public const string FoldFunction = "f_fold";

    /// <summary>
    /// Trigram operator class. It is what makes an unanchored <c>LIKE '%term%'</c> indexable at
    /// all; a plain B-tree cannot serve one.
    /// </summary>
    public const string TrigramOperators = "gin_trgm_ops";

    /// <summary>The generated-column expression for a searched column.</summary>
    public static string Folded(string column) => $"{FoldFunction}(\"{column}\")";
}
