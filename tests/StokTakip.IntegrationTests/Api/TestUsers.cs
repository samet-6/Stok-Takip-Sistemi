using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Sweeps the throwaway accounts, the way TestScratch sweeps throwaway products. Without it the
    /// rows simply accumulated: a full run used to end with seventeen of them still in the table.
    /// Nothing noticed, because no test counted users — the first one that does would have started
    /// failing depending on which class ran before it.
    ///
    /// A plain delete is enough: these accounts only ever log in and get edited. Movements and
    /// notifications are written by the two seeded users, so nothing points at these rows.
    /// </summary>
    public static async Task CleanupAsync(TestDatabaseFixture db, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        // Spelled out rather than looped over a prefix array, which EF cannot translate.
        // "t3-yeni-" is covered by "t3-": the email-change test renames an account and the new
        // address keeps the prefix — which is why the prefix goes at the front, not the back.
        await context.Users
            .Where(u => u.Email!.StartsWith("t2-") || u.Email!.StartsWith("t3-"))
            .ExecuteDeleteAsync(ct);
    }

    private sealed record CreatedUser(string Id);
}

internal sealed record TestUser(string Id, string Email, string Password);
