namespace StokTakip.Application.Auth;

// Fresh JWT issued after a successful self change-password so the user stays
// logged in despite the SecurityStamp bump.
public sealed record ChangePasswordResponse(string Token, DateTime ExpiresAt);
