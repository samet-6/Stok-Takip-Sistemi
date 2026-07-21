namespace StokTakip.Infrastructure.Identity;

// Full password policy stated as one message (mirrors the Identity options in
// Program.cs and the frontend zod schema). Shown as-is on any policy violation,
// shared by user-create/reset (UserService) and self change-password (AuthService).
internal static class PasswordPolicy
{
    public const string Message =
        "Şifre en az 8 karakter olmalı ve en az bir büyük harf, bir küçük harf, bir rakam ve bir özel karakter içermelidir.";
}
