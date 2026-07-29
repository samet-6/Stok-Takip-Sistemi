using System.Net.Http.Json;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Session-invalidation tests deactivate accounts and reset passwords, so they must not touch
/// the two seeded users the rest of the suite logs in with. Each test gets its own throwaway
/// employee, created through the real admin endpoint — a row written straight into the database
/// would skip the password hashing and role assignment the test then depends on.
/// </summary>
internal static class TestUsers
{
    public const string Password = "T2Calisan!2026";

    public static async Task<TestUser> CreateCalisanAsync(
        StokTakipFactory factory, CancellationToken ct)
    {
        using var admin = await factory.AsAdminAsync(ct);

        var email = $"t2-{Guid.NewGuid():N}@stok.local";
        var response = await admin.PostAsJsonAsync(
            "/api/users",
            new { fullName = "T2 Test Çalışanı", email, password = Password },
            ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Test kullanicisi olusturulamadi ({(int)response.StatusCode}).");

        var created = await response.Content.ReadFromJsonAsync<CreatedUser>(ct)
            ?? throw new InvalidOperationException("Kullanici yaniti okunamadi.");

        return new TestUser(created.Id, email, Password);
    }

    private sealed record CreatedUser(string Id);
}

internal sealed record TestUser(string Id, string Email, string Password);
