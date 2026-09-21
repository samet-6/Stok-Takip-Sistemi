using StokTakip.Application.Common;
using Xunit;

namespace StokTakip.UnitTests.Common;

/// <summary>
/// The date-filter boundary rule, pinned where it is actually observable.
///
/// The integration suite can prove the endpoint accepts each wire form, but not how it reads one:
/// when the process runs at UTC offset zero — Docker and the deployed application — a boundary
/// read as local time and one read as UTC are the same instant, so a wrong arm passes silently
/// there and only misbehaves on a server with an offset. These assertions compare instants
/// directly, so they hold in every timezone, including the ones the application ships on.
/// </summary>
public sealed class FilterInstantTests
{
    /// <summary>An instant is already an instant — converting it again would shift it twice.</summary>
    [Fact]
    public void Utc_deger_oldugu_gibi_kaliyor()
    {
        var utc = new DateTime(2026, 3, 15, 9, 30, 0, DateTimeKind.Utc);

        var result = FilterInstant.ToUtc(utc);

        Assert.Equal(utc, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    /// <summary>
    /// The expected path: the frontend sends the viewer's local day boundary with its offset.
    /// Asserted through <see cref="DateTime.ToUniversalTime"/> rather than a fixed number of
    /// hours, because the correct shift depends on the machine's timezone — the claim is that
    /// the offset is applied, not that it is three hours.
    /// </summary>
    [Fact]
    public void Local_deger_UTC_ye_ceviriliyor()
    {
        var local = new DateTime(2026, 3, 15, 9, 30, 0, DateTimeKind.Local);

        var result = FilterInstant.ToUtc(local);

        Assert.Equal(local.ToUniversalTime(), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    /// <summary>
    /// The defensive arm, and the one this class exists for. An offset-less value carries no
    /// timezone information, so it is taken as already-UTC: the wall clock is kept and only the
    /// Kind is stamped. Reading it as local time instead would move the boundary by the server's
    /// offset — invisible on a UTC server, wrong everywhere else.
    /// </summary>
    [Fact]
    public void Unspecified_deger_UTC_kabul_ediliyor_saat_kaydirilmadan()
    {
        var bare = new DateTime(2026, 3, 15, 9, 30, 0, DateTimeKind.Unspecified);

        var result = FilterInstant.ToUtc(bare);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(bare.Ticks, result.Ticks);
    }

    /// <summary>
    /// The two non-local forms of the same wall clock must land on the same instant. This is the
    /// assertion that fails if Unspecified is ever "fixed" into a local reading, and unlike the
    /// HTTP-level test it fails on a UTC machine too — because it never asks the machine.
    /// </summary>
    [Fact]
    public void Unspecified_ve_Utc_ayni_saati_ayni_ana_goturuyor()
    {
        var wallClock = new DateTime(2026, 3, 15, 9, 30, 0);

        Assert.Equal(
            FilterInstant.ToUtc(DateTime.SpecifyKind(wallClock, DateTimeKind.Utc)),
            FilterInstant.ToUtc(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified)));
    }

    /// <summary>Normalizing an already-normalized boundary changes nothing.</summary>
    [Fact]
    public void Ikinci_kez_cagrildiginda_deger_degismiyor()
    {
        var once = FilterInstant.ToUtc(new DateTime(2026, 3, 15, 9, 30, 0, DateTimeKind.Local));

        Assert.Equal(once, FilterInstant.ToUtc(once));
    }
}
