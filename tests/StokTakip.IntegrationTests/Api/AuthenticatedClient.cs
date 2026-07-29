using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Produces clients that carry a real token. Tokens are obtained by calling the real login
/// endpoint rather than being forged in the test: a hand-built token would skip password
/// verification, the security stamp and the active-user check, and every authorisation test
/// would then be proving something the application never does.
/// </summary>
public static class AuthenticatedClient
{
    public static Task<HttpClient> AsAdminAsync(this StokTakipFactory factory, CancellationToken ct = default) =>
        factory.AsUserAsync(StokTakipFactory.AdminEmail, StokTakipFactory.AdminPassword, ct);

    public static Task<HttpClient> AsCalisanAsync(this StokTakipFactory factory, CancellationToken ct = default) =>
        factory.AsUserAsync(StokTakipFactory.UserEmail, StokTakipFactory.UserPassword, ct);

    public static async Task<HttpClient> AsUserAsync(
        this StokTakipFactory factory, string email, string password, CancellationToken ct = default)
    {
        var client = factory.CreateClient();
        var token = await LoginAsync(client, email, password, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Attaches a token the caller already has. Used by the token-validation tests, which need
    /// to present tokens no login would ever hand out — expired, mis-signed, claim-less.
    /// </summary>
    public static HttpClient WithToken(this StokTakipFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Logs in and returns the raw token. Throws with the status code when login fails.</summary>
    public static async Task<string> LoginAsync(
        HttpClient client, string email, string password, CancellationToken ct = default)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Test girisi basarisiz ({(int)response.StatusCode}) — kullanici: {email}. " +
                "Seed calismadiysa veya seed parolalari degistiyse burasi kirilir.");

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct)
            ?? throw new InvalidOperationException("Giris yaniti okunamadi.");

        return body.Token;
    }

    /// <summary>Only the token is needed here; the full DTO lives in the application layer.</summary>
    private sealed record LoginResponse(string Token);
}
