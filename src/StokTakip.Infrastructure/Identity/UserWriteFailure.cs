using Microsoft.AspNetCore.Identity;
using StokTakip.Application.Common.Exceptions;

namespace StokTakip.Infrastructure.Identity;

/// <summary>
/// Turns a failed UserManager write into what the admin is told. UserService checks for a taken
/// address first, so a duplicate only arrives here when two admins race for it — and then it
/// gets the very answer the pre-check gives, not a message about something else.
/// </summary>
public static class UserWriteFailure
{
    // UserName tracks Email, so a taken user name is a taken address.
    private static readonly string[] DuplicateCodes =
    [
        nameof(IdentityErrorDescriber.DuplicateEmail),
        nameof(IdentityErrorDescriber.DuplicateUserName)
    ];

    private static readonly string[] EmailCodes =
    [
        nameof(IdentityErrorDescriber.InvalidEmail),
        nameof(IdentityErrorDescriber.InvalidUserName)
    ];

    /// <summary>
    /// Required fields pass DataAnnotations first, so apart from a duplicate what is left is the
    /// password policy — reported whole, not just the rule that was missed.
    /// </summary>
    public static Exception ForCreate(IdentityResult result) =>
        IsDuplicate(result)
            ? Duplicate()
            : new BadRequestException(
                new Dictionary<string, string[]> { ["password"] = [PasswordPolicy.Message] });

    /// <summary>
    /// UpdateAsync runs Identity's user validator, whose failures are about the email or the
    /// username derived from it; anything else is reported without blaming a specific field.
    /// </summary>
    public static Exception ForUpdate(IdentityResult result)
    {
        if (IsDuplicate(result))
            return Duplicate();

        return result.Errors.Any(e => EmailCodes.Contains(e.Code))
            ? new BadRequestException(
                new Dictionary<string, string[]> { ["email"] = ["Geçerli bir e-posta girin."] })
            : new BadRequestException("Kullanıcı güncellenemedi.");
    }

    private static bool IsDuplicate(IdentityResult result) =>
        result.Errors.Any(e => DuplicateCodes.Contains(e.Code));

    private static ConflictException Duplicate() => new("Bu e-posta zaten kayıtlı");
}
