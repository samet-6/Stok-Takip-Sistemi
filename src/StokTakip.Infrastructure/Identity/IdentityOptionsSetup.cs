using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace StokTakip.Infrastructure.Identity;

/// <summary>
/// Identity's options configured where the rules live. Wiring is the composition root's job; a
/// password rule is not a wiring decision, and while it sat inline in Program.cs nothing else
/// could read it — which is how the policy sentence ended up hand-copied in four more places.
/// </summary>
public sealed class IdentityOptionsSetup : IConfigureOptions<IdentityOptions>
{
    public void Configure(IdentityOptions options)
    {
        options.Password.RequiredLength = PasswordPolicy.RequiredLength;
        options.Password.RequireUppercase = PasswordPolicy.RequireUppercase;
        options.Password.RequireLowercase = PasswordPolicy.RequireLowercase;
        options.Password.RequireDigit = PasswordPolicy.RequireDigit;
        options.Password.RequireNonAlphanumeric = PasswordPolicy.RequireNonAlphanumeric;

        // Brute-force fence. These numbers live here for the same reason the password ones do:
        // AuthService needs the duration to put in its message, and a second copy of "15" typed
        // into that sentence would be the same defect all over again.
        options.Lockout.MaxFailedAccessAttempts = LockoutPolicy.MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutPolicy.Duration;
        options.Lockout.AllowedForNewUsers = true;

        // The e-mail is the login, so two accounts on one address is not a state to allow.
        // The database has the matching unique index (ApplicationUserConfiguration).
        options.User.RequireUniqueEmail = true;
    }
}
