namespace StokTakip.Application.Auth;

public sealed record HubTicketResponse(string Token, DateTime ExpiresAt);
