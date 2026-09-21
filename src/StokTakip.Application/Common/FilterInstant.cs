namespace StokTakip.Application.Common;

/// <summary>
/// The single definition of "what instant does this filter boundary mean".
/// <para>
/// Date filters compare against <c>timestamptz</c> columns, which hold instants — the backend
/// never learns the viewer's timezone. The frontend sends offset-aware boundaries (the local day
/// start as <c>…+03:00</c>), so the comparison runs UTC-vs-UTC. This class handles the two other
/// forms a hand-written request can carry: an already-UTC value, and a value with no timezone
/// information at all.
/// </para>
/// <para>
/// It lives here rather than inside the service because the difference between its arms is
/// <b>invisible over HTTP</b> when the process runs at UTC offset zero — which is exactly how the
/// application ships (Docker, deployment). An integration test can only prove the endpoint accepts
/// the wire form; under UTC, a value read as local and a value read as UTC produce identical
/// results, so a wrong arm would pass unnoticed. As a pure function it is pinned by unit tests that
/// hold in every timezone. (.NET cannot reassign <c>TimeZoneInfo.Local</c> in-process, so running
/// the suite "under another timezone" is not an option either.)
/// </para>
/// </summary>
public static class FilterInstant
{
    /// <summary>
    /// Normalizes a filter boundary to the UTC instant the query compares against.
    /// <list type="bullet">
    /// <item><description><c>Utc</c> — already an instant; used as-is.</description></item>
    /// <item><description><c>Local</c> — offset-aware input, converted.</description></item>
    /// <item><description><c>Unspecified</c> — no timezone information: taken as already-UTC.
    /// This is the defensive arm. Npgsql rejects an Unspecified <c>DateTime</c> against
    /// <c>timestamptz</c> outright, and reading it as local time would silently shift every
    /// boundary by the server's offset — which, on a server set to UTC, would look correct right
    /// up until it was deployed somewhere else.</description></item>
    /// </list>
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
