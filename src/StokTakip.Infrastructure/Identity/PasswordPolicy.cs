namespace StokTakip.Infrastructure.Identity;

/// <summary>
/// The password policy, stated once. Identity's options, the sentence the user sees and the
/// shared test vectors all read these values. Before this the same rule was written out by hand
/// in five places, and the numbers lived inline in Program.cs where nothing else could ask them —
/// raising the length would have enforced 10 while three copies still promised 8.
/// </summary>
public static class PasswordPolicy
{
    public const int RequiredLength = 8;

    // static readonly rather than const: Message branches on these, and a const false would make
    // a branch unreachable (CS0162) in a project that builds warning-free.
    public static readonly bool RequireUppercase = true;
    public static readonly bool RequireLowercase = true;
    public static readonly bool RequireDigit = true;
    public static readonly bool RequireNonAlphanumeric = true;

    /// <summary>
    /// Shown as-is on any policy violation: the whole rule at once, not the single clause the
    /// user happened to miss first. Telling them one rule per attempt turns setting a password
    /// into a guessing game, which is why Identity's per-rule descriptions are not used.
    /// </summary>
    public static string Message
    {
        get
        {
            var clauses = new List<string>();
            if (RequireUppercase) clauses.Add("bir büyük harf");
            if (RequireLowercase) clauses.Add("bir küçük harf");
            if (RequireDigit) clauses.Add("bir rakam");
            if (RequireNonAlphanumeric) clauses.Add("bir özel karakter");

            return $"Şifre en az {RequiredLength} karakter olmalı ve en az " +
                   string.Join(", ", clauses) + " içermelidir.";
        }
    }
}
