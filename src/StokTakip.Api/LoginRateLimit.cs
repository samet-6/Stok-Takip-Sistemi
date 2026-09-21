namespace StokTakip.Api;

/// <summary>
/// The login endpoint's rate limit, stated once — the policy name has to match between the
/// registration and the attribute on the action, and a typo there fails silently: the endpoint
/// simply goes unlimited.
/// </summary>
public static class LoginRateLimit
{
    public const string PolicyName = "login";

    public const int PermitLimit = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
