using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StokTakip.Infrastructure.Data.Seed;

namespace StokTakip.Infrastructure.Data;

/// <summary>
/// Migration and seeding run at startup, which means every replica runs them at the same moment.
/// A PostgreSQL advisory lock turns that race into a queue: the first host migrates, the rest
/// wait and then find nothing left to do.
///
/// It lives here rather than in Program.cs because the lock needs a raw Npgsql connection, and
/// the API layer has no business knowing which database engine is underneath.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Arbitrary, but it has to be identical in every replica or the lock protects nothing.
    /// Advisory locks are scoped to the database, so this number only ever meets itself.
    /// </summary>
    private const long StartupLockKey = 5150726;

    public static async Task InitializeAsync(IServiceProvider sp, bool includeDemoData)
    {
        var context = sp.GetRequiredService<AppDbContext>();

        // The lock is taken on the maintenance database, not the application's own, because on a
        // brand new server the application's database does not exist yet — creating it is part of
        // what MigrateAsync does below. Advisory locks are scoped per database, so every replica
        // has to meet in one that is always there.
        var lockTarget = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString())
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(lockTarget.ConnectionString);
        await connection.OpenAsync();

        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock($1)", connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = StartupLockKey });
            await command.ExecuteNonQueryAsync();
        }

        // No explicit unlock: a session-level advisory lock is released when its connection
        // closes, which the enclosing 'await using' does on every path. Releasing it by hand in a
        // finally block would swallow the real exception whenever migration itself failed.
        await context.Database.MigrateAsync();
        await DbSeeder.SeedAsync(sp, includeDemoData);
    }
}
