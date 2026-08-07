using System.Text.RegularExpressions;
using Xunit;

namespace StokTakip.UnitTests.Architecture;

/// <summary>
/// Culture-sensitive casing is banned in production code.
/// <para>
/// <c>"DIAG".ToLower()</c> is <c>"dıag"</c> on a Turkish machine and <c>"diag"</c> on an English
/// one, so any comparison built on it depends on where the server happens to run. That is one of
/// the three sources of the Turkish search bug, and the dangerous half is the other direction:
/// <c>ToUpper()</c> turns <c>i</c> into <c>İ</c>, so a SKU normalised with it would be stored
/// with a character no keyboard produces — silently, and only on Turkish hosts.
/// </para>
/// <para>
/// Today <c>src/</c> uses <c>ToUpperInvariant()</c> everywhere and search folding happens in the
/// database. Nothing enforced that; this test does. It reads source text rather than IL because
/// the point is the call as written — an invariant call and a culture-sensitive one compile to
/// different methods, but only the source says which one an author chose on purpose.
/// </para>
/// </summary>
public sealed class CasingPurityTests
{
    /// <summary>ToLower()/ToUpper() with no argument. The Invariant variants are different method
    /// names, so they do not match; an explicit CultureInfo argument does not either.</summary>
    private static readonly Regex CultureSensitiveCasing =
        new(@"\.To(Lower|Upper)\(\s*\)", RegexOptions.Compiled);

    [Fact]
    public void Uretim_kodunda_kulture_duyarli_ToLower_ToUpper_yok()
    {
        var sourceRoot = FindSourceRoot();

        var files = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            // Generated migrations are EF's output, not hand-written policy.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .ToArray();

        // Guard the test itself: a wrong path would make the scan vacuously clean.
        Assert.True(files.Length > 20, $"Kaynak taramasi bos dondu ({files.Length} dosya): {sourceRoot}");

        var offenders = files
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, line, number: index + 1))
                .Where(entry => CultureSensitiveCasing.IsMatch(StripComment(entry.line)))
                .Select(entry => $"{Path.GetFileName(entry.path)}:{entry.number} -> {entry.line.Trim()}"))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Kulture duyarli casing kullanimi (ToLowerInvariant/ToUpperInvariant kullanin): "
            + string.Join(" | ", offenders));
    }

    /// <summary>Line comments explain the ban and quote the banned call; they are not code.</summary>
    private static string StripComment(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        var doc = line.IndexOf("///", StringComparison.Ordinal);

        if (doc >= 0) return line[..doc];

        return comment >= 0 ? line[..comment] : line;
    }

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");

            if (File.Exists(Path.Combine(directory.FullName, "StokTakip.sln")) && Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Depo koku bulunamadi (StokTakip.sln + src/). Baslangic: {AppContext.BaseDirectory}");
    }
}
