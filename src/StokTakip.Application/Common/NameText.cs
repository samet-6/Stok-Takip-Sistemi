using System.Globalization;
using System.Text;

namespace StokTakip.Application.Common;

/// <summary>
/// The single definition of "are these two catalogue names the same" (D9c).
/// <para>
/// Two layers, measured before they were chosen. <see cref="Clean"/> removes what the eye cannot
/// see — Unicode decomposition, invisible characters, stray whitespace — so names that look the
/// same are stored the same. <see cref="Key"/> then compares the cleaned names with Turkish case
/// folding only: <c>GIDA</c> = <c>Gıda</c>, but <c>Kıl</c> ≠ <c>Kil</c> and <c>Cam</c> ≠ <c>Çam</c>,
/// because in Turkish those are different letters, not decorated variants of one another.
/// Accent-insensitive folding (<c>f_fold</c>, the search key) was measured and rejected here: it
/// merged every one of those real pairs.
/// </para>
/// </summary>
public static class NameText
{
    /// <summary>
    /// Name of the generated uniqueness-key column on Categories, mapped as a shadow property.
    /// Lives here because the service's pre-check has to name it too.
    /// </summary>
    public const string NameKey = "NameKey";

    /// <summary>
    /// Brings a name typed or pasted by a person to one canonical form: NFC composition
    /// (a Word/macOS paste arrives as <c>S</c> + combining cedilla, not <c>Ş</c>), invisible format
    /// characters dropped (zero-width space, byte-order mark — .NET's <c>Trim()</c> keeps them),
    /// every run of whitespace — non-breaking space and tabs included — made one space, ends trimmed.
    /// Letters and their case are never touched.
    /// </summary>
    public static string Clean(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                // Only between words: nothing is emitted before the first letter or after the last.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The uniqueness key: <c>lower(value COLLATE "tr-TR-x-icu")</c>.
    /// <para>
    /// It has no body, like <see cref="SearchText.Fold"/>: inside an EF query it runs as the
    /// database's <c>f_name_key(text)</c> — the same function that generates the
    /// <see cref="NameKey"/> column, so the pre-check and the unique index cannot disagree.
    /// Calling it client-side throws on purpose; a second casing rule must not appear quietly.
    /// </para>
    /// </summary>
    public static string Key(string value) =>
        throw new InvalidOperationException(
            $"{nameof(NameText)}.{nameof(Key)} is only usable inside an EF query, where it runs " +
            "as f_name_key() in the database.");
}
