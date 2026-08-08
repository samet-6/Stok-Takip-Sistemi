using Microsoft.EntityFrameworkCore;
using Npgsql;
using StokTakip.Infrastructure.Data;
using StokTakip.IntegrationTests.Api;
using Xunit;

namespace StokTakip.IntegrationTests;

/// <summary>
/// Owns the test database for the whole run: drops it, applies migrations, and hands out the
/// connection string. Created once per test collection, not per test class.
/// </summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private const string EnvVariable = "STOKTAKIP_TEST_DB";

    /// <summary>
    /// The only database name this fixture will ever drop. A connection string pointing anywhere
    /// else is refused before a single command runs — the development database and the test
    /// database differ by one word, and the fixture starts by deleting whatever it is pointed at.
    /// </summary>
    private const string RequiredDatabaseName = "stoktakip_test";

    /// <summary>
    /// Arbitrary but fixed, and taken on the maintenance database so two runs meet in the same
    /// place regardless of which test database they are pointed at.
    /// </summary>
    private const long RunLockKey = 5150727;

    public string ConnectionString { get; private set; } = string.Empty;

    private StokTakipFactory? _factory;
    private NpgsqlConnection? _runLock;

    /// <summary>
    /// One application host for the whole run. Booting it per test class would repeat migration
    /// and seeding for no gain — the host is stateless, the database is what carries state.
    /// </summary>
    public StokTakipFactory Factory => _factory ??= new StokTakipFactory(ConnectionString);

    public async ValueTask InitializeAsync()
    {
        ConnectionString = ReadConnectionString();
        _runLock = await AcquireRunLockAsync();

        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        // Booting the host here is what seeds the database, because seeding lives in Program.cs.
        // Left until a test happened to touch the factory, a class that only reads seeded data
        // would pass or fail depending on which class ran first — and running a single test from
        // the IDE would find an empty database.
        _ = Factory.Services;
    }

    /// <summary>
    /// The database is left behind on purpose so a failed run can be inspected afterwards. The
    /// next run starts by dropping it, so nothing accumulates.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        // Closing the connection releases the advisory lock; no explicit unlock needed.
        if (_runLock is not null)
            await _runLock.DisposeAsync();
    }

    /// <summary>
    /// Refuses to start when another run already holds the lock. Without this the second run
    /// simply called <c>EnsureDeleted</c> on the database the first one was using and both
    /// collapsed into unrelated-looking errors — easy to hit here, because Visual Studio's Test
    /// Explorer and the CLI are both in daily use.
    /// <para>
    /// The lock is taken on the maintenance database rather than the test database: the test
    /// database is about to be dropped, and a connection to it would stop that from happening.
    /// It is held for the whole run and released when this connection closes.
    /// </para>
    /// </summary>
    private async Task<NpgsqlConnection> AcquireRunLockAsync()
    {
        var maintenance = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };
        var connection = new NpgsqlConnection(maintenance.ConnectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = RunLockKey });

        if (await command.ExecuteScalarAsync() is not true)
        {
            await connection.DisposeAsync();

            throw new InvalidOperationException(
                "Baska bir test kosusu suruyor. Testler tek bir veritabanini paylasiyor ve kosu " +
                "ilk isi olarak onu SILIYOR; iki kosu ayni anda calisirsa biri digerinin " +
                "verisini yok eder. Once digerinin bitmesini bekleyin (ornegin Visual Studio Test " +
                "Explorer ile komut satiri ayni anda kullanilmis olabilir).");
        }

        return connection;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    private static string ReadConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(EnvVariable);

        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"'{EnvVariable}' ortam degiskeni tanimli degil. Entegrasyon testleri gercek bir " +
                $"PostgreSQL veritabanina baglanir ve baglanti dizesi koda gomulmez. Ornek: " +
                $"Host=localhost;Port=5432;Database={RequiredDatabaseName};Username=...;Password=...");

        var databaseName = ReadDatabaseName(value);

        if (!string.Equals(databaseName, RequiredDatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"'{EnvVariable}' '{databaseName}' veritabanini gosteriyor, ancak testler yalnizca " +
                $"'{RequiredDatabaseName}' uzerinde calisir. Bu kontrol kasitlidir: fixture ilk isi " +
                $"olarak gosterilen veritabanini SILER, dolayisiyla yanlis bir baglanti dizesi " +
                $"gelistirme verisini yok ederdi.");

        return value;
    }

    private static string ReadDatabaseName(string connectionString)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;
        }
        catch (Exception ex)
        {
            // The message deliberately carries no part of the connection string: it holds a password.
            throw new InvalidOperationException(
                $"'{EnvVariable}' gecerli bir PostgreSQL baglanti dizesi degil.", ex);
        }
    }
}
