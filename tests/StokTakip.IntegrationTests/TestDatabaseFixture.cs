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

    public string ConnectionString { get; private set; } = string.Empty;

    private StokTakipFactory? _factory;

    /// <summary>
    /// One application host for the whole run. Booting it per test class would repeat migration
    /// and seeding for no gain — the host is stateless, the database is what carries state.
    /// </summary>
    public StokTakipFactory Factory => _factory ??= new StokTakipFactory(ConnectionString);

    public async ValueTask InitializeAsync()
    {
        ConnectionString = ReadConnectionString();

        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// The database is left behind on purpose so a failed run can be inspected afterwards. The
    /// next run starts by dropping it, so nothing accumulates.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
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
