using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StokTakip.Infrastructure.Data;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// D27: the application's own DbContext — resolved from the host, so this is the configuration
/// Program.cs actually ships — retries what a moment's wait can fix and nothing else. Measured
/// before it was written: Npgsql's stock strategy also retries command timeouts, and with the
/// defaults (30 s × 7 attempts) one slow query would hold a request for minutes.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DatabaseResilienceTests
{
    private readonly TestDatabaseFixture _db;

    public DatabaseResilienceTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// 57P03 (cannot_connect_now) is what a restarting server says. The function counts its own
    /// calls in a sequence — sequences ignore rollbacks — so the attempts are counted, not
    /// inferred from timing.
    /// </summary>
    [Fact]
    public async Task Gecici_sunucu_hatasi_sinirli_sayida_tekrar_deneniyor()
    {
        await using (var setup = _db.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE SEQUENCE IF NOT EXISTS test_transient_calls;
                CREATE OR REPLACE FUNCTION test_transient() RETURNS int LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM nextval('test_transient_calls');
                    RAISE EXCEPTION 'transient for test' USING ERRCODE = '57P03';
                END $$;
                """, Ct);
        }

        using var scope = _db.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await CallsAsync();

        var error = await Assert.ThrowsAsync<RetryLimitExceededException>(() =>
            db.Database.SqlQueryRaw<int>("SELECT test_transient() AS \"Value\"").ToListAsync(Ct));

        Assert.IsType<PostgresException>(error.InnerException);
        // One attempt plus three retries — bounded, so an outage fails fast instead of piling up.
        Assert.Equal(4, await CallsAsync() - before);
    }

    /// <summary>
    /// A query that ran out of time will run out of time again; repeating it only multiplies the
    /// wait and the load on a database that is already struggling.
    /// </summary>
    [Fact]
    public async Task Zaman_asimi_tekrar_denenmiyor()
    {
        using var scope = _db.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.SetCommandTimeout(1);

        var error = await Record.ExceptionAsync(() =>
            db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM pg_sleep(3)").ToListAsync(Ct));

        var npgsql = Assert.IsType<NpgsqlException>(error);
        Assert.IsType<TimeoutException>(npgsql.InnerException);
    }

    [Fact]
    public void Komut_zaman_asimi_acikca_ayarli()
    {
        using var scope = _db.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(30, db.Database.GetCommandTimeout());
    }

    private async Task<long> CallsAsync()
    {
        await using var db = _db.CreateContext();
        return await db.Database
            .SqlQueryRaw<long>("SELECT CASE WHEN is_called THEN last_value ELSE 0 END AS \"Value\" FROM test_transient_calls")
            .SingleAsync(Ct);
    }
}
