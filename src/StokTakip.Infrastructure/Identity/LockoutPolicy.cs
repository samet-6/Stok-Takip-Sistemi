namespace StokTakip.Infrastructure.Identity;

/// <summary>
/// The lockout rule, stated once — the same discipline as PasswordPolicy, applied before a
/// second copy could appear: IdentityOptionsSetup configures Identity from these values and
/// AuthService builds the message the user sees from them.
/// </summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Says that waiting is the fix. Hiding the lockout was considered and rejected: it would
    /// leave someone typing their correct password at a wall with no explanation, while an
    /// attacker — who has been guessing that address for five attempts — already knows the
    /// account exists.
    /// </summary>
    public static string Message =>
        $"Çok fazla hatalı deneme. Hesabınız {Duration.TotalMinutes:0} dakika kilitlendi.";
}
