using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace StokTakip.Infrastructure.Data;

/// <summary>
/// D27: retries what a moment's wait can fix — a dropped connection, a server restarting — and
/// nothing else. Npgsql's own strategy also counts a command timeout as transient (measured: a
/// query given 1 s was run three times), and a query that ran out of time will run out of time
/// again; retrying it only multiplies the user's wait and the load on a struggling database.
/// Retrying writes is safe here because the one write whose copy would be wrong — a stock
/// movement — carries an idempotency key the database keeps unique.
/// </summary>
public sealed class TransientRetryStrategy : NpgsqlRetryingExecutionStrategy
{
    public const int MaxRetries = 3;
    public const int CommandTimeoutSeconds = 30;

    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    public TransientRetryStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, MaxRetries, MaxDelay, errorCodesToAdd: null)
    {
    }

    protected override bool ShouldRetryOn(Exception? exception)
        => exception is not NpgsqlException { InnerException: TimeoutException }
           && base.ShouldRetryOn(exception);
}
