namespace StokTakip.Api.Realtime;

/// <summary>
/// Hub paths. Single source for <c>MapHub</c> and the JwtBearer events that only accept
/// a query-string token under this prefix — if the two ever drifted apart, either the hub
/// would stop authenticating or a token would be read from a URL somewhere it shouldn't be.
/// </summary>
public static class HubRoutes
{
    /// <summary>
    /// Deliberately NOT under <c>/api</c>: that path is the REST/ProblemDetails contract,
    /// and the long read timeout plus disabled buffering a live connection needs must not
    /// leak onto it.
    /// </summary>
    public const string Prefix = "/hubs";

    public const string Stok = Prefix + "/stok";
}
