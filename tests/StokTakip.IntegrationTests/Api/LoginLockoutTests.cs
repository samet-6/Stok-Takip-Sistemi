using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Brute-force fence. Until now Options.Lockout was configured, documented and completely inert:
/// AuthService called UserManager.CheckPasswordAsync, which only compares a hash — it never
/// touches AccessFailedCount and never looks at LockoutEnd. The counter had never moved once.
/// <para>
/// Every test brings its own account. Failing logins against a shared user would lock it and
/// take the rest of the suite down with it — the seeded admin is what most classes log in with.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoginLockoutTests : IAsyncLifetime
{
    private const string WrongPassword = "Kesinlikle!Yanlis2026";

    private readonly TestDatabaseFixture _db;

    public LoginLockoutTests(TestDatabaseFixture db) => _db = db;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestUsers.CleanupAsync(_db, CancellationToken.None);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The whole weight of this test is in the last two lines: after five wrong attempts even the
    /// <b>correct</b> password is refused. Asserting only that wrong passwords give 401 would have
    /// passed against the broken implementation too.
    /// </summary>
    [Fact]
    public async Task Bes_hatali_denemeden_sonra_dogru_sifre_de_401_account_locked_donuyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var client = _db.Factory.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Email, WrongPassword)).StatusCode);

        var locked = await LoginAsync(client, user.Email, user.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("account_locked", await CodeOfAsync(locked));
    }

    /// <summary>
    /// A successful login clears the counter. Without this, four near-misses spread over a working
    /// day would eventually add up to a lockout the user cannot explain — and the lockout would
    /// look like a bug rather than a defence.
    /// </summary>
    [Fact]
    public async Task Basarili_giris_hatali_deneme_sayacini_sifirliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var client = _db.Factory.CreateClient();

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < 4; i++)
                await LoginAsync(client, user.Email, WrongPassword);

            var success = await LoginAsync(client, user.Email, user.Password);
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        }
    }

    /// <summary>
    /// A wrong password on an already locked account still says "locked", not "wrong password":
    /// the user needs to know that waiting is the fix, not more guessing.
    /// </summary>
    [Fact]
    public async Task Kilitli_hesapta_yanlis_sifre_de_account_locked_donuyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var client = _db.Factory.CreateClient();

        for (var i = 0; i < 5; i++)
            await LoginAsync(client, user.Email, WrongPassword);

        var afterLock = await LoginAsync(client, user.Email, WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, afterLock.StatusCode);
        Assert.Equal("account_locked", await CodeOfAsync(afterLock));
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
