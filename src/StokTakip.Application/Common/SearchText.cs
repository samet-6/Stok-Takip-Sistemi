using System.Text;

namespace StokTakip.Application.Common;

/// <summary>
/// The single definition of "are these two strings the same for search purposes".
/// <para>
/// Folding happens in the <b>database</b> (<c>f_fold</c>), never in C#. That is a fix for a
/// measured bug: the term was folded by .NET's <c>ToLower()</c> (server culture) while the column
/// was folded by SQL <c>lower()</c> (database ctype). The two rules never met on any Turkish
/// character, and the outcome changed per environment — dev runs ctype <c>C</c>, the compose
/// database <c>en_US.utf8</c>, so the same search worked in Docker and failed locally.
/// One implementation is the point of this class.
/// </para>
/// <para>
/// A "correct" Turkish collation would not fix it either: in Turkish <c>I/ı</c> and <c>İ/i</c> are
/// separate case pairs, so <c>lower('I')</c> is always <c>'ı'</c> and <c>I</c> never meets <c>i</c>.
/// Folding all four onto one letter has to be explicit. Sorting wants the opposite (it needs the
/// letter information) and is solved separately, by a <c>tr-TR-x-icu</c> collation on the column.
/// </para>
/// </summary>
public static class SearchText
{
    /// <summary>
    /// Names of the generated search-key columns, mapped as shadow properties. They live here
    /// rather than in the persistence configuration because the query side has to name them too,
    /// and a typo would silently search nothing.
    /// </summary>
    public const string NameFolded = "NameFolded";

    /// <inheritdoc cref="NameFolded"/>
    public const string SkuFolded = "SkuFolded";

    /// <summary>
    /// Turns text into its search key: accents drop, case collapses
    /// (<c>Çöp</c> → <c>cop</c>, <c>Işıl</c> → <c>isil</c>, <c>İSTANBUL</c> → <c>istanbul</c>).
    /// <para>
    /// It has no body: inside an EF query it is translated to the database's <c>f_fold(text)</c>
    /// (mapped in <c>AppDbContext.OnModelCreating</c>). The searched columns are STORED generated
    /// columns produced by that same function, so the term and the column go through identical
    /// code. Calling it client-side throws on purpose — a second folding rule must not appear
    /// quietly, which is exactly how the original bug was born.
    /// </para>
    /// </summary>
    public static string Fold(string value) =>
        throw new InvalidOperationException(
            $"{nameof(SearchText)}.{nameof(Fold)} is only usable inside an EF query, where it " +
            "runs as f_fold() in the database. Folding never happens on the client.");

    /// <summary>
    /// Makes user input safe to embed in a <c>LIKE</c> pattern: <c>%</c> and <c>_</c> are
    /// wildcards, and a user who types them means the literal character. The escape character is
    /// <c>\</c>, PostgreSQL's <c>LIKE</c> default.
    /// </summary>
    public static string EscapeLike(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is '\\' or '%' or '_')
                builder.Append('\\');

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Turns a user's term into a "contains" pattern.</summary>
    public static string ContainsPattern(string term) => $"%{EscapeLike(term.Trim())}%";
}
